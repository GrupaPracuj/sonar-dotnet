namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotSendRequestToUserControlledUrl : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0028";

    private const string MessageFormat = "Do not send a request to a URL taken from parameter '{0}' - validate the host against an allowlist first.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Types where the first argument of the call is the request URI. The Juno HTTP client is intentionally absent:
    // its fluent chain takes a service name and path segments resolved from configuration, not a caller-supplied URL.
    private static readonly HashSet<string> RequestSendingTypes = new(StringComparer.Ordinal)
    {
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpMessageInvoker",
        "System.Net.WebClient",
        "System.Net.WebRequest",
        "System.Net.Http.Json.HttpClientJsonExtensions",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { Arguments.Count: > 0 } argumentList
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsRequestSendingCall(method)
            || FirstUrlArgument(context.Model, argumentList) is not { } urlArgument
            || GpUrlExpressionHelper.ActionParameterSteeringDestination(context.Model, urlArgument.Expression) is not { } parameterName)
        {
            return;
        }

        context.ReportIssue(Rule, urlArgument, parameterName);
    }

    private static bool IsRequestSendingCall(IMethodSymbol method) =>
        RequestSendingTypes.Contains(method.ContainingType?.ToDisplayString() ?? string.Empty)
        || (method.IsExtensionMethod && RequestSendingTypes.Contains(method.ReceiverType?.ToDisplayString() ?? string.Empty));

    // All the APIs covered here take the destination as their first argument - HttpClient.GetAsync(requestUri),
    // WebClient.DownloadString(address), WebRequest.Create(requestUriString). Later arguments carry the payload,
    // which is expected to come from the request and is not what this rule is about.
    private static ArgumentSyntax FirstUrlArgument(SemanticModel model, ArgumentListSyntax argumentList) =>
        argumentList.Arguments[0] is { } argument
        && model.GetTypeInfo(argument.Expression).Type is { } type
        && (type.Is(KnownType.System_String) || type.Is(KnownType.System_Uri))
            ? argument
            : null;
}
