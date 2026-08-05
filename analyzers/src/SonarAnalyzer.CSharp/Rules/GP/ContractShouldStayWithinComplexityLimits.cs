namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldStayWithinComplexityLimits : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0062";

    // One descriptor with the specifics in the argument, rather than a descriptor per kind of limit - the rule id is
    // the same either way, and building descriptors at report time would allocate on every issue.
    private const string MessageFormat = "'{0}' exceeds a message contract limit: {1}.";

    private const int DefaultMaxProperties = 30;
    private const int DefaultMaxDepth = 4;
    private const int DefaultMaxComplexTypes = 10;

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("maxProperties", PropertyType.Integer, "Maximum number of public data members on a contract", DefaultMaxProperties)]
    public int MaxProperties { get; set; } = DefaultMaxProperties;

    [RuleParameter("maxDepth", PropertyType.Integer, "Maximum nesting depth of contract types", DefaultMaxDepth)]
    public int MaxDepth { get; set; } = DefaultMaxDepth;

    [RuleParameter("maxComplexTypes", PropertyType.Integer, "Maximum number of distinct contract types reachable from a contract", DefaultMaxComplexTypes)]
    public int MaxComplexTypes { get; set; } = DefaultMaxComplexTypes;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKindEx.RecordDeclaration, SyntaxKind.StructDeclaration);

    private void AnalyzeTypeDeclaration(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier: var identifier } declaration
            || !GpMessageContracts.HasContractName(identifier.ValueText)
            || context.Model.GetDeclaredSymbol(declaration) is not { } type)
        {
            return;
        }

        var members = GpMessageContracts.DataMembers(type).ToList();
        if (members.Count > MaxProperties)
        {
            Report(context, identifier, type.Name, $"{members.Count} properties, above the limit of {MaxProperties}");
            return;
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var depth = Depth(
            type,
            reachable,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<(string Type, int RemainingDepth), int>(),
            MaxDepth + 1,
            out _);
        if (depth > MaxDepth)
        {
            Report(context, identifier, type.Name, $"contract types nested {depth} levels deep, above the limit of {MaxDepth}");
        }
        else if (reachable.Count > MaxComplexTypes)
        {
            Report(context, identifier, type.Name, $"{reachable.Count} contract types reachable, above the limit of {MaxComplexTypes}");
        }
    }

    private static void Report(SonarSyntaxNodeReportingContext context, SyntaxToken identifier, string typeName, string detail) =>
        context.ReportIssue(Rule, identifier, typeName, detail);

    // Depth follows contract-like members only and stops at BCL types. The current-path set breaks cycles, while the
    // cache prevents a shared subtype from being traversed once per path. Remaining depth is part of the cache key
    // because the walk only needs to distinguish compliant depth from MaxDepth + 1.
    private static int Depth(
        INamedTypeSymbol type,
        HashSet<string> reachable,
        HashSet<string> currentPath,
        Dictionary<(string Type, int RemainingDepth), int> cache,
        int remainingDepth,
        out bool cacheable)
    {
        var key = type.ToDisplayString();
        if (remainingDepth == 0)
        {
            cacheable = true;
            return 0;
        }

        if (!currentPath.Add(key))
        {
            cacheable = false;
            return 0;
        }

        if (cache.TryGetValue((key, remainingDepth), out var cached))
        {
            currentPath.Remove(key);
            cacheable = true;
            return cached;
        }

        var deepest = 0;
        cacheable = true;
        foreach (var nested in GpMessageContracts.DataMembers(type)
                     .Select(x => ContractType(x.Type))
                     .Where(x => x is not null)
                     .GroupBy(x => x.ToDisplayString(), StringComparer.Ordinal)
                     .Select(x => x.First()))
        {
            reachable.Add(nested.ToDisplayString());
            var nestedDepth = Depth(nested, reachable, currentPath, cache, remainingDepth - 1, out var nestedCacheable);
            deepest = Math.Max(deepest, 1 + nestedDepth);
            cacheable &= nestedCacheable;
        }

        currentPath.Remove(key);
        if (cacheable)
        {
            cache[(key, remainingDepth)] = deepest;
        }
        return deepest;
    }

    private static INamedTypeSymbol ContractType(ITypeSymbol type)
    {
        var candidate = type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic => generic.TypeArguments[0],
            _ => type,
        };

        return candidate is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None } named
               && !IsFrameworkType(named)
            ? named
            : null;
    }

    private static bool IsFrameworkType(ITypeSymbol type) =>
        (type.ContainingNamespace?.ToDisplayString() ?? string.Empty) is var containing
        && (containing == "System"
            || containing.StartsWith("System.", StringComparison.Ordinal)
            || containing.StartsWith("Microsoft.", StringComparison.Ordinal));
}
