namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotDuplicateTransportMetadata : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0061";

    private const string MessageFormat = "'{0}' duplicates transport metadata - read it from the consume context instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Exact names only. A domain identifier is named after the thing it identifies, so "OrderId" and "ProcessId" do
    // not collide with this list even though they end the same way.
    private static readonly HashSet<string> TransportMetadataNames = new(StringComparer.Ordinal)
    {
        "MessageId",
        "ConversationId",
        "CorrelationId",
        "RequestId",
        "InitiatorId",
        "SentTime",
        "SourceAddress",
        "DestinationAddress",
        "ResponseAddress",
        "FaultAddress",
        "HostInfo",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeRecordParameters, SyntaxKindEx.RecordDeclaration);
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (GpMessageContracts.IsContractMember(declaration) && TransportMetadataNames.Contains(declaration.Identifier.ValueText))
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText);
        }
    }

    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName } declaration
            || !GpMessageContracts.HasContractName(typeName)
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters.Where(x => TransportMetadataNames.Contains(x.Identifier.ValueText)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
        }
    }
}
