namespace SonarAnalyzer.CSharp.Rules;

// S3260 already covers this for 'private' (scope: containing type) and 'file' (scope: file) types - this extends the
// same technique to 'internal' (scope: the whole assembly), which S3260 does not cover. Skips the whole compilation
// when it uses [InternalsVisibleTo], since a friend assembly may then subclass a type that has no subtype locally.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonDerivedInternalTypesShouldBeSealed : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0098";

    private const string MessageFormat = "'{0}' has no subtype in this assembly and should be sealed.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(compilationStart =>
        {
            if (HasInternalsVisibleTo(compilationStart.Compilation))
            {
                return;
            }

            var baseTypesWithSubtype = new Lazy<HashSet<INamedTypeSymbol>>(() => BaseTypesWithSubtype(compilationStart.Compilation));
            compilationStart.RegisterNodeAction(c => Analyze(c, baseTypesWithSubtype.Value), SyntaxKind.ClassDeclaration, SyntaxKindEx.RecordDeclaration);
        });

    private static void Analyze(SonarSyntaxNodeReportingContext context, HashSet<INamedTypeSymbol> baseTypesWithSubtype)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (IsExcluded(declaration)
            || context.Model.GetDeclaredSymbol(declaration) is not { EffectiveAccessibility: Accessibility.Internal } symbol
            || baseTypesWithSubtype.Contains(symbol.OriginalDefinition))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, symbol.Name);
    }

    // Partial is excluded to avoid reporting once per file for a type split across several files.
    private static bool IsExcluded(TypeDeclarationSyntax declaration) =>
        declaration.Modifiers.Any(SyntaxKind.SealedKeyword)
        || declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
        || declaration.Modifiers.Any(SyntaxKind.AbstractKeyword)
        || declaration.Modifiers.Any(SyntaxKind.PartialKeyword);

    private static HashSet<INamedTypeSymbol> BaseTypesWithSubtype(Compilation compilation)
    {
        var result = new HashSet<INamedTypeSymbol>();
        foreach (var type in compilation.Assembly.GlobalNamespace.GetAllNamedTypes())
        {
            if (type.BaseType is { } baseType)
            {
                result.Add(baseType.OriginalDefinition);
            }
        }

        return result;
    }

    private static bool HasInternalsVisibleTo(Compilation compilation) =>
        compilation.Assembly.GetAttributes().Any(x => x.AttributeClass.Is(KnownType.System_Runtime_CompilerServices_InternalsVisibleToAttribute));
}
