namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotInheritDomainType : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0057";

    private const string MessageFormat = "'{0}' is a domain type - a contract that inherits it publishes the whole entity.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("entityBaseTypes", PropertyType.String, "Comma-separated base types whose descendants are entities, e.g. Entity,AggregateRoot", "")]
    public string EntityBaseTypes { get; set; } = string.Empty;

    [RuleParameter("domainNamespaces", PropertyType.String, "Comma-separated namespaces holding domain types, e.g. MyCompany.Domain", "")]
    public string DomainNamespaces { get; set; } = string.Empty;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var entities = GpEntityTypes.Create(start.Compilation, EntityBaseTypes, DomainNamespaces);
            start.RegisterNodeAction(c => AnalyzeTypeDeclaration(c, entities), SyntaxKind.ClassDeclaration, SyntaxKindEx.RecordDeclaration);
        });

    private static void AnalyzeTypeDeclaration(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName, BaseList: not null } declaration
            || !GpMessageContracts.HasContractName(typeName)
            || context.Model.GetDeclaredSymbol(declaration) is not { BaseType: { } baseType }
            // Only a base class counts. Inheriting another contract or implementing a marker interface is fine.
            || baseType.SpecialType == SpecialType.System_Object
            || !entities.IsEntity(baseType))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, baseType.Name);
    }
}
