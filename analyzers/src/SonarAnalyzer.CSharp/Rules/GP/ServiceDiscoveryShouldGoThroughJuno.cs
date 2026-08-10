namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServiceDiscoveryShouldGoThroughJuno : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0047";

    private const string MessageFormat = "Resolve the service through Juno instead of querying '{0}' directly.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> AgentDiscoveryMethods = new(StringComparer.Ordinal)
    {
        "CheckDeregister",
        "CheckRegister",
        "ServiceDeregister",
        "ServiceRegister",
    };

    private static readonly HashSet<string> DiscoveryRegistrationTypes = new(StringComparer.Ordinal)
    {
        "Consul.AgentCheckRegistration",
        "Consul.AgentServiceCheck",
        "Consul.AgentServiceRegistration",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (IsInsideJuno(context)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsDiscoveryMethod(method))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.ContainingType.Name);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!IsInsideJuno(context)
            && ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && DiscoveryRegistrationTypes.Contains(type.ToDisplayString())
            && !IsPartOfReportedDiscoveryInvocation(context))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static bool IsPartOfReportedDiscoveryInvocation(SonarSyntaxNodeReportingContext context) =>
        context.Node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Select(x => context.Model.GetSymbolInfo(x).Symbol)
            .OfType<IMethodSymbol>()
            .Any(IsDiscoveryMethod);

    private static bool IsDiscoveryMethod(IMethodSymbol method) =>
        GpJunoTypes.Implements(method.ContainingType, "Consul.ICatalogEndpoint")
        || GpJunoTypes.Implements(method.ContainingType, "Consul.IHealthEndpoint")
        || (GpJunoTypes.Implements(method.ContainingType, "Consul.IAgentEndpoint")
            && AgentDiscoveryMethods.Contains(method.Name));

    // Juno is the layer that is supposed to wrap Consul, so its own code is not reported.
    private static bool IsInsideJuno(SonarSyntaxNodeReportingContext context) =>
        context.Model.GetEnclosingSymbol(context.Node.SpanStart)?.ContainingNamespace?.ToDisplayString() is { } containingNamespace
        && (containingNamespace == "GP.Juno" || containingNamespace.StartsWith("GP.Juno.", StringComparison.Ordinal));
}
