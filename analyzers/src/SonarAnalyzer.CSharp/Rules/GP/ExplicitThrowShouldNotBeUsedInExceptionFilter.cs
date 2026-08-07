namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitThrowShouldNotBeUsedInExceptionFilter : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0093";

    private const string MessageFormat = "Remove this throw from the exception filter; the CLR silently treats the filter as false when it throws.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeThrow, SyntaxKindEx.ThrowExpression);

    private static void AnalyzeThrow(SonarSyntaxNodeReportingContext context)
    {
        var ancestors = context.Node.Ancestors().TakeWhile(x => x is not CatchFilterClauseSyntax).ToArray();
        if (context.Node.Ancestors().OfType<CatchFilterClauseSyntax>().Any()
            && !ancestors.Any(x => x is AnonymousFunctionExpressionSyntax))
        {
            context.ReportIssue(Rule, context.Node);
        }
    }
}
