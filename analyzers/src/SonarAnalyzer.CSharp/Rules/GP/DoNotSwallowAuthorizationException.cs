namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotSwallowAuthorizationException : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0021";

    private const string MessageFormat = "Do not silently swallow an exception around an access check - at least log the failure.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> AccessCheckMethods = new(StringComparer.Ordinal) { "HasClaim", "IsInRole" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeTryStatement, SyntaxKind.TryStatement);

    private static void AnalyzeTryStatement(SonarSyntaxNodeReportingContext context)
    {
        var tryStatement = (TryStatementSyntax)context.Node;
        if (!ContainsAccessCheck(tryStatement.Block))
        {
            return;
        }

        foreach (var catchClause in tryStatement.Catches)
        {
            if (catchClause.Block is { Statements.Count: 0 } && !IsCoveredByGenericCatchRule(catchClause, context.Model))
            {
                context.ReportIssue(Rule, catchClause);
            }
        }
    }

    // S2486 already reports an empty "catch" or "catch (Exception)" without a filter, so reporting those again here
    // would only produce a second issue on the same line. What S2486 deliberately leaves alone - an empty catch of a
    // specific exception type, or one behind a filter - is what this rule adds.
    private static bool IsCoveredByGenericCatchRule(CatchClauseSyntax catchClause, SemanticModel model) =>
        catchClause.Filter is null
        && (catchClause.Declaration?.Type is not { } type || model.GetTypeInfo(type).Type.Is(KnownType.System_Exception));

    private static bool ContainsAccessCheck(SyntaxNode node) =>
        node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(x => x.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } && AccessCheckMethods.Contains(name));
}
