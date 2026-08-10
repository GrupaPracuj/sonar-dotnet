namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuthResponseShouldMatchAuthCheck : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0011";

    private const string MessageFormat = "This looks like {0} check; return {1} instead of {2}.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);

    private static void AnalyzeIfStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not IfStatementSyntax ifStatement)
        {
            return;
        }

        if (!TryGetCheck(ifStatement.Condition, context.Model, out var checkKind, out var checkPassesWhenTrue))
        {
            return;
        }

        var failedBranch = checkPassesWhenTrue ? ifStatement.Else?.Statement : ifStatement.Statement;
        foreach (var invocation in DirectReturnInvocations(failedBranch))
        {
            if (checkKind == AuthCheckKind.Permission && IsUnauthorizedResponse(context.Model, invocation))
            {
                context.ReportIssue(Rule, invocation, "a permission", "403 (Forbid)", "401 (Unauthorized)");
            }
            else if (checkKind == AuthCheckKind.Authentication && IsForbiddenResponse(context.Model, invocation))
            {
                context.ReportIssue(Rule, invocation, "an authentication", "401 (Unauthorized)", "403 (Forbid)");
            }
        }
    }

    private static bool TryGetCheck(ExpressionSyntax condition, SemanticModel model, out AuthCheckKind kind, out bool passesWhenTrue)
    {
        condition = RemoveParentheses(condition);
        passesWhenTrue = true;
        if (condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression, Operand: var operand })
        {
            condition = RemoveParentheses(operand);
            passesWhenTrue = false;
        }

        if (condition is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "IsInRole" or "HasClaim" }
            } invocation
            && GpPrincipalApi.IsAccessCheck(model, invocation))
        {
            kind = AuthCheckKind.Permission;
            return true;
        }

        if (condition is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "IsAuthenticated" } memberAccess
            && model.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol { ContainingType: { } identityType }
            && GpPrincipalApi.IsIdentityType(identityType))
        {
            kind = AuthCheckKind.Authentication;
            return true;
        }

        kind = default;
        return false;
    }

    private static ExpressionSyntax RemoveParentheses(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax parenthesized ? RemoveParentheses(parenthesized.Expression) : expression;

    private static IEnumerable<InvocationExpressionSyntax> DirectReturnInvocations(StatementSyntax statement) =>
        statement switch
        {
            ReturnStatementSyntax { Expression: InvocationExpressionSyntax invocation } => new[] { invocation },
            BlockSyntax block => block.Statements
                .OfType<ReturnStatementSyntax>()
                .Select(x => x.Expression)
                .OfType<InvocationExpressionSyntax>(),
            _ => Enumerable.Empty<InvocationExpressionSyntax>()
        };

    private static bool IsUnauthorizedResponse(SemanticModel model, InvocationExpressionSyntax invocation) =>
        IsResponseMethod(model, invocation, "Unauthorized") || IsStatusCodeCall(model, invocation, 401);

    private static bool IsForbiddenResponse(SemanticModel model, InvocationExpressionSyntax invocation) =>
        IsResponseMethod(model, invocation, "Forbid") || IsStatusCodeCall(model, invocation, 403);

    private static bool IsResponseMethod(SemanticModel model, InvocationExpressionSyntax invocation, string methodName) =>
        TryGetResponseMethod(model, invocation, out var method) && method.Name == methodName;

    private static bool IsStatusCodeCall(SemanticModel model, InvocationExpressionSyntax invocation, int expectedStatusCode) =>
        TryGetResponseMethod(model, invocation, out var method)
        && method.Name == "StatusCode"
        && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } codeExpression
        && model.GetConstantValue(codeExpression) is { HasValue: true, Value: int statusCode }
        && statusCode == expectedStatusCode;

    private static bool TryGetResponseMethod(SemanticModel model, InvocationExpressionSyntax invocation, out IMethodSymbol method)
    {
        if (GpMinimalApi.TryGetResultMethod(model, invocation, out method))
        {
            return true;
        }

        return GpMvcResults.TryGetResultMethod(model, invocation, out method);
    }

    private enum AuthCheckKind
    {
        Authentication,
        Permission
    }
}
