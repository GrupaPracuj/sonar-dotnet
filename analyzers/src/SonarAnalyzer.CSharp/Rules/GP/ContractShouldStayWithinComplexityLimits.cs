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
        var depth = Depth(type, reachable, new HashSet<string>(StringComparer.Ordinal), 0);
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

    // Depth follows contract-like members only and stops at BCL types, so a string or a DateTimeOffset is never a
    // level. The visited set keeps a self-referencing type from recursing forever.
    private int Depth(INamedTypeSymbol type, HashSet<string> reachable, HashSet<string> visited, int current)
    {
        if (current > MaxDepth + 1 || !visited.Add(type.ToDisplayString()))
        {
            return current;
        }

        var deepest = current;
        foreach (var nested in GpMessageContracts.DataMembers(type).Select(x => ContractType(x.Type)).Where(x => x is not null))
        {
            reachable.Add(nested.ToDisplayString());
            deepest = Math.Max(deepest, Depth(nested, reachable, visited, current + 1));
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
