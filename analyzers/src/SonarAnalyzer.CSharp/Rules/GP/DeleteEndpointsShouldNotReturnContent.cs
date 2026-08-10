namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeleteEndpointsShouldNotReturnContent : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0024";

    private const string MessageFormat = "DELETE endpoints should return 204 (NoContent) instead of 200 with a response body.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> BodyProducingResultMethods = new(StringComparer.Ordinal)
    {
        "Content",
        "Json",
        "Text",
    };

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
            || !IsHttpDeleteMethod(method)
            || !GpMvcResults.IsResponseFactory(context.Model, invocation, "Ok")
            || invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static void AnalyzeMinimalApiResult(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!GpMinimalApi.TryGetResultMethod(context.Model, invocation, out var method)
            || !ProducesStatus200Body(context.Model, invocation, method)
            || !GpMinimalApi.TryGetInlineHandler(invocation, context.Model, "MapDelete", out _, out _, out _, out _))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool ProducesStatus200Body(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.Name == "Ok")
        {
            return invocation.ArgumentList.Arguments.Count > 0;
        }

        if (!BodyProducingResultMethods.Contains(method.Name))
        {
            return false;
        }

        var statusCode = new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings()
            .FirstOrDefault(x => x.Symbol.Name == "statusCode");
        if (statusCode.Node is null)
        {
            return true;
        }

        var constant = model.GetConstantValue(statusCode.Node.Expression);
        return constant.HasValue && (constant.Value is null || constant.Value is int value && value == 200);
    }

    private static bool IsHttpDeleteMethod(IMethodSymbol method) =>
        method.IsControllerActionMethod()
        && method.GetAttributes().Select(x => x.AttributeClass?.Name).Any(x => x is "HttpDelete" or "HttpDeleteAttribute");
}
