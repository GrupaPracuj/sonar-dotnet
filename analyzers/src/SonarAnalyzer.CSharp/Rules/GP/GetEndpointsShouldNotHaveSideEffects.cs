namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GetEndpointsShouldNotHaveSideEffects : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0046";

    private const string MessageFormat = "A GET endpoint should not change state - '{0}' makes this endpoint unsafe to retry, prefetch or cache.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> PersistenceMethods = new(StringComparer.Ordinal)
    {
        "SaveChanges",
        "SaveChangesAsync",
        "Add",
        "AddAsync",
        "AddRange",
        "AddRangeAsync",
        "Update",
        "UpdateRange",
        "Remove",
        "RemoveRange",
        "ExecuteDelete",
        "ExecuteDeleteAsync",
        "ExecuteUpdate",
        "ExecuteUpdateAsync",
    };

    private static readonly HashSet<string> MessagingMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsStateChanging(method)
            || context.Model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol enclosing
            || !IsHttpGetAction(enclosing))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }

    private static bool IsStateChanging(IMethodSymbol method) =>
        (PersistenceMethods.Contains(method.Name) && IsEntityFrameworkTarget(method.ContainingType))
        || (MessagingMethods.Contains(method.Name) && IsMessagingTarget(method));

    // Add/Update/Remove are common names, so they only count on a DbContext or a DbSet - not on any list.
    private static bool IsEntityFrameworkTarget(ITypeSymbol type) =>
        type is not null
        && (GpJunoTypes.DerivesFrom(type, "Microsoft.EntityFrameworkCore.DbContext")
            || GpJunoTypes.DerivesFrom(type, "System.Data.Entity.DbContext")
            || (type as INamedTypeSymbol)?.ConstructedFrom.Is(KnownType.Microsoft_EntityFrameworkCore_DbSet_TEntity) == true
            || type.Name is "DbSet" or "EntityFrameworkQueryableExtensions" or "RelationalDatabaseFacadeExtensions");

    private static bool IsMessagingTarget(IMethodSymbol method)
    {
        var containing = method.ContainingType?.ToDisplayString() ?? string.Empty;
        return containing.StartsWith("MassTransit", StringComparison.Ordinal)
               || containing.StartsWith("GP.Juno", StringComparison.Ordinal)
               || (method.ContainingType?.AllInterfaces.Any(x => x.ToDisplayString().StartsWith("MassTransit", StringComparison.Ordinal)
                                                                 || x.ToDisplayString().StartsWith("GP.Juno", StringComparison.Ordinal)) ?? false);
    }

    private static bool IsHttpGetAction(IMethodSymbol method) =>
        method.IsControllerActionMethod()
        && method.GetAttributes().Select(x => x.AttributeClass?.Name).Any(x => x is "HttpGet" or "HttpGetAttribute");
}
