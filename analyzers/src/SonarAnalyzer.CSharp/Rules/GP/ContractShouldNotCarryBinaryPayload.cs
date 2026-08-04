namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotCarryBinaryPayload : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0050";

    private const string MessageFormat = "'{0}' puts binary content on the broker - publish a reference to it instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Names that say "this holds the content", for the cases where the type alone does not (a base64 string).
    private static readonly HashSet<string> BinaryMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FileContent",
        "AttachmentContent",
        "BinaryData",
        "FileBytes",
        "ContentBytes",
        "RawContent",
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
        if (GpMessageContracts.IsContractMember(declaration)
            && IsBinaryPayload(declaration.Identifier.ValueText, context.Model.GetTypeInfo(declaration.Type).Type))
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

        foreach (var parameter in parameterList.Parameters
            .Where(x => IsBinaryPayload(x.Identifier.ValueText, x.Type is null ? null : context.Model.GetTypeInfo(x.Type).Type)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
        }
    }

    // Stream is deliberately absent: GP0025 reports it, on the stronger ground that a stream does not serialize at all.
    private static bool IsBinaryPayload(string memberName, ITypeSymbol type) =>
        BinaryMemberNames.Contains(memberName) || IsByteCollection(type);

    private static bool IsByteCollection(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } => true,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named =>
                named.TypeArguments[0].SpecialType == SpecialType.System_Byte && GpCollectionEndpointHelper.IsCollectionLike(named),
            _ => false,
        };
}
