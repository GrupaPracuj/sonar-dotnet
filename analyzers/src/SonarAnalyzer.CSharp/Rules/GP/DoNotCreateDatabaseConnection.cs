/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotCreateDatabaseConnection : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0035";

    private const string MessageFormat = "Perform database access through Juno IDbExecute instead of using '{0}' directly.";
    private const string TransactionMessage = "Pass the IDbExecute transaction to this Dapper operation.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly DiagnosticDescriptor TransactionRule = DescriptorFactory.Create(RuleId, TransactionMessage);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule, TransactionRule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && GpJunoTypes.DerivesFrom(type, "System.Data.Common.DbConnection"))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (IsInsideJuno(context)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (IsDapperDatabaseOperation(method)
            && context.Model.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType is { } containingType
            && ControllersShouldNotUseInfrastructureDirectly.IsDbExecute(containingType))
        {
            if (AvailableTransaction(context.Model, invocation) is { } transaction
                && PassesTransaction(context.Model, invocation, method, transaction) == false)
            {
                context.ReportIssue(TransactionRule, invocation);
            }
        }
        else if (IsDapperDatabaseOperation(method))
        {
            context.ReportIssue(Rule, invocation, $"Dapper.{method.Name}");
        }
        else if (method.Name == "CreateConnection"
                 && GpJunoTypes.DerivesFrom(method.ContainingType, "System.Data.Common.DbProviderFactory"))
        {
            context.ReportIssue(Rule, invocation, $"{method.ContainingType.Name}.{method.Name}");
        }
    }

    private static bool IsDapperDatabaseOperation(IMethodSymbol method) =>
        (method.ContainingType.Is(KnownType.Dapper_SqlMapper)
         || method.ReducedFrom?.ContainingType.Is(KnownType.Dapper_SqlMapper) == true)
        && (method.Name.StartsWith("Query", StringComparison.Ordinal)
            || method.Name.StartsWith("Execute", StringComparison.Ordinal));

    private static IParameterSymbol AvailableTransaction(SemanticModel model, SyntaxNode node)
    {
        for (var symbol = model.GetEnclosingSymbol(node.SpanStart); symbol is IMethodSymbol method; symbol = method.ContainingSymbol)
        {
            if (method.Parameters.FirstOrDefault(x => IsDbTransaction(x.Type)) is { } transaction)
            {
                return transaction;
            }
        }

        return null;
    }

    private static bool? PassesTransaction(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        IParameterSymbol availableTransaction)
    {
        var mappings = new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings().ToArray();
        if (method.Parameters.Any(x => x.Name == "transaction" && IsDbTransaction(x.Type)))
        {
            return mappings
                .Where(x => x.Symbol.Name == "transaction" && IsDbTransaction(x.Symbol.Type))
                .Select(x => x.Node?.Expression)
                .Any(x => IsExactParameter(model, x, availableTransaction));
        }

        var command = mappings
            .FirstOrDefault(x => x.Symbol.Type.Is(KnownType.Dapper_CommandDefinition))
            .Node?.Expression;
        return CommandCreation(model, command) is { ArgumentList: { } arguments } creation
               && creation.MethodSymbol(model) is { } constructor
            ? new CSharpMethodParameterLookup(arguments, constructor).GetAllArgumentParameterMappings()
                .Where(x => x.Symbol.Name == "transaction" && IsDbTransaction(x.Symbol.Type))
                .Select(x => x.Node?.Expression)
                .Any(x => IsExactParameter(model, x, availableTransaction))
            : null;
    }

    private static IObjectCreation CommandCreation(SemanticModel model, ExpressionSyntax expression)
    {
        if (expression is null)
        {
            return null;
        }

        if (ObjectCreationFactory.TryCreate(expression) is { } creation)
        {
            return creation;
        }

        return model.GetSymbolInfo(expression).Symbol is ILocalSymbol local
            ? local.DeclaringSyntaxReferences
                .Select(x => x.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .Select(x => x.Initializer?.Value)
                .Select(ObjectCreationFactory.TryCreate)
                .WhereNotNull()
                .SingleOrDefault()
            : null;
    }

    private static bool IsExactParameter(SemanticModel model, ExpressionSyntax expression, IParameterSymbol expected) =>
        expression is not null && expected.Equals(model.GetSymbolInfo(expression).Symbol);

    private static bool IsDbTransaction(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "IDbTransaction" } named
        && named.ContainingNamespace.ToDisplayString() == "System.Data";

    private static bool IsInsideJuno(SonarSyntaxNodeReportingContext context)
    {
        var containingNamespace = context.Model.GetEnclosingSymbol(context.Node.SpanStart)?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return containingNamespace == "GP.Juno" || containingNamespace.StartsWith("GP.Juno.", StringComparison.Ordinal);
    }
}
