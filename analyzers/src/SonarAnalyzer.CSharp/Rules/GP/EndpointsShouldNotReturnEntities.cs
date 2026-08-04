namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointsShouldNotReturnEntities : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0045";

    private const string EntityMessageFormat = "'{0}' is a database entity - return a response contract instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, EntityMessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("entityBaseTypes", PropertyType.String, "Comma-separated base types whose descendants are entities, e.g. Entity,AggregateRoot", "")]
    public string EntityBaseTypes { get; set; } = string.Empty;

    [RuleParameter("domainNamespaces", PropertyType.String, "Comma-separated namespaces holding domain types, e.g. MyCompany.Domain", "")]
    public string DomainNamespaces { get; set; } = string.Empty;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var entities = GpEntityTypes.Create(start.Compilation, EntityBaseTypes, DomainNamespaces);
            start.RegisterNodeAction(c => AnalyzeMethod(c, entities), SyntaxKind.MethodDeclaration);
        });

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(methodDeclaration) is not { } method
            || !method.IsControllerActionMethod()
            || Unwrap(method.ReturnType) is not { } returned)
        {
            return;
        }

        if (IsQueryable(returned))
        {
            context.ReportIssue(Rule, methodDeclaration.ReturnType, returned.Name);
        }
        else if (ElementType(returned) is { } element && entities.IsEntity(element))
        {
            context.ReportIssue(Rule, methodDeclaration.ReturnType, element.Name);
        }
    }

    // Task<T>, ValueTask<T> and ActionResult<T> only wrap what the endpoint really returns.
    private static ITypeSymbol Unwrap(ITypeSymbol type)
    {
        var current = type;
        while (current is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named && IsWrapper(named))
        {
            current = named.TypeArguments[0];
        }

        return current;
    }

    private static bool IsWrapper(INamedTypeSymbol type) =>
        type.ConstructedFrom.IsAny(KnownType.System_Threading_Tasks_Task_T, KnownType.System_Threading_Tasks_ValueTask_TResult)
        || (type.Name == "ActionResult" && type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc");

    // A collection of entities is just as much a leak as a single one.
    private static ITypeSymbol ElementType(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named when GpCollectionEndpointHelper.IsCollectionLike(named) => named.TypeArguments[0],
            _ => type,
        };

    private static bool IsQueryable(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.ToDisplayString() is "System.Linq.IQueryable<T>" or "System.Linq.IQueryable";
}
