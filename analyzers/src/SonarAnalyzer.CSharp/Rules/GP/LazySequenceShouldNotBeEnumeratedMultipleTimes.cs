namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LazySequenceShouldNotBeEnumeratedMultipleTimes : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0087";

    private const string MessageFormat = "'{0}' is an unmaterialized sequence and is enumerated more than once here - each enumeration re-runs the "
                                          + "underlying query/iterator. Materialize it once with '.ToList()' if you need to use it multiple times.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> EnumeratingLinqMethods = new(StringComparer.Ordinal)
    {
        "Aggregate", "All", "Any", "Average", "Contains", "Count", "ElementAt", "ElementAtOrDefault",
        "First", "FirstOrDefault", "Last", "LastOrDefault", "LongCount", "Max", "MaxBy", "Min", "MinBy",
        "SequenceEqual", "Single", "SingleOrDefault", "Sum", "ToArray", "ToDictionary", "ToHashSet", "ToList", "ToLookup",
    };

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

        // Sites are collected once, in source order, per symbol. The first site that can execute after an earlier
        // site is reported; opposite branches of the same if/else are treated as mutually exclusive.
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

        foreach (var pair in enumerationSites.OrderBy(x => x.Value[0].SpanStart))
        {
            if (FirstRepeatedSite(pair.Value) is { } repeatedSite)
            {
                context.ReportIssue(Rule, repeatedSite, pair.Key.Name);
            }
        }
    }

    private static SyntaxNode FirstRepeatedSite(IReadOnlyList<SyntaxNode> sites)
    {
        for (var i = 1; i < sites.Count; i++)
        {
            if (sites.Take(i).Any(x => !AreMutuallyExclusive(x, sites[i])))
            {
                return sites[i];
            }
        }
        return null;
    }

    private static bool AreMutuallyExclusive(SyntaxNode first, SyntaxNode second) =>
        AreInOppositeIfBranches(first, second) || AreInDifferentSwitchBranches(first, second);

    private static bool AreInOppositeIfBranches(SyntaxNode first, SyntaxNode second) =>
        first.Ancestors().OfType<IfStatementSyntax>().Any(ifStatement =>
            ifStatement.Else is not null
            && ((ifStatement.Statement.Span.Contains(first.Span) && ifStatement.Else.Statement.Span.Contains(second.Span))
                || (ifStatement.Statement.Span.Contains(second.Span) && ifStatement.Else.Statement.Span.Contains(first.Span))));

    // Two sites under different case branches of the same switch - two sections of a switch statement, two arms of a
    // switch expression - never both run, exactly like the two halves of an if/else. The switch's own governing
    // expression always runs, so it is not a case branch and is never exclusive with anything.
    private static bool AreInDifferentSwitchBranches(SyntaxNode first, SyntaxNode second) =>
        first.Ancestors()
            .Where(IsSwitch)
            .Any(x => CaseBranch(x, first) is { } firstBranch
                      && CaseBranch(x, second) is { } secondBranch
                      && firstBranch != secondBranch
                      && !CanTransferBetweenSwitchSections(x));

    private static bool CanTransferBetweenSwitchSections(SyntaxNode switchNode) =>
        switchNode is SwitchStatementSyntax switchStatement
        && switchStatement.DescendantNodes().OfType<GotoStatementSyntax>()
            .Any(x => x.IsKind(SyntaxKind.GotoCaseStatement) || x.IsKind(SyntaxKind.GotoDefaultStatement));

    private static bool IsSwitch(SyntaxNode node) =>
        node is SwitchStatementSyntax || SwitchExpressionSyntaxWrapper.IsInstance(node);

    private static SyntaxNode CaseBranch(SyntaxNode switchNode, SyntaxNode site) =>
        switchNode.ChildNodes().FirstOrDefault(x => IsCaseBranch(x) && x.Span.Contains(site.Span));

    private static bool IsCaseBranch(SyntaxNode node) =>
        node is SwitchSectionSyntax || SwitchExpressionArmSyntaxWrapper.IsInstance(node);

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
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType: { } containingType } method
        && containingType.ToDisplayString() is "System.Linq.Enumerable" or "System.Linq.Queryable"
        && EnumeratingLinqMethods.Contains(method.Name);
}
