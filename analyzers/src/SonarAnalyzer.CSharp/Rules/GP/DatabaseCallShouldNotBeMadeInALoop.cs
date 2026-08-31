/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DatabaseCallShouldNotBeMadeInALoop : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0126";

    private const string MessageFormat = "This database call depends on the loop variable and runs once per iteration - query in a batch or move the call outside the loop.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpLoopCallHelper.DependsOnDirectLoopVariable(invocation, context.Model)
            && context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
            && GpDatabaseCallHelper.IsDatabaseCall(context.Model, invocation, method)
            && (GpSynchronousApiReachability.IsReachable(context.Model, invocation)
                || GpLoopCallHelper.IteratesFetchedSequence(invocation, context.Model)))
        {
            context.ReportIssue(Rule, invocation);
        }
    }
}
