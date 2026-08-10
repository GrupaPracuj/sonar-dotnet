namespace SonarAnalyzer.CSharp.Rules;

// Attribute properties are exempt (an attribute constructor argument can only be an array, never a collection, so
// there is no alternative shape to move to) and so are message contracts (GpMessageContracts) - this org's DTOs
// legitimately expose arrays for wire serialization with no in-process aliasing risk, and flagging every one of
// them would flood the contract assemblies for no benefit. 'override' is excluded too: the property's shape is
// dictated by the base member, so the finding would be unfixable at that site.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PropertyShouldNotReturnArray : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0099";

    private const string MessageFormat = "'{0}' returns an array - callers can mutate it through this property. Return a read-only collection, or a method that returns a copy.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.PropertyDeclaration);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (declaration.Modifiers.Any(SyntaxKind.OverrideKeyword)
            || context.Model.GetDeclaredSymbol(declaration) is not { } property
            || property.GetMethod?.EffectiveAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
            || property.Type is not IArrayTypeSymbol
            || IsExcludedContainingType(property.ContainingType))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, property.Name);
    }

    private static bool IsExcludedContainingType(INamedTypeSymbol containingType) =>
        containingType.DerivesFrom(KnownType.System_Attribute) || GpMessageContracts.HasContractName(containingType.Name);
}
