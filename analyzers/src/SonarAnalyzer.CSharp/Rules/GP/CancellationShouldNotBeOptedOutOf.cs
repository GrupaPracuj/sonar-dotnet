/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationShouldNotBeOptedOutOf : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0116";

    private const string MessageFormat =
        "This call can never be cancelled. Add a CancellationToken parameter to '{0}' and pass it here.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || OptedOutTokenArgument(context.Model, invocation, method) is not { } argument
            // A token that is in scope but not passed is GP0027's finding, not this one: there the fix is to
            // forward what already exists. This rule is about the case that leaves no token to forward.
            || HttpCallShouldPropagateCancellationToken.AvailableCancellationToken(context.Model, invocation) is not null
            || EnclosingMethod(invocation) is not { } enclosing
            || IsNestedFunction(invocation, enclosing)
            || context.Model.GetDeclaredSymbol(enclosing) is not { } enclosingSymbol
            || CannotTakeAToken(enclosingSymbol))
        {
            return;
        }

        context.ReportIssue(Rule, argument, enclosingSymbol.Name);
    }

    private static ArgumentSyntax OptedOutTokenArgument(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        for (var index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
        {
            var argument = invocation.ArgumentList.Arguments[index];
            if (Parameter(method, argument, index) is { } parameter
                && parameter.Type.Is(KnownType.System_Threading_CancellationToken)
                && IsExplicitOptOut(model, argument.Expression))
            {
                return argument;
            }
        }

        return null;
    }

    private static IParameterSymbol Parameter(IMethodSymbol method, ArgumentSyntax argument, int index) =>
        argument.NameColon is { Name.Identifier.ValueText: var name }
            ? method.Parameters.FirstOrDefault(x => x.Name == name)
            : index < method.Parameters.Length ? method.Parameters[index] : null;

    // "CancellationToken.None" and "default" both compile to the same never-cancelled token. Both are written on
    // purpose, so both are a deliberate statement that this call is not cancellable - which is what is being reported.
    private static bool IsExplicitOptOut(SemanticModel model, ExpressionSyntax expression)
    {
        expression = expression.RemoveParentheses() as ExpressionSyntax ?? expression;
        return expression.RawKind is (int)SyntaxKind.DefaultExpression or (int)SyntaxKindEx.DefaultLiteralExpression
               || (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "None" } memberAccess
                   && model.GetSymbolInfo(memberAccess).Symbol is { ContainingType: { } containingType }
                   && containingType.Is(KnownType.System_Threading_CancellationToken));
    }

    private static MethodDeclarationSyntax EnclosingMethod(SyntaxNode node) =>
        node.FirstAncestorOrSelf<MethodDeclarationSyntax>();

    // Registration callbacks, DI factories and other lambdas are handed to a framework that decides their signature,
    // so "add a parameter" is not a fix that applies to them.
    private static bool IsNestedFunction(SyntaxNode node, SyntaxNode enclosing) =>
        node.Ancestors().TakeWhile(x => x != enclosing)
            .Any(x => x is AnonymousFunctionExpressionSyntax || x.Kind() == SyntaxKindEx.LocalFunctionStatement);

    // Entry points and disposal have no caller that could supply a token, so they legitimately opt out.
    private static bool CannotTakeAToken(IMethodSymbol method) =>
        method.IsStatic && method.Name is "Main"
        || method.Name is "Dispose" or "DisposeAsync" or "Finalize";
}
