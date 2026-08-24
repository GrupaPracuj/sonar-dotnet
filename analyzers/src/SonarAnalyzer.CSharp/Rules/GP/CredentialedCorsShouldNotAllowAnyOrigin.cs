/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CredentialedCorsShouldNotAllowAnyOrigin : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0123";

    private const string MessageFormat = "Restrict credentialed CORS requests to explicit trusted origins.";
    private const string CorsPolicyBuilder = "Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (IsBuilderMethod(invocation, "SetIsOriginAllowed", context.Model)
            && HasAlwaysTruePredicate(invocation, context.Model)
            && FluentChain(invocation).Any(x => IsBuilderMethod(x, "AllowCredentials", context.Model)))
        {
            context.ReportIssue(
                Rule,
                invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Name : invocation.Expression);
        }
    }

    private static bool HasAlwaysTruePredicate(InvocationExpressionSyntax invocation, SemanticModel model) =>
        invocation.ArgumentList.Arguments.Count == 1
        && invocation.ArgumentList.Arguments[0].Expression.RemoveParentheses() switch
        {
            LambdaExpressionSyntax { Body: ExpressionSyntax expression } => IsConstantTrue(expression, model),
            LambdaExpressionSyntax { Body: BlockSyntax block } => IsSingleConstantTrueReturn(block, model),
            AnonymousMethodExpressionSyntax { Block: { } block } => IsSingleConstantTrueReturn(block, model),
            _ => false,
        };

    private static bool IsSingleConstantTrueReturn(BlockSyntax block, SemanticModel model) =>
        block.Statements.Count == 1
        && block.Statements[0] is ReturnStatementSyntax { Expression: { } expression }
        && IsConstantTrue(expression, model);

    private static bool IsConstantTrue(ExpressionSyntax expression, SemanticModel model) =>
        model.GetConstantValue(expression) is { HasValue: true, Value: true };

    private static bool IsBuilderMethod(InvocationExpressionSyntax invocation, string methodName, SemanticModel model) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol
        {
            Name: var name,
            ContainingType: { } containingType,
        }
        && name == methodName
        && containingType.ToDisplayString() == CorsPolicyBuilder;

    private static IEnumerable<InvocationExpressionSyntax> FluentChain(InvocationExpressionSyntax invocation)
    {
        var top = invocation;
        while (top.Parent is MemberAccessExpressionSyntax { Expression: var receiver }
               && receiver == top
               && top.Parent.Parent is InvocationExpressionSyntax parentInvocation)
        {
            top = parentInvocation;
        }

        for (ExpressionSyntax current = top;
             current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } currentInvocation;
             current = (ExpressionSyntax)memberAccess.Expression.RemoveParentheses())
        {
            yield return currentInvocation;
        }
    }
}
