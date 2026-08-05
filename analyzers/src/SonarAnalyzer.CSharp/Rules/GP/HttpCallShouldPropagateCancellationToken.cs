namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpCallShouldPropagateCancellationToken : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0027";

    private const string MessageFormat = "Pass the available CancellationToken to this call to another service, so it can be cancelled or time out.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !GpHttpCallHelper.IsHttpCall(method)
            || !HasCancellationTokenOverload(method)
            || AlreadyPassesCancellationToken(context.Model, invocation)
            || !HasAvailableCancellationToken(context.Model, invocation))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    // GpHttpCallHelper.IsHttpCall recognizes any call to a known HTTP-ish type - including the GP.Juno fluent HTTP API
    // (IHttpClient, IHttpClientBuilder, HttpRequestProperties) - because that same broad detection is also shared by
    // GP0007 (SharedDictionariesShouldUseJunoDictionaries) and DatabaseTransactionsShouldNotContainExternalNetworkCalls,
    // which only need to know "is this an outgoing HTTP call", not whether it can be cancelled.
    // For GP0027 specifically, a call is only actionable if it can actually be fixed. Verified against the
    // submodules/juno source: none of the GP.Juno fluent API surface (IHttpClient.Send, IHttpClientBuilder.Service,
    // nor any HttpRequestProperties extension such as GetJson/PostJson/PutJson/PatchJson/Delete/...) exposes an
    // overload accepting a CancellationToken anywhere, so those calls can never propagate one and must not be
    // reported. A call is only reported when another member sharing its name in the same containing type - a sibling
    // overload for instance methods, or a sibling extension method for extension methods - actually accepts one.
    private static bool HasCancellationTokenOverload(IMethodSymbol method) =>
        method.ContainingType is { } containingType
        && containingType.GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Any(x => x.Parameters.Any(p => p.Type.Is(KnownType.System_Threading_CancellationToken)));

    private static bool AlreadyPassesCancellationToken(SemanticModel model, InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments.Any(x => model.GetTypeInfo(x.Expression).Type.Is(KnownType.System_Threading_CancellationToken));

    // Only looks at the immediately enclosing method's own parameters - a token available via a field or a
    // wrapping local function is not considered "available" for this check.
    private static bool HasAvailableCancellationToken(SemanticModel model, SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } methodDeclaration
        && model.GetDeclaredSymbol(methodDeclaration) is IMethodSymbol method
        && method.Parameters.Any(x => x.Type.Is(KnownType.System_Threading_CancellationToken));
}
