namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationShouldBeBoundToTypedClass : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0039";

    private const string MessageFormat = "Bind configuration to a typed class instead of reading it by key.";

    private const string ConfigurationInterface = "Microsoft.Extensions.Configuration.IConfiguration";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeElementAccess, SyntaxKind.ElementAccessExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // configuration["Orders:BaseUrl"]
    private static void AnalyzeElementAccess(SonarSyntaxNodeReportingContext context)
    {
        var elementAccess = (ElementAccessExpressionSyntax)context.Node;
        if (IsConfiguration(context.Model.GetTypeInfo(elementAccess.Expression).Type))
        {
            context.ReportIssue(Rule, elementAccess);
        }
    }

    // configuration.GetValue<int>("Orders:Timeout") - typed, but still one value looked up by key.
    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "GetValue" } method)
        {
            return;
        }

        // GetValue is an extension method on IConfiguration, so the receiver carries the type.
        if (IsConfiguration(method.ReceiverType) || (method.Parameters.Length > 0 && IsConfiguration(method.Parameters[0].Type)))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    // GetSection(...) is not reported: it is how a section is selected before being bound with Get<T>()/Bind(...),
    // which is the pattern this rule steers towards.
    private static bool IsConfiguration(ITypeSymbol type) =>
        GpJunoTypes.Implements(type, ConfigurationInterface);
}
