namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotStartDatabaseTransactionManually : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0036";

    private const string MessageFormat = "Start the transaction with Juno's ITransactional instead of calling '{0}' on the connection.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> TransactionStartMethods = new(StringComparer.Ordinal)
    {
        "BeginTransaction",
        "BeginTransactionAsync",
        "BeginDbTransaction",
        "BeginDbTransactionAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !TransactionStartMethods.Contains(method.Name)
            || !GpJunoTypes.Implements(method.ContainingType, "System.Data.IDbConnection")
            // The ITransactional implementation is the type whose job is to produce the transaction Juno then tracks.
            || GpJunoTypes.IsInsideTypeImplementing(context, GpJunoTypes.TransactionalInterface))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }
}
