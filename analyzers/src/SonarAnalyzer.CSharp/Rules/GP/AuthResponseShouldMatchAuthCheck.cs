namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuthResponseShouldMatchAuthCheck : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0011";

    private const string MessageFormat = "This looks like {0} check; return {1} instead of {2}.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> PermissionCheckMethods = new(StringComparer.Ordinal)
    {
        "IsInRole",
        "HasClaim"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);

    private static void AnalyzeIfStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not IfStatementSyntax ifStatement)
        {
            return;
        }

        var isPermissionCheck = ContainsPermissionCheck(ifStatement.Condition);
        var isAuthenticationCheck = ContainsAuthenticationCheck(ifStatement.Condition);
        if (isPermissionCheck == isAuthenticationCheck)
        {
            // Neither check recognized, or both (a mixed/ambiguous condition) - do not guess.
            return;
        }

        foreach (var invocation in GetDirectInvocations(ifStatement.Statement).Concat(GetDirectInvocations(ifStatement.Else?.Statement)))
        {
            if (isPermissionCheck && IsUnauthorizedResponse(context.Model, invocation))
            {
                context.ReportIssue(Rule, invocation, "a permission", "403 (Forbid)", "401 (Unauthorized)");
            }
            else if (isAuthenticationCheck && IsForbiddenResponse(context.Model, invocation))
            {
                context.ReportIssue(Rule, invocation, "an authentication", "401 (Unauthorized)", "403 (Forbid)");
            }
        }
    }

    private static bool ContainsPermissionCheck(ExpressionSyntax condition) =>
        condition.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(x => x.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } && PermissionCheckMethods.Contains(name));

    private static bool ContainsAuthenticationCheck(ExpressionSyntax condition) =>
        condition.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(x => x.Name.Identifier.ValueText == "IsAuthenticated");

    // Stops at a nested/chained if-statement (including an "else if"), which is registered and analyzed on its own -
    // otherwise a status-code call guarded by an inner condition would be wrongly attributed to this outer one.
    private static IEnumerable<InvocationExpressionSyntax> GetDirectInvocations(StatementSyntax statement)
    {
        if (statement is null or IfStatementSyntax)
        {
            return Enumerable.Empty<InvocationExpressionSyntax>();
        }

        return statement.DescendantNodesAndSelf(x => x is not IfStatementSyntax).OfType<InvocationExpressionSyntax>();
    }

    private static bool IsUnauthorizedResponse(SemanticModel model, InvocationExpressionSyntax invocation) =>
        IsControllerHelperCall(invocation, "Unauthorized") || IsStatusCodeCall(model, invocation, 401);

    private static bool IsForbiddenResponse(SemanticModel model, InvocationExpressionSyntax invocation) =>
        IsControllerHelperCall(invocation, "Forbid") || IsStatusCodeCall(model, invocation, 403);

    private static bool IsControllerHelperCall(InvocationExpressionSyntax invocation, string methodName) =>
        GetInvokedMethodName(invocation) == methodName;

    private static bool IsStatusCodeCall(SemanticModel model, InvocationExpressionSyntax invocation, int expectedStatusCode) =>
        GetInvokedMethodName(invocation) == "StatusCode"
        && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } codeExpression
        && model.GetConstantValue(codeExpression) is { HasValue: true, Value: int statusCode }
        && statusCode == expectedStatusCode;

    private static string GetInvokedMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => string.Empty
        };
}
