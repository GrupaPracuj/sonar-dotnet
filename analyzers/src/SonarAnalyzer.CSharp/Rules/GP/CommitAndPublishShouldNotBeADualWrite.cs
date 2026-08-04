namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommitAndPublishShouldNotBeADualWrite : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0048";

    private const string MessageFormat = "This publish follows a database commit with no outbox - if it fails, the data has changed and nobody was told.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> CommitMethods = new(StringComparer.Ordinal)
    {
        "SaveChanges",
        "SaveChangesAsync",
        "Commit",
        "CommitAsync",
    };

    private static readonly HashSet<string> PublishMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("outboxTypes", PropertyType.String, "Comma-separated types marking an approved outbox; a method inside such a type is not reported", "")]
    public string OutboxTypes { get; set; } = string.Empty;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        if (methodDeclaration.Body is not { } body || IsInsideApprovedOutbox(context, methodDeclaration))
        {
            return;
        }

        // Only a publish that follows the commit is a dual write. A publish before it is GP0008's case - a network
        // call inside an open transaction - so the two rules never report the same statement.
        var invocations = body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(x => (Invocation: x, Symbol: context.Model.GetSymbolInfo(x).Symbol as IMethodSymbol))
            .Where(x => x.Symbol is not null)
            .ToList();

        if (invocations.FirstOrDefault(x => IsCommit(x.Symbol)) is not { Invocation: { } commit })
        {
            return;
        }

        foreach (var publish in invocations
            .Where(x => x.Invocation.SpanStart > commit.SpanStart && IsPublish(x.Symbol))
            .Select(x => x.Invocation))
        {
            context.ReportIssue(Rule, publish);
        }
    }

    private static bool IsCommit(IMethodSymbol method) =>
        CommitMethods.Contains(method.Name)
        && (GpJunoTypes.DerivesFrom(method.ContainingType, "Microsoft.EntityFrameworkCore.DbContext")
            || GpJunoTypes.DerivesFrom(method.ContainingType, "System.Data.Entity.DbContext")
            || GpJunoTypes.Implements(method.ContainingType, "GP.Juno.Abstractions.Ado.ITransaction")
            || GpJunoTypes.Implements(method.ContainingType, "System.Data.IDbTransaction"));

    private static bool IsPublish(IMethodSymbol method)
    {
        if (!PublishMethods.Contains(method.Name))
        {
            return false;
        }

        var containing = method.ContainingType?.ToDisplayString() ?? string.Empty;
        return containing.StartsWith("GP.Juno", StringComparison.Ordinal)
               || containing.StartsWith("MassTransit", StringComparison.Ordinal)
               || (method.ContainingType?.AllInterfaces.Any(x => x.ToDisplayString() is { } name
                   && (name.StartsWith("GP.Juno", StringComparison.Ordinal) || name.StartsWith("MassTransit", StringComparison.Ordinal))) ?? false);
    }

    private bool IsInsideApprovedOutbox(SonarSyntaxNodeReportingContext context, SyntaxNode node)
    {
        var outboxTypes = GpEntityTypes.SplitParameter(OutboxTypes);
        if (outboxTypes.Length == 0)
        {
            return false;
        }

        return node.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .Select(x => context.Model.GetDeclaredSymbol(x))
            .Any(x => x is not null && IsApprovedOutbox(x, outboxTypes));
    }

    private static bool IsApprovedOutbox(INamedTypeSymbol type, string[] outboxTypes) =>
        Array.Exists(outboxTypes, x => type.Name == x || type.ToDisplayString() == x)
        || type.AllInterfaces.Any(i => Array.Exists(outboxTypes, x => i.Name == x || i.ToDisplayString() == x))
        || BaseTypes(type).Any(b => Array.Exists(outboxTypes, x => b.Name == x || b.ToDisplayString() == x));

    private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
