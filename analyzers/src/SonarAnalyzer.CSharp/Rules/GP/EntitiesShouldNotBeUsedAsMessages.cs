namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntitiesShouldNotBeUsedAsMessages : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0043";

    // Wording covers both directions the rule reports: publishing an entity and consuming one.
    private const string MessageFormat = "'{0}' is a database entity - use a dedicated contract type as the message instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Publishing through Juno (GP0034 requires it) as well as the MassTransit and raw shapes, so the rule keeps
    // working whichever of them a given service still uses.
    private static readonly HashSet<string> PublishMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("entityBaseTypes", PropertyType.String, "Comma-separated base types whose descendants are entities, e.g. Entity,AggregateRoot", "")]
    public string EntityBaseTypes { get; set; } = string.Empty;

    [RuleParameter("domainNamespaces", PropertyType.String, "Comma-separated namespaces holding domain types, e.g. MyCompany.Domain", "")]
    public string DomainNamespaces { get; set; } = string.Empty;

    // The entity set is built once per compilation - the DbSet scan walks every type, so repeating it per call site
    // would make the rule scale with the number of publish calls times the size of the assembly.
    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var entities = GpEntityTypes.Create(start.Compilation, EntityBaseTypes, DomainNamespaces);
            start.RegisterNodeAction(c => AnalyzeInvocation(c, entities), SyntaxKind.InvocationExpression);
            start.RegisterNodeAction(c => AnalyzeConsumerDeclaration(c, entities), SyntaxKind.ClassDeclaration);
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !PublishMethods.Contains(method.Name)
            || MessageType(context.Model, invocation, method) is not { } messageType
            || !entities.IsEntity(messageType))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, messageType.Name);
    }

    // A consumer of an entity is the mirror image of publishing one: the contract is still the entity.
    private static void AnalyzeConsumerDeclaration(SonarSyntaxNodeReportingContext context, GpEntityTypes entities)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(classDeclaration) is not { } type)
        {
            return;
        }

        foreach (var consumed in type.AllInterfaces
            .Where(x => x is { Name: "IConsumer", IsGenericType: true, TypeArguments.Length: 1 })
            .Select(x => x.TypeArguments[0])
            .Where(entities.IsEntity))
        {
            context.ReportIssue(Rule, classDeclaration.Identifier, consumed.Name);
        }
    }

    private static ITypeSymbol MessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.TypeArguments.FirstOrDefault() is { } typeArgument)
        {
            return typeArgument;
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } firstArgument
            ? model.GetTypeInfo(firstArgument).Type
            : null;
    }
}
