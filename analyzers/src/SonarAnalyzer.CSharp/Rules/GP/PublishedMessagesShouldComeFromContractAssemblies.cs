namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedMessagesShouldComeFromContractAssemblies : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0043";

    private const string MessageFormat = "Publish '{0}' from a contract assembly; it is declared in '{1}'.";
    private const string DefaultContractAssemblyNames = "Contracts";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("contractAssemblyNames", PropertyType.String, "Comma-separated names or suffixes identifying contract assemblies", DefaultContractAssemblyNames)]
    public string ContractAssemblyNames { get; set; } = DefaultContractAssemblyNames;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contractAssemblyNames = GpEntityTypes.SplitParameter(ContractAssemblyNames);
            if (contractAssemblyNames.Length > 0)
            {
                start.RegisterNodeAction(c => AnalyzeInvocation(c, contractAssemblyNames), SyntaxKind.InvocationExpression);
            }
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, string[] contractAssemblyNames)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpMessageContracts.PublishedType(context.Model, invocation) is not { } messageType
            || GpMessageContracts.DescribeShapelessType(messageType) is not null
            || messageType.ContainingAssembly?.Name is not { } assemblyName
            || contractAssemblyNames.Any(x => GpAssemblyNames.Matches(assemblyName, x)))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, messageType.Name, assemblyName);
    }
}
