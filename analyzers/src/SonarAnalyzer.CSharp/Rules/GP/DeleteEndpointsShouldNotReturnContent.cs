namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeleteEndpointsShouldNotReturnContent : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0024";

    private const string MessageFormat = "DELETE endpoints should return 204 (NoContent) instead of 200 with a response body.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeReturnStatement, SyntaxKind.ReturnStatement);

    private static void AnalyzeReturnStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not ReturnStatementSyntax { Expression: InvocationExpressionSyntax invocation }
            || context.Model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol method
            || !IsHttpDeleteMethod(method)
            || GpCollectionEndpointHelper.GetInvokedMethodName(invocation) != "Ok"
            || invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool IsHttpDeleteMethod(IMethodSymbol method) =>
        method.IsControllerActionMethod()
        && method.GetAttributes().Select(x => x.AttributeClass?.Name).Any(x => x is "HttpDelete" or "HttpDeleteAttribute");
}
