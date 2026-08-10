namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DatabaseCallShouldNotBeMadeInALoop : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0097";

    private const string MessageFormat = "This database call runs once per loop iteration - batch the calls or move it outside the loop.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.InvocationExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpLoopCallHelper.IsDirectlyInsideLoop(invocation)
            && context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
            && GpDbCallHelper.IsDbCall(method))
        {
            context.ReportIssue(Rule, invocation);
        }
    }
}
