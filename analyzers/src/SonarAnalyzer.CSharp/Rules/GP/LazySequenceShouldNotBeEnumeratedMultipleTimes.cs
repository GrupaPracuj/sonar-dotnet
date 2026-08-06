namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LazySequenceShouldNotBeEnumeratedMultipleTimes : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0087";

    private const string MessageFormat = "'{0}' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the "
                                          + "underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(c => Analyze(c, ((MethodDeclarationSyntax)c.Node).ParameterList, ((MethodDeclarationSyntax)c.Node).Body), SyntaxKind.MethodDeclaration);
        context.RegisterNodeAction(c => Analyze(c, ((ConstructorDeclarationSyntax)c.Node).ParameterList, ((ConstructorDeclarationSyntax)c.Node).Body), SyntaxKind.ConstructorDeclaration);
        context.RegisterNodeAction(c => Analyze(c, ((LocalFunctionStatementSyntaxWrapper)c.Node).ParameterList, ((LocalFunctionStatementSyntaxWrapper)c.Node).Body), SyntaxKindEx.LocalFunctionStatement);
    }

    private static void Analyze(SonarSyntaxNodeReportingContext context, ParameterListSyntax parameterList, BlockSyntax body)
    {
        if (body is null)
        {
            return;
        }

        var trackedSymbols = TrackedLazySequenceSymbols(context.Model, parameterList, body);
        if (trackedSymbols.Count == 0)
        {
            return;
        }

        // Sites are collected once, in source order, per symbol - foreach and DescendantNodes() both visit nodes depth-first
        // in document order, so the first two entries for a symbol are its first two enumerations in the method body.
        var enumerationSites = new Dictionary<ISymbol, List<SyntaxNode>>();
        foreach (var node in body.DescendantNodes())
        {
            if (EnumeratedSymbol(context.Model, node) is { } symbol && trackedSymbols.Contains(symbol))
            {
                if (!enumerationSites.TryGetValue(symbol, out var sites))
                {
                    enumerationSites[symbol] = sites = [];
                }

                sites.Add(node);
            }
        }

        foreach (var pair in enumerationSites.Where(x => x.Value.Count >= 2).OrderBy(x => x.Value[1].SpanStart))
        {
            context.ReportIssue(Rule, pair.Value[1], pair.Key.Name);
        }
    }

    // Local variables and parameters whose DECLARED type is exactly IEnumerable<T> or IQueryable<T>. A concrete collection such as
    // List<T> also implements IEnumerable<T>, but its declared type is List<T>, not the interface, so it is not tracked: once
    // materialized, repeated enumeration is safe.
    private static HashSet<ISymbol> TrackedLazySequenceSymbols(SemanticModel model, ParameterListSyntax parameterList, BlockSyntax body)
    {
        var symbols = new HashSet<ISymbol>();

        if (parameterList is not null)
        {
            foreach (var parameter in parameterList.Parameters)
            {
                if (model.GetDeclaredSymbol(parameter) is IParameterSymbol parameterSymbol && IsLazySequenceType(parameterSymbol.Type))
                {
                    symbols.Add(parameterSymbol);
                }
            }
        }

        foreach (var declarator in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (model.GetDeclaredSymbol(declarator) is ILocalSymbol localSymbol && IsLazySequenceType(localSymbol.Type))
            {
                symbols.Add(localSymbol);
            }
        }

        return symbols;
    }

    private static bool IsLazySequenceType(ITypeSymbol type) =>
        type.Is(KnownType.System_Collections_Generic_IEnumerable_T) || type.Is(KnownType.System_Linq_IQueryable);

    // A foreach over the tracked symbol, or a LINQ extension method called directly on the tracked symbol (not on the result of
    // an earlier call in the same chain - GetSymbolInfo on a MemberAccessExpression's receiver only resolves to the tracked
    // symbol when the receiver is that symbol itself, not an intermediate InvocationExpression).
    private static ISymbol EnumeratedSymbol(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            ForEachStatementSyntax forEach => model.GetSymbolInfo(forEach.Expression).Symbol,
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: var receiver } } invocation when IsLinqExtensionCall(model, invocation) =>
                model.GetSymbolInfo(receiver).Symbol,
            _ => null,
        };

    private static bool IsLinqExtensionCall(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType: { } containingType }
        && containingType.ToDisplayString() is "System.Linq.Enumerable" or "System.Linq.Queryable";
}
