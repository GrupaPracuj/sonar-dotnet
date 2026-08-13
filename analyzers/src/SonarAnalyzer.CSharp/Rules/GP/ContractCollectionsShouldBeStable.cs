namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractCollectionsShouldBeStable : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0058";

    private const string MessageFormat = "'{0}' exposes a deferred query or asynchronous sequence; materialize it as IReadOnlyList<T> before serialization.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> DeferredSequenceTypes = new(StringComparer.Ordinal)
    {
        "System.Linq.IQueryable<T>",
        "System.Linq.IQueryable",
        "System.Linq.IOrderedQueryable<T>",
        "System.Collections.Generic.IAsyncEnumerable<T>",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contracts = GpSemanticContractDetector.GetOrCreate(start.Compilation);
            start.RegisterNodeAction(c => AnalyzeProperty(c, contracts), SyntaxKind.PropertyDeclaration);
            start.RegisterNodeAction(c => AnalyzeRecordParameters(c, contracts), SyntaxKindEx.RecordDeclaration);
        });

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context, GpSemanticContractDetector contracts)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (IsDeferredSequence(context.Model.GetTypeInfo(declaration.Type).Type)
            && context.Model.GetDeclaredSymbol(declaration) is { ContainingType: { } containingType }
            && contracts.IsContract(containingType))
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText);
        }
    }

    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context, GpSemanticContractDetector contracts)
    {
        if (context.Node is not TypeDeclarationSyntax declaration
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList
            || context.Model.GetDeclaredSymbol(declaration) is not { } containingType
            || !contracts.IsContract(containingType))
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters.Where(x => x.Type is not null && IsDeferredSequence(context.Model.GetTypeInfo(x.Type).Type)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText);
        }
    }

    private static bool IsDeferredSequence(ITypeSymbol type) =>
        type is INamedTypeSymbol named && DeferredSequenceTypes.Contains(named.OriginalDefinition.ToDisplayString());
}
