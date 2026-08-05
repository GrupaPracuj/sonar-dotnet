namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractMembersShouldHaveConcreteTypes : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0072";

    private const string InterfaceMessage = "'{0}' is declared as the interface '{1}', so a consumer cannot tell what to deserialize it into.";
    private const string AbstractMessage = "'{0}' is declared as the abstract type '{1}', so a consumer cannot tell what to deserialize it into.";

    private const string DefaultAllowedInterfaces = "IReadOnlyList,IReadOnlyCollection,IReadOnlyDictionary,IReadOnlySet";

    private static readonly DiagnosticDescriptor InterfaceRule = DescriptorFactory.Create(RuleId, InterfaceMessage);
    private static readonly DiagnosticDescriptor AbstractRule = DescriptorFactory.Create(RuleId, AbstractMessage);

    // GP0058 reports these on the stronger ground that their items may not exist yet, so they are not reported twice.
    private static readonly HashSet<string> OwnedByLazySequenceRule = new(StringComparer.Ordinal)
    {
        "IEnumerable",
        "IQueryable",
        "IOrderedQueryable",
        "IAsyncEnumerable",
    };

    // Having written the polymorphic configuration down is the point - any of these means the decision was made.
    private static readonly HashSet<string> PolymorphismAttributes = new(StringComparer.Ordinal)
    {
        "JsonDerivedTypeAttribute",
        "JsonPolymorphicAttribute",
        "JsonConverterAttribute",
        "KnownTypeAttribute",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(InterfaceRule);

    [RuleParameter("allowedInterfaces", PropertyType.String, "Comma-separated interfaces allowed as contract member types", DefaultAllowedInterfaces)]
    public string AllowedInterfaces { get; set; } = DefaultAllowedInterfaces;

    protected override void Initialize(SonarParametrizedAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeRecordParameters, SyntaxKindEx.RecordDeclaration);
    }

    private void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (GpMessageContracts.IsContractMember(declaration))
        {
            Report(context, declaration.Identifier, context.Model.GetTypeInfo(declaration.Type).Type);
        }
    }

    private void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName } declaration
            || !GpMessageContracts.HasContractName(typeName)
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters.Where(x => x.Type is not null))
        {
            Report(context, parameter.Identifier, context.Model.GetTypeInfo(parameter.Type).Type);
        }
    }

    private void Report(SonarSyntaxNodeReportingContext context, SyntaxToken identifier, ITypeSymbol memberType)
    {
        if (memberType is not INamedTypeSymbol named || HasPolymorphicConfiguration(named))
        {
            return;
        }

        if (named.TypeKind == TypeKind.Interface)
        {
            if (!IsAllowedInterface(named))
            {
                context.ReportIssue(InterfaceRule, identifier, identifier.ValueText, named.Name);
            }
        }
        else if (named is { IsAbstract: true, TypeKind: TypeKind.Class })
        {
            context.ReportIssue(AbstractRule, identifier, identifier.ValueText, named.Name);
        }
    }

    private bool IsAllowedInterface(INamedTypeSymbol type)
    {
        if (OwnedByLazySequenceRule.Contains(type.Name))
        {
            return true;
        }

        var allowed = GpEntityTypes.SplitParameter(AllowedInterfaces);
        return Array.Exists(allowed, x => type.Name == x || type.ToDisplayString() == x);
    }

    private static bool HasPolymorphicConfiguration(INamedTypeSymbol type) =>
        type.GetAttributes().Any(x => x.AttributeClass?.Name is { } name && PolymorphismAttributes.Contains(name));
}
