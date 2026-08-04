namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotLockAcrossProcessesManually : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0040";

    private const string MessageFormat = "Use Juno's ILockableFactory instead of '{0}' for locking across processes.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> ConsulLockMethods = new(StringComparer.Ordinal)
    {
        "AcquireLock",
        "CreateLock",
        "ExecuteLocked",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (!ObjectCreationFactory.TryCreate(context.Node, out var creation)
            || creation.TypeSymbol(context.Model) is not { } type
            || type.ToDisplayString() != "System.Threading.Mutex"
            || !HasName(context.Model, creation.ArgumentList))
        {
            return;
        }

        context.ReportIssue(Rule, creation.Expression, type.Name);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType: { } containingType } method
            && ConsulLockMethods.Contains(method.Name)
            && (containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty).StartsWith("Consul", StringComparison.Ordinal))
        {
            context.ReportIssue(Rule, invocation, containingType.Name);
        }
    }

    // Only a *named* Mutex reaches beyond the current process; an unnamed one is in-process synchronization, which
    // this rule deliberately leaves alone along with lock/SemaphoreSlim/Monitor.
    private static bool HasName(SemanticModel model, ArgumentListSyntax argumentList) =>
        argumentList is { Arguments.Count: > 1 }
        && argumentList.Arguments.Any(x => model.GetTypeInfo(x.Expression).Type.Is(KnownType.System_String));
}
