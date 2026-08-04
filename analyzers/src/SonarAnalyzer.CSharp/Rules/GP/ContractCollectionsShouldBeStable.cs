namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractCollectionsShouldBeStable : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0058";

    private const string MessageFormat = "'{0}' is a lazy sequence - the serializer would enumerate it; use IReadOnlyList<T>.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Types whose items are not guaranteed to exist yet when the message is handed to the serializer.
    private static readonly HashSet<string> LazySequenceTypes = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.IEnumerable",
        "System.Linq.IQueryable<T>",
        "System.Linq.IQueryable",
        "System.Linq.IOrderedQueryable<T>",
        "System.Collections.Generic.IAsyncEnumerable<T>",
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
            && IsLazySequence(context.Model.GetTypeInfo(declaration.Type).Type))
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
            .Where(x => x.Type is not null && IsLazySequence(context.Model.GetTypeInfo(x.Type).Type)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
        }
    }

    // A string is an IEnumerable<char> but is obviously not a lazy sequence, so the check is on the declared type
    // rather than on what it happens to implement.
    private static bool IsLazySequence(ITypeSymbol type) =>
        type is INamedTypeSymbol named && LazySequenceTypes.Contains(named.OriginalDefinition.ToDisplayString());
}
