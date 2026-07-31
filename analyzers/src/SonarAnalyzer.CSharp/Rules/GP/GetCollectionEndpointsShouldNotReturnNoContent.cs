namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GetCollectionEndpointsShouldNotReturnNoContent : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0009";

    private const string MessageFormat = "GET endpoints returning collections should return 200 with an empty collection instead of 204.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeReturnStatement, SyntaxKind.ReturnStatement);

    private static void AnalyzeReturnStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not ReturnStatementSyntax { Expression: InvocationExpressionSyntax invocation }
            || context.Model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol method
            || !GpCollectionEndpointHelper.IsHttpGetMethod(method)
            || !GpCollectionEndpointHelper.ReturnsCollection(method, context.Model, context.Node)
            || !IsNoContentResponse(context.Model, invocation))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool IsNoContentResponse(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        var methodName = GpCollectionEndpointHelper.GetInvokedMethodName(invocation);

        if (methodName == "NoContent")
        {
            return true;
        }

        if (methodName != "StatusCode"
            || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not ExpressionSyntax codeExpression
            || model.GetConstantValue(codeExpression) is not { HasValue: true, Value: int statusCode })
        {
            return false;
        }

        return statusCode == 204;
    }
}
