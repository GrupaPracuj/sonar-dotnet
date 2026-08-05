namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractEnumShouldNotBeFlags : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0070";

    private const string MessageFormat =
        "'{0}' is a flags enum exposed by a contract - a combined value carries bits a consumer may not recognise, and it cannot report that.";

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
        if (context.Node is not EnumDeclarationSyntax declaration
            || context.Model.GetDeclaredSymbol(declaration) is not { } enumType
            || !contractEnums.IsUsedByAContract(enumType)
            || !HasFlagsAttribute(enumType))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, enumType.Name);
    }

    private static bool HasFlagsAttribute(INamedTypeSymbol enumType) =>
        enumType.GetAttributes().Any(x => x.AttributeClass.Is(KnownType.System_FlagsAttribute));
}
