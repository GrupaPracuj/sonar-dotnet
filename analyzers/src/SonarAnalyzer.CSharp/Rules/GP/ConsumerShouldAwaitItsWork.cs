/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConsumerShouldAwaitItsWork : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0053";

    private const string MessageFormat = "Await this call - the message is acknowledged when Consume returns, so work nothing awaits is lost without a trace.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
        context.RegisterNodeAction(AnalyzeDiscard, SyntaxKind.SimpleAssignmentExpression);
    }

    // A bare "SomethingAsync();" statement: the task is produced and dropped.
    private static void AnalyzeExpressionStatement(SonarSyntaxNodeReportingContext context)
    {
        var statement = (ExpressionStatementSyntax)context.Node;
        if (statement.Expression is InvocationExpressionSyntax invocation
            && ReturnsTask(context.Model, invocation)
            && GpMessageContracts.IsInsideConsumer(context.Model, invocation))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    // "_ = SomethingAsync();" - the discard makes it explicit, but the work is just as unobserved.
    private static void AnalyzeDiscard(SonarSyntaxNodeReportingContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "_" }
            && assignment.Right is InvocationExpressionSyntax invocation
            && ReturnsTask(context.Model, invocation)
            && GpMessageContracts.IsInsideConsumer(context.Model, invocation))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    private static bool ReturnsTask(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ReturnType: { } returnType }
        && (returnType.Is(KnownType.System_Threading_Tasks_Task)
            || returnType.Is(KnownType.System_Threading_Tasks_ValueTask)
            || (returnType as INamedTypeSymbol)?.ConstructedFrom.IsAny(
                KnownType.System_Threading_Tasks_Task_T,
                KnownType.System_Threading_Tasks_ValueTask_TResult) == true);
}
