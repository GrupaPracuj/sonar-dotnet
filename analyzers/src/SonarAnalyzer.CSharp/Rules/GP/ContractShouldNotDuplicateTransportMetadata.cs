/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

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

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contracts = GpSemanticContractDetector.GetOrCreate(start.Compilation);
            start.RegisterNodeAction(c => AnalyzeProperty(c, contracts), SyntaxKind.PropertyDeclaration);
            start.RegisterNodeAction(c => AnalyzeRecordParameters(c, contracts), SyntaxKindEx.RecordDeclaration);
        });

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context, GpSemanticContractDetector contracts)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (TransportMetadataNames.Contains(declaration.Identifier.ValueText)
            && context.Model.GetDeclaredSymbol(declaration) is { ContainingType: { } containingType }
            && contracts.IsMessagingContract(containingType)
            && !GpMessageContracts.IsNestedMessageEnvelope(containingType))
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText);
        }
    }

    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context, GpSemanticContractDetector contracts)
    {
        if (context.Node is not TypeDeclarationSyntax declaration
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList)
        {
            return;
        }

        if (context.Model.GetDeclaredSymbol(declaration) is not { } type
            || !contracts.IsMessagingContract(type)
            || GpMessageContracts.IsNestedMessageEnvelope(type))
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters.Where(x => TransportMetadataNames.Contains(x.Identifier.ValueText)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
        }
    }

}
