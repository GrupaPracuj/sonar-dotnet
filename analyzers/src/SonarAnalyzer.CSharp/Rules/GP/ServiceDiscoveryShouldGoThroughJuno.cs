namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServiceDiscoveryShouldGoThroughJuno : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0047";

    private const string MessageFormat = "Resolve the service through Juno instead of querying '{0}' directly.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private const string ConsulNamespace = "Consul";

    // Locking on Consul is GP0040's job, so those members are left to it rather than reported twice.
    private static readonly HashSet<string> LockMembers = new(StringComparer.Ordinal)
    {
        "AcquireLock",
        "CreateLock",
        "ExecuteLocked",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeMemberAccess(SonarSyntaxNodeReportingContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (LockMembers.Contains(memberAccess.Name.Identifier.ValueText)
            || IsInsideJuno(context)
            || context.Model.GetTypeInfo(memberAccess.Expression).Type is not { } type
            || !IsConsulType(type))
        {
            return;
        }

        context.ReportIssue(Rule, memberAccess, type.Name);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!IsInsideJuno(context)
            && ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && IsConsulType(type))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    private static bool IsConsulType(ITypeSymbol type) =>
        (type.ContainingNamespace?.ToDisplayString() ?? string.Empty) is var containing
        && (containing == ConsulNamespace || containing.StartsWith(ConsulNamespace + ".", StringComparison.Ordinal));

    // Juno is the layer that is supposed to wrap Consul, so its own code is not reported.
    private static bool IsInsideJuno(SonarSyntaxNodeReportingContext context) =>
        (context.Model.GetEnclosingSymbol(context.Node.SpanStart)?.ContainingNamespace?.ToDisplayString() ?? string.Empty)
            .StartsWith("GP.Juno", StringComparison.Ordinal);
}
