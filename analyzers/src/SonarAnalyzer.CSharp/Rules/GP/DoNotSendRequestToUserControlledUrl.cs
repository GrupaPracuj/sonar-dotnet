namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotSendRequestToUserControlledUrl : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0028";

    private const string MessageFormat = "Do not send a request to a URL taken from parameter '{0}' - validate the host against an allowlist first.";
    private const string HttpRequestMessage = "System.Net.Http.HttpRequestMessage";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Types whose URL parameters are request destinations. The Juno HTTP client is intentionally absent:
    // its fluent chain takes a service name and path segments resolved from configuration, not a caller-supplied URL.
    private static readonly HashSet<string> RequestSendingTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpMessageInvoker",
        "System.Net.WebClient",
        "System.Net.WebRequest",
        "System.Net.Http.Json.HttpClientJsonExtensions",
    };
    private static readonly HashSet<string> UrlParameterNames = new(StringComparer.Ordinal)
    {
        "address",
        "requestUri",
        "requestUriString",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList.Arguments.Count == 0
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsRequestSendingCall(method)
            || UrlExpression(context.Model, invocation, method) is not { } urlExpression
            || GpUrlExpressionHelper.ActionParameterSteeringDestination(context.Model, urlExpression) is not { } parameterName)
        {
            return;
        }

        context.ReportIssue(Rule, urlExpression, parameterName);
    }

    private static bool IsRequestSendingCall(IMethodSymbol method) =>
        RequestSendingTypes.Contains(method.ContainingType?.ToDisplayString() ?? string.Empty)
        || (method.IsExtensionMethod && RequestSendingTypes.Contains(method.ReceiverType?.ToDisplayString() ?? string.Empty));

    private static ExpressionSyntax UrlExpression(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        var lookup = new CSharpMethodParameterLookup(invocation, method);
        var directUrl = lookup.GetAllArgumentParameterMappings()
            .FirstOrDefault(x => UrlParameterNames.Contains(x.Symbol.Name)
                                 && (x.Symbol.Type.Is(KnownType.System_String) || x.Symbol.Type.Is(KnownType.System_Uri)));
        if (directUrl.Node is not null)
        {
            return directUrl.Node.Expression;
        }

        var request = lookup.GetAllArgumentParameterMappings()
            .FirstOrDefault(x => method.Name == "SendAsync" && x.Symbol.Type.ToDisplayString() == HttpRequestMessage);
        return request.Node is null ? null : RequestUriExpression(model, request.Node.Expression);
    }

    private static ExpressionSyntax RequestUriExpression(SemanticModel model, ExpressionSyntax expression)
    {
        if (!ObjectCreationFactory.TryCreate(expression, out var creation)
            || creation.TypeSymbol(model)?.ToDisplayString() != HttpRequestMessage)
        {
            return null;
        }

        if (creation.MethodSymbol(model) is { } constructor
            && creation.ArgumentList is { } argumentList)
        {
            var lookup = new CSharpMethodParameterLookup(argumentList, constructor);
            var requestUri = lookup.GetAllArgumentParameterMappings()
                .FirstOrDefault(x => x.Symbol.Name == "requestUri"
                                     && (x.Symbol.Type.Is(KnownType.System_String) || x.Symbol.Type.Is(KnownType.System_Uri)));
            if (requestUri.Node is not null)
            {
                return requestUri.Node.Expression;
            }
        }

        return creation.InitializerExpressions?
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(x => model.GetSymbolInfo(x.Left).Symbol is IPropertySymbol
            {
                Name: "RequestUri",
                ContainingType: { } containingType,
                Type: { } propertyType,
            }
            && containingType.ToDisplayString() == HttpRequestMessage
            && propertyType.Is(KnownType.System_Uri))
            ?.Right;
    }
}
