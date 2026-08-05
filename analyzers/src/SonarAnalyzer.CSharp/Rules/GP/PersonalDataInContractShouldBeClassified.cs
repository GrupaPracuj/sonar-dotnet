namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PersonalDataInContractShouldBeClassified : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0049";

    private const string MessageFormat = "'{0}' is personal data - classify it with an approved attribute or interface.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("classificationAttributes", PropertyType.String, "Comma-separated attributes that classify personal data, e.g. PersonalData", "")]
    public string ClassificationAttributes { get; set; } = string.Empty;

    [RuleParameter("classificationInterfaces", PropertyType.String, "Comma-separated interfaces that classify a contract as carrying personal data", "")]
    public string ClassificationInterfaces { get; set; } = string.Empty;

    protected override void Initialize(SonarParametrizedAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeRecordParameters, SyntaxKindEx.RecordDeclaration);
    }

    private void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (GpMessageContracts.IsContractMember(declaration)
            && GpIdentifierWords.ContainsPiiWord(declaration.Identifier.ValueText)
            && !IsClassified(context, declaration))
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText);
        }
    }

    private void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName } declaration
            || !GpMessageContracts.HasContractName(typeName)
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters
            .Where(x => GpIdentifierWords.ContainsPiiWord(x.Identifier.ValueText) && !IsClassified(context, x)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
        }
    }

    // The classification may sit on the member or on the contract as a whole, and either an attribute or a marker
    // interface counts - organisations express it both ways.
    private bool IsClassified(SonarSyntaxNodeReportingContext context, SyntaxNode member)
    {
        var attributes = GpEntityTypes.SplitParameter(ClassificationAttributes);
        var interfaces = GpEntityTypes.SplitParameter(ClassificationInterfaces);
        if (attributes.Length == 0 && interfaces.Length == 0)
        {
            return false;
        }

        // The classification counts whether it sits on the member itself or on the contract that declares it.
        return HasClassifyingAttribute(context, member, attributes)
            || HasClassifyingAttributeOnGeneratedProperty(context, member, attributes)
            || (EnclosingType(member) is { } typeDeclaration && HasClassifyingAttribute(context, typeDeclaration, attributes))
            || EnclosingTypeImplements(context, member, interfaces);
    }

    // On a positional record, "[property: PersonalData] string Email" puts the attribute on the generated property
    // rather than on the parameter, so the parameter's own attribute list does not show it.
    private static bool HasClassifyingAttributeOnGeneratedProperty(SonarSyntaxNodeReportingContext context, SyntaxNode member, string[] attributes) =>
        member is ParameterSyntax { Identifier.ValueText: var name }
        && EnclosingType(member) is { } typeDeclaration
        && context.Model.GetDeclaredSymbol(typeDeclaration) is { } type
        && type.GetMembers(name).OfType<IPropertySymbol>().Any(x => HasClassifyingAttribute(x, attributes));

    private static TypeDeclarationSyntax EnclosingType(SyntaxNode member) =>
        member.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

    private static bool HasClassifyingAttribute(SonarSyntaxNodeReportingContext context, SyntaxNode node, string[] attributes) =>
        context.Model.GetDeclaredSymbol(node) is { } symbol && HasClassifyingAttribute(symbol, attributes);

    private static bool HasClassifyingAttribute(ISymbol symbol, string[] attributes) =>
        attributes.Length > 0
        && symbol.GetAttributes().Any(x => x.AttributeClass is { } attributeClass
                                           && Array.Exists(attributes, y => attributeClass.Name == y || attributeClass.Name == y + "Attribute"));

    private static bool EnclosingTypeImplements(SonarSyntaxNodeReportingContext context, SyntaxNode member, string[] interfaces) =>
        interfaces.Length > 0
        && member.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is { } typeDeclaration
        && context.Model.GetDeclaredSymbol(typeDeclaration) is { } type
        && type.AllInterfaces.Any(x => Array.Exists(interfaces, y => x.Name == y || x.ToDisplayString() == y));
}
