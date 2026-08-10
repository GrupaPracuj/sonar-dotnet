namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpCallShouldNotBeMadeInALoop : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0096";

    private const string MessageFormat = "This HTTP call runs once per loop iteration - batch the requests or move the call outside the loop.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpLoopCallHelper.IsDirectlyInsideLoop(invocation)
            && context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
            && GpHttpCallHelper.IsHttpCall(method))
        {
            context.ReportIssue(Rule, invocation);
        }
    }
}
