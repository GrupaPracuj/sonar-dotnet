/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UniqueConstraintCatchShouldCoverSingleOperation : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0111";

    private const string MessageFormat = "Catch this unique-constraint violation around one database operation; this try scope executes {0}.";
    private const string JunoExecuteExtensions = "GP.Juno.Abstractions.Ado.TransactionExtensions";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> SqlExceptionTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.Data.SqlClient.SqlException",
        "System.Data.SqlClient.SqlException",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeCatch, SyntaxKind.CatchClause);

    private static void AnalyzeCatch(SonarSyntaxNodeReportingContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Parent is not TryStatementSyntax tryStatement
            || catchClause.Declaration is not { } declaration
            || context.Model.GetDeclaredSymbol(declaration) is not ILocalSymbol exception
            || !SqlExceptionTypes.Contains(exception.Type.ToDisplayString())
            || !DiscriminatesUniqueConstraint(context.Model, catchClause, exception))
        {
            return;
        }

        var operations = DatabaseOperations(context.Model, tryStatement.Block).Count();
        if (operations > 1)
        {
            context.ReportIssue(Rule, catchClause.CatchKeyword, operations.ToString());
        }
    }

    // A when filter is one way to single the violation out; testing ex.Number inside the catch is just as common and
    // means the same thing. Requiring the filter left that half of real code unreported.
    private static bool DiscriminatesUniqueConstraint(SemanticModel model, CatchClauseSyntax catchClause, ILocalSymbol exception)
    {
        if (catchClause.Filter?.FilterExpression is { } filter)
        {
            return IsUniqueConstraintFilter(model, filter, exception);
        }

        return catchClause.Block is { } block
               && block.DescendantNodes()
                   .OfType<IfStatementSyntax>()
                   .Any(x => IsUniqueConstraintFilter(model, x.Condition, exception));
    }

    private static bool IsUniqueConstraintFilter(SemanticModel model, ExpressionSyntax filter, ILocalSymbol exception) =>
        HasUniqueConstraintNumber(model, filter, exception)
        || filter.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(x => IsUniqueConstraintHelper(model, x, exception));

    private static bool HasUniqueConstraintNumber(SemanticModel model, ExpressionSyntax filter, ILocalSymbol exception) =>
        filter.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(x => x.Name.Identifier.ValueText == "Number"
                      && model.GetSymbolInfo(x.Expression).Symbol?.Equals(exception) == true
                      && model.GetSymbolInfo(x).Symbol is IPropertySymbol property
                      && SqlExceptionTypes.Contains(property.ContainingType.ToDisplayString())
                      && IsUniqueConstraintComparison(model, x));

    private static bool IsUniqueConstraintComparison(SemanticModel model, MemberAccessExpressionSyntax numberAccess)
    {
        ExpressionSyntax current = numberAccess;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized && parenthesized.Expression == current)
        {
            current = parenthesized;
        }

        if (current.Parent is { } parent && IsPatternExpressionSyntaxWrapper.IsInstance(parent))
        {
            var isPattern = (IsPatternExpressionSyntaxWrapper)parent;
            return isPattern.Expression == current
                && isPattern.Pattern.WrappedInstance.DescendantNodesAndSelf()
                    .OfType<ExpressionSyntax>()
                    .Any(x => IsUniqueConstraintNumber(model, x));
        }

        return current.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression } binary
            && (binary.Left == current && IsUniqueConstraintNumber(model, binary.Right)
                || binary.Right == current && IsUniqueConstraintNumber(model, binary.Left));
    }

    private static bool IsUniqueConstraintNumber(SemanticModel model, ExpressionSyntax expression) =>
        model.GetConstantValue(expression) is { HasValue: true, Value: 2601 or 2627 };

    private static bool IsUniqueConstraintHelper(SemanticModel model, InvocationExpressionSyntax invocation, ILocalSymbol exception) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
        {
            Name: "IsUniqueConstraintViolation",
            ContainingType.Name: "SqlServerErrors",
            ReturnType.SpecialType: SpecialType.System_Boolean,
        }
        && invocation.ArgumentList.Arguments.Any(x =>
            x.Expression.DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Any(y => model.GetSymbolInfo(y).Symbol?.Equals(exception) == true));

    private static IEnumerable<InvocationExpressionSyntax> DatabaseOperations(SemanticModel model, BlockSyntax tryBlock)
    {
        var directInvocations = tryBlock.DescendantNodesAndSelf(DoesNotBelongToNestedFunction)
            .OfType<InvocationExpressionSyntax>()
            .ToArray();

        foreach (var invocation in directInvocations.Where(x => IsJunoExecute(model, x)))
        {
            yield return invocation;
        }

        foreach (var transaction in directInvocations.Where(x => IsRunInTransaction(model, x)))
        {
            foreach (var invocation in CallbackInvocations(transaction).Where(x => IsJunoExecute(model, x)))
            {
                yield return invocation;
            }
        }
    }

    private static bool IsJunoExecute(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Execute" } method
        && (method.ReducedFrom ?? method).ContainingType?.ToDisplayString() == JunoExecuteExtensions;

    private static bool IsRunInTransaction(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "RunInTransaction" } method)
        {
            return false;
        }

        var original = method.ReducedFrom ?? method;
        return original.ContainingType?.Name.IndexOf("TransactionalExtensions", StringComparison.OrdinalIgnoreCase) >= 0
            || original.ContainingNamespace?.ToDisplayString().StartsWith("GP.Juno.Ado", StringComparison.Ordinal) == true;
    }

    private static IEnumerable<InvocationExpressionSyntax> CallbackInvocations(InvocationExpressionSyntax invocation)
    {
        var callback = invocation.ArgumentList.Arguments
            .Select(x => x.Expression)
            .FirstOrDefault(x => x is AnonymousFunctionExpressionSyntax);
        var body = callback switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body,
            SimpleLambdaExpressionSyntax simple => simple.Body,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            _ => null,
        };
        return body?.DescendantNodesAndSelf(DoesNotBelongToNestedFunction).OfType<InvocationExpressionSyntax>()
            ?? Enumerable.Empty<InvocationExpressionSyntax>();
    }

    private static bool DoesNotBelongToNestedFunction(SyntaxNode node) =>
        node.Kind() != SyntaxKindEx.LocalFunctionStatement && node is not AnonymousFunctionExpressionSyntax;
}
