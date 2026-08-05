namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractTypesShouldNotFormACycle : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0064";

    private const string MessageFormat = "'{0}' lets '{1}' reach itself - the serializer throws on a circular reference.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeRecordParameters, SyntaxKindEx.RecordDeclaration);
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (declaration.Parent is TypeDeclarationSyntax { Identifier.ValueText: var typeName } owner
            && GpMessageContracts.HasContractName(typeName)
            && context.Model.GetDeclaredSymbol(owner) is { } ownerType
            && ClosesACycle(context.Model.GetTypeInfo(declaration.Type).Type, ownerType))
        {
            context.ReportIssue(Rule, declaration.Identifier, declaration.Identifier.ValueText, ownerType.Name);
        }
    }

    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier.ValueText: var typeName } declaration
            || !GpMessageContracts.HasContractName(typeName)
            || !RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            || ((RecordDeclarationSyntaxWrapper)declaration).ParameterList is not { } parameterList
            || context.Model.GetDeclaredSymbol(declaration) is not { } ownerType)
        {
            return;
        }

        foreach (var parameter in parameterList.Parameters
            .Where(x => x.Type is not null && ClosesACycle(context.Model.GetTypeInfo(x.Type).Type, ownerType)))
        {
            context.ReportIssue(Rule, parameter.Identifier, parameter.Identifier.ValueText, ownerType.Name);
        }
    }

    // True when the owning type is reachable from this member's type, which is what makes the graph cyclic. Reporting
    // the closing member rather than every type in the cycle keeps it to one actionable issue per edge.
    private static bool ClosesACycle(ITypeSymbol memberType, INamedTypeSymbol ownerType) =>
        ContractType(memberType) is { } start && Reaches(start, ownerType, new HashSet<string>(StringComparer.Ordinal));

    private static bool Reaches(INamedTypeSymbol from, INamedTypeSymbol target, HashSet<string> visited)
    {
        if (SameType(from, target))
        {
            return true;
        }

        if (!visited.Add(from.ToDisplayString()))
        {
            return false;
        }

        return GpMessageContracts.DataMembers(from)
            .Select(x => ContractType(x.Type))
            .Where(x => x is not null)
            .Any(x => Reaches(x, target, visited));
    }

    private static bool SameType(ISymbol left, ISymbol right) =>
        left.OriginalDefinition.ToDisplayString() == right.OriginalDefinition.ToDisplayString();

    // Follows single members and collection elements, and stops at framework types so a string is never part of
    // a cycle.
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
