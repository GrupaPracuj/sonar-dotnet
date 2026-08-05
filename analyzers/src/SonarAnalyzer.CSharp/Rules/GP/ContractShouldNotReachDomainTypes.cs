namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotReachDomainTypes : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0067";

    private const string MessageFormat = "'{0}' lets this contract reach the domain type '{1}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("entityBaseTypes", PropertyType.String, "Comma-separated base types whose descendants are entities, e.g. Entity,AggregateRoot", "")]
    public string EntityBaseTypes { get; set; } = string.Empty;

    [RuleParameter("domainNamespaces", PropertyType.String, "Comma-separated namespaces holding domain types, e.g. MyCompany.Domain", "")]
    public string DomainNamespaces { get; set; } = string.Empty;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var entities = GpEntityTypes.Create(start.Compilation, EntityBaseTypes, DomainNamespaces);
            start.RegisterNodeAction(c => AnalyzeProperty(c, entities), SyntaxKind.PropertyDeclaration);
            start.RegisterNodeAction(c => AnalyzeRecordParameters(c, entities), SyntaxKindEx.RecordDeclaration);
        });

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (GpMessageContracts.IsContractMember(declaration)
            && ReachableDomainType(context.Model.GetTypeInfo(declaration.Type).Type, entities) is { } domainType)
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText, domainType.Name);
        }
    }

    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName } declaration
            || !GpMessageContracts.HasContractName(typeName)
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters)
        {
            if (parameter.Type is { } parameterType
                && ReachableDomainType(context.Model.GetTypeInfo(parameterType).Type, entities) is { } domainType)
            {
                context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText, domainType.Name);
            }
        }
    }

    // Walks the member graph and returns the first domain type it finds, at any depth. GP0043 and GP0057 cover the
    // published type and the base class; this covers everything the contract can reach through its members.
    private static INamedTypeSymbol ReachableDomainType(ITypeSymbol memberType, GpEntityTypes entities) =>
        ContractType(memberType) is { } start
            ? FindDomainType(start, entities, new HashSet<string>(StringComparer.Ordinal))
            : null;

    private static INamedTypeSymbol FindDomainType(INamedTypeSymbol type, GpEntityTypes entities, HashSet<string> visited)
    {
        if (!visited.Add(type.ToDisplayString()))
        {
            return null;
        }

        if (entities.IsEntity(type))
        {
            return type;
        }

        foreach (var nested in GpMessageContracts.DataMembers(type).Select(x => ContractType(x.Type)).Where(x => x is not null))
        {
            if (FindDomainType(nested, entities, visited) is { } found)
            {
                return found;
            }
        }

        return null;
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
