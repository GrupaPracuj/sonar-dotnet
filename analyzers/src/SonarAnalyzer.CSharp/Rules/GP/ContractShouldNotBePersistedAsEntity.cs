namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotBePersistedAsEntity : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0066";

    private const string MessageFormat =
        "'{0}' is a message contract - persisting it makes the wire format and the schema the same thing.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Matched by fully-qualified name, not just the short attribute class name: a custom attribute that happens to
    // be called TableAttribute or KeyAttribute but lives in the application's own namespace carries no EF mapping
    // semantics and must not be mistaken for one of these.
    private static readonly HashSet<string> EntityMappingAttributes = new(StringComparer.Ordinal)
    {
        "System.ComponentModel.DataAnnotations.KeyAttribute",
        "System.ComponentModel.DataAnnotations.Schema.TableAttribute",
        "System.ComponentModel.DataAnnotations.Schema.ColumnAttribute",
        "System.ComponentModel.DataAnnotations.Schema.ForeignKeyAttribute",
        "Microsoft.EntityFrameworkCore.PrimaryKeyAttribute",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeDbSetProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeEntityConfiguration, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeMappedContract, SyntaxKind.ClassDeclaration, SyntaxKindEx.RecordDeclaration);
    }

    // public DbSet<OrderAcceptedContract> ...
    private static void AnalyzeDbSetProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (context.Model.GetTypeInfo(declaration.Type).Type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } dbSet
            && dbSet.ConstructedFrom.Is(KnownType.Microsoft_EntityFrameworkCore_DbSet_TEntity)
            && GpMessageContracts.HasContractName(dbSet.TypeArguments[0].Name))
        {
            context.ReportIssue(Rule, declaration.Type, dbSet.TypeArguments[0].Name);
        }
    }

    // modelBuilder.Entity<OrderAcceptedContract>()
    private static void AnalyzeEntityConfiguration(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Entity", TypeArguments.Length: 1 } method
            && (method.ContainingType?.ToDisplayString() ?? string.Empty).StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            && GpMessageContracts.HasContractName(method.TypeArguments[0].Name))
        {
            context.ReportIssue(Rule, invocation, method.TypeArguments[0].Name);
        }
    }

    // A contract carrying EF mapping attributes is being mapped even without a DbSet.
    private static void AnalyzeMappedContract(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier: var identifier } declaration
            || !GpMessageContracts.HasContractName(identifier.ValueText)
            || context.Model.GetDeclaredSymbol(declaration) is not { } type
            || !HasEntityMappingAttribute(type))
        {
            return;
        }

        context.ReportIssue(Rule, identifier, identifier.ValueText);
    }

    private static bool HasEntityMappingAttribute(INamedTypeSymbol type) =>
        type.GetAttributes().Concat(type.GetMembers().OfType<IPropertySymbol>().SelectMany(x => x.GetAttributes()))
            .Any(x => x.AttributeClass?.ToDisplayString() is { } name && EntityMappingAttributes.Contains(name));
}
