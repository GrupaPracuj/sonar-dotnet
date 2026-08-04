namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotCarrySecrets : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0044";

    private const string MessageFormat = "'{0}' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly string[] ContractNameSuffixes = { "Dto", "Request", "Response", "Contract", "Event", "Command", "Message" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeRecordParameters, SyntaxKindEx.RecordDeclaration);
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (IsContractMember(declaration) && GpIdentifierWords.ContainsSecretWord(declaration.Identifier.ValueText))
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText);
        }
    }

    // A positional record declares its members in the parameter list, so those need checking too.
    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName } declaration
            || !HasContractName(typeName)
            || ParameterList(declaration) is not { } parameterList)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters.Where(x => GpIdentifierWords.ContainsSecretWord(x.Identifier.ValueText)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
        }
    }

    private static ParameterListSyntax ParameterList(TypeDeclarationSyntax declaration) =>
        RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            ? ((RecordDeclarationSyntaxWrapper)declaration).ParameterList
            : null;

    private static bool IsContractMember(MemberDeclarationSyntax member) =>
        member.Parent is TypeDeclarationSyntax { Identifier.ValueText: var typeName } && HasContractName(typeName);

    private static bool HasContractName(string typeName) =>
        Array.Exists(ContractNameSuffixes, x => typeName.EndsWith(x, StringComparison.Ordinal));
}
