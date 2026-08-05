namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationShouldNotBeSuppressed : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0054";

    private const string MessageFormat = "Do not turn cancellation into success - let '{0}' propagate or rethrow it.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> CancellationExceptions = new(StringComparer.Ordinal)
    {
        "System.OperationCanceledException",
        "System.Threading.Tasks.TaskCanceledException",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);

    private static void AnalyzeCatchClause(SonarSyntaxNodeReportingContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Declaration?.Type is not { } typeSyntax
            || context.Model.GetTypeInfo(typeSyntax).Type is not { } caught
            || !CancellationExceptions.Contains(caught.ToDisplayString())
            || SignalsCancellationToCaller(catchClause.Block))
        {
            return;
        }

        context.ReportIssue(Rule, typeSyntax, caught.Name);
    }

    // Rethrowing, throwing something else, returning a value, or breaking out of the enclosing loop all mean the code
    // stops rather than carrying on as if the work had finished. Logging alone does not - it records the fact without
    // changing what happens next.
    private static bool SignalsCancellationToCaller(BlockSyntax block) =>
        block is null
        || block.DescendantNodes(DoesNotBelongToANestedFunction).Any(IsCallerVisibleExit);

    // A throw or return inside a lambda or local function exits that function, not the catch block, so it says
    // nothing about what the caller will see.
    private static bool DoesNotBelongToANestedFunction(SyntaxNode node) =>
        node.Kind() != SyntaxKindEx.LocalFunctionStatement && node is not AnonymousFunctionExpressionSyntax;

    // "break" covers the idiomatic worker loop: catch cancellation, leave the loop, shut down cleanly.
    private static bool IsCallerVisibleExit(SyntaxNode node) =>
        node is ThrowStatementSyntax or ReturnStatementSyntax or BreakStatementSyntax
        || node.Kind() == SyntaxKindEx.ThrowExpression;
}
