namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotRedirectToUserControlledUrl : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0031";

    private const string MessageFormat = "Do not redirect to a URL taken from parameter '{0}' - use LocalRedirect or check it with Url.IsLocalUrl first.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // LocalRedirect/LocalRedirectPermanent are absent on purpose: they perform the check themselves.
    private static readonly HashSet<string> UncheckedRedirectMethods = new(StringComparer.Ordinal)
    {
        "Redirect",
        "RedirectPermanent",
        "RedirectPreserveMethod",
        "RedirectPermanentPreserveMethod",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { Arguments.Count: > 0 } argumentList
            || !UncheckedRedirectMethods.Contains(GpCollectionEndpointHelper.GetInvokedMethodName(invocation) ?? string.Empty)
            || argumentList.Arguments[0] is not { } urlArgument
            || GpUrlExpressionHelper.ActionParameterSteeringDestination(context.Model, urlArgument.Expression) is not { } parameterName
            || HasLocalUrlCheck(invocation))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, parameterName);
    }

    // A method that checks any URL for locality is left alone rather than verifying that the check guards this
    // particular redirect - the alternative would report code that already handles the problem.
    private static bool HasLocalUrlCheck(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
        && method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(x => GpCollectionEndpointHelper.GetInvokedMethodName(x) == "IsLocalUrl");
}
