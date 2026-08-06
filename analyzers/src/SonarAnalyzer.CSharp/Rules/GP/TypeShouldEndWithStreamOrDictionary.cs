namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeShouldEndWithStreamOrDictionary : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0077";

    private const string MessageFormat = "Type '{0}' implements {1} and should have a name ending in '{2}'.";

    internal const string StreamSuffix = "Stream";
    internal const string DictionarySuffix = "Dictionary";

    private const string StreamDescription = "System.IO.Stream";
    private const string DictionaryDescription = "IDictionary<TKey, TValue>";
    private const string NonGenericDictionaryFullName = "System.Collections.IDictionary";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKindEx.RecordDeclaration, SyntaxKindEx.RecordStructDeclaration);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax declaration
            || context.Model.GetDeclaredSymbol(declaration) is not { } symbol)
        {
            return;
        }

        switch (MissingSuffix(symbol))
        {
            case StreamSuffix:
                context.ReportIssue(Rule, declaration.Identifier, symbol.Name, StreamDescription, StreamSuffix);
                break;
            case DictionarySuffix:
                context.ReportIssue(Rule, declaration.Identifier, symbol.Name, DictionaryDescription, DictionarySuffix);
                break;
        }
    }

    // Returns the suffix the type is missing (either "Stream" or "Dictionary"), or null when the type does not
    // need one. Shared between the analyzer and the code fix so both agree on what "missing" means.
    internal static string MissingSuffix(INamedTypeSymbol symbol)
    {
        if (symbol is null)
        {
            return null;
        }

        // Defensive: Stream/IDictionary themselves are framework code and are never analyzed as source, but guard
        // against flagging them anyway if this is ever invoked on them directly.
        if (symbol.DerivesFrom(KnownType.System_IO_Stream) && !symbol.Is(KnownType.System_IO_Stream) && !symbol.Name.EndsWith(StreamSuffix, StringComparison.Ordinal))
        {
            return StreamSuffix;
        }

        if (ImplementsDictionary(symbol) && !symbol.Name.EndsWith(DictionarySuffix, StringComparison.Ordinal))
        {
            return DictionarySuffix;
        }

        return null;
    }

    private static bool ImplementsDictionary(INamedTypeSymbol symbol) =>
        symbol.AllInterfaces.Any(x => x.ConstructedFrom.Is(KnownType.System_Collections_Generic_IDictionary_TKey_TValue) || x.ToDisplayString() == NonGenericDictionaryFullName);
}
