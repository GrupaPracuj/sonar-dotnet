namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntitiesShouldNotBeUsedAsMessages : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0043";

    private const string MessageFormat = "'{0}' is a database entity - publish a dedicated contract type instead.";

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

    protected override void Initialize(SonarParametrizedAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeConsumerDeclaration, SyntaxKind.ClassDeclaration);
    }

    private void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !PublishMethods.Contains(method.Name)
            || MessageType(context.Model, invocation, method) is not { } messageType
            || !IsEntity(messageType, context))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, messageType.Name);
    }

    // A consumer of an entity is the mirror image of publishing one: the contract is still the entity.
    private void AnalyzeConsumerDeclaration(SonarSyntaxNodeReportingContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(classDeclaration) is not { } type)
        {
            return;
        }

        foreach (var consumed in type.AllInterfaces
            .Where(x => x is { Name: "IConsumer", IsGenericType: true, TypeArguments.Length: 1 })
            .Select(x => x.TypeArguments[0])
            .Where(x => IsEntity(x, context)))
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

    private bool IsEntity(ITypeSymbol type, SonarSyntaxNodeReportingContext context) =>
        GpEntityTypes.IsEntity(
            type,
            context.Compilation,
            GpEntityTypes.SplitParameter(EntityBaseTypes),
            GpEntityTypes.SplitParameter(DomainNamespaces));
}
