namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractEnumShouldHaveUnknownValue : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0068";

    private const string MessageFormat =
        "'{0}' is exposed by a contract but has no zero value named {1} - a consumer cannot represent a value it does not recognise.";

    private const string DefaultUnknownNames = "Unknown,Unspecified,None";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("unknownValueNames", PropertyType.String, "Comma-separated member names accepted as the unknown value", DefaultUnknownNames)]
    public string UnknownValueNames { get; set; } = DefaultUnknownNames;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contractEnums = GpContractEnums.Create(start.Compilation);
            if (!contractEnums.IsEmpty)
            {
                start.RegisterNodeAction(c => AnalyzeEnum(c, contractEnums), SyntaxKind.EnumDeclaration);
            }
        });

    private void AnalyzeEnum(SonarSyntaxNodeReportingContext context, GpContractEnums contractEnums)
    {
        var accepted = GpEntityTypes.SplitParameter(UnknownValueNames);
        if (accepted.Length == 0
            || context.Node is not EnumDeclarationSyntax declaration
            || context.Model.GetDeclaredSymbol(declaration) is not { } enumType
            || !contractEnums.IsUsedByAContract(enumType)
            || HasUnknownAtZero(enumType, accepted))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, enumType.Name, accepted[0]);
    }

    // The member must be named for "unknown" *and* sit at zero: zero is what a consumer deserializing an absent or
    // unrecognised value ends up with, so a correctly named member anywhere else does not help.
    private static bool HasUnknownAtZero(INamedTypeSymbol enumType, string[] acceptedNames) =>
        enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(x => x is { HasConstantValue: true, ConstantValue: { } value }
                      && Array.Exists(acceptedNames, y => string.Equals(x.Name, y, StringComparison.Ordinal))
                      && IsZero(value));

    private static bool IsZero(object value) =>
        value is int and 0
        || value is long and 0L
        || value is byte and 0
        || value is sbyte and 0
        || value is short and 0
        || value is ushort and 0
        || value is uint and 0U
        || value is ulong and 0UL;
}
