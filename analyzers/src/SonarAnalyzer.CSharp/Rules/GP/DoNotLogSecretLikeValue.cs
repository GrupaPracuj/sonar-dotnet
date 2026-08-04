namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotLogSecretLikeValue : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0019";

    private const string MessageFormat = "Do not log '{0}' - its name suggests it holds a secret.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { } argumentList || !GpLoggingHelper.IsLoggingCall(context.Model, invocation))
        {
            return;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (GpLoggingHelper.CandidateNames(argument.Expression).FirstOrDefault(GpIdentifierWords.ContainsSecretWord) is { } name)
            {
                context.ReportIssue(Rule, argument, name);
                return; // one finding per logging call is enough
            }
        }
    }
}
