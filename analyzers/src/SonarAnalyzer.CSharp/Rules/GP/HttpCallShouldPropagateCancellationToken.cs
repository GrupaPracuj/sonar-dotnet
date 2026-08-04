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
            || AlreadyPassesCancellationToken(context.Model, invocation)
            || !HasAvailableCancellationToken(context.Model, invocation))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool AlreadyPassesCancellationToken(SemanticModel model, InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments.Any(x => model.GetTypeInfo(x.Expression).Type.Is(KnownType.System_Threading_CancellationToken));

    // Only looks at the immediately enclosing method's own parameters - a token available via a field or a
    // wrapping local function is not considered "available" for this check.
    private static bool HasAvailableCancellationToken(SemanticModel model, SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } methodDeclaration
        && model.GetDeclaredSymbol(methodDeclaration) is IMethodSymbol method
        && method.Parameters.Any(x => x.Type.Is(KnownType.System_Threading_CancellationToken));
}
