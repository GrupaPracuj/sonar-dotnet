namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractEnumShouldAssignExplicitValues : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0069";

    private const string MessageFormat =
        "'{0}' is exposed by a contract with implicit values - reordering or inserting a member would silently change what is already on the wire.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contractEnums = GpContractEnums.Create(start.Compilation);
            if (!contractEnums.IsEmpty)
            {
                start.RegisterNodeAction(c => AnalyzeEnum(c, contractEnums), SyntaxKind.EnumDeclaration);
            }
        });

    private static void AnalyzeEnum(SonarSyntaxNodeReportingContext context, GpContractEnums contractEnums)
    {
        if (context.Node is not EnumDeclarationSyntax { Members.Count: > 0 } declaration
            || context.Model.GetDeclaredSymbol(declaration) is not { } enumType
            || !contractEnums.IsUsedByAContract(enumType)
            || UsesStringEnumConverter(enumType)
            // Every member has to be explicit: one implicit member is enough for a later edit to shift it.
            || declaration.Members.All(x => x.EqualsValue is not null))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, enumType.Name);
    }

    private static bool UsesStringEnumConverter(INamedTypeSymbol enumType) =>
        enumType.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonConverterAttribute"
            && attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol converter
            && converter.OriginalDefinition.ToDisplayString() is
                "System.Text.Json.Serialization.JsonStringEnumConverter"
                or "System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>");
}
