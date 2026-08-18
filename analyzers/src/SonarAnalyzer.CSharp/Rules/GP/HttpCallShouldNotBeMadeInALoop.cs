/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpCallShouldNotBeMadeInALoop : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0096";

    private const string MessageFormat = "This HTTP call directly depends on the loop variable and runs once per iteration - batch the requests or move the call outside the loop.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpLoopCallHelper.DependsOnDirectLoopVariable(invocation, context.Model)
            && context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
            && GpHttpCallHelper.IsHttpCall(method))
        {
            context.ReportIssue(Rule, invocation);
        }
    }
}
