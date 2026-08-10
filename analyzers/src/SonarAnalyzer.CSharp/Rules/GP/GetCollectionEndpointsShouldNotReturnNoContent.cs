namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GetCollectionEndpointsShouldNotReturnNoContent : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0009";

    private const string MessageFormat = "GET endpoints returning collections should return 200 with an empty collection instead of 204.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeReturnStatement, SyntaxKind.ReturnStatement);
        context.RegisterNodeAction(AnalyzeMinimalApiResult, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeReturnStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not ReturnStatementSyntax { Expression: InvocationExpressionSyntax invocation }
            || context.Model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol method
            || !GpCollectionEndpointHelper.IsHttpGetMethod(method)
            || !GpCollectionEndpointHelper.ReturnsCollection(method, context.Model, context.Node)
            || !GpMvcResults.IsStatusResponse(context.Model, invocation, "NoContent", 204))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static void AnalyzeMinimalApiResult(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!GpMvcResults.IsStatusResponse(context.Model, invocation, "NoContent", 204)
            || !GpMinimalApi.TryGetInlineHandler(invocation, context.Model, "MapGet", out var handler, out _, out _, out _)
            || !GpMinimalApi.HandlerReturnsCollection(handler, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }
}
