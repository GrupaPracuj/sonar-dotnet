/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingClaimShouldNotGrantAccess : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0005";

    private const string MessageFormat = "Do not grant access when the required claim is absent.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // GP.Juno.Security(.UserContexts) exposes its own parameterless claim-existence checks, which bypass the generic
    // HasClaim(string)/HasClaim(predicate) overloads entirely, so the presence check has to know their names too.
    private static readonly HashSet<string> JunoClaimPresenceChecks = new(StringComparer.Ordinal)
    {
        "HasUserClaim",
        "HasApplicationClaim",
        "HasUserGroupClaim",
        "HasCompanyClaim",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(CheckClaimGuard, SyntaxKind.IfStatement);

    private static void CheckClaimGuard(SonarSyntaxNodeReportingContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        if (!TryGetClaimPresenceCheck(ifStatement.Condition, context.Model, out var claimCheck, out var claimPresentWhenTrue))
        {
            return;
        }

        var missingClaimBranch = claimPresentWhenTrue ? ifStatement.Else?.Statement : ifStatement.Statement;
        if (BranchDirectlyGrantsAccess(missingClaimBranch, context.Model))
        {
            context.ReportIssue(Rule, claimCheck);
        }
    }

    private static bool TryGetClaimPresenceCheck(ExpressionSyntax condition,
                                                 SemanticModel model,
                                                 out ExpressionSyntax claimCheck,
                                                 out bool claimPresentWhenTrue)
    {
        condition = RemoveParentheses(condition);
        if (condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression, Operand: var operand })
        {
            operand = RemoveParentheses(operand);
            if (operand is InvocationExpressionSyntax invocation && IsHasClaimInvocation(model, invocation))
            {
                claimCheck = condition;
                claimPresentWhenTrue = false;
                return true;
            }
        }
        else if (condition is InvocationExpressionSyntax invocation && IsHasClaimInvocation(model, invocation))
        {
            claimCheck = invocation;
            claimPresentWhenTrue = true;
            return true;
        }

        claimCheck = null;
        claimPresentWhenTrue = false;
        return false;
    }

    private static ExpressionSyntax RemoveParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }
        return expression;
    }

    private static bool BranchDirectlyGrantsAccess(StatementSyntax statement, SemanticModel model) =>
        DirectReturnExpressions(statement).Any(x => IsAccessGrant(x, model));

    private static IEnumerable<ExpressionSyntax> DirectReturnExpressions(StatementSyntax statement) =>
        statement switch
        {
            ReturnStatementSyntax { Expression: { } expression } => new[] { expression },
            BlockSyntax block => block.Statements.OfType<ReturnStatementSyntax>().Select(x => x.Expression).WhereNotNull(),
            _ => Enumerable.Empty<ExpressionSyntax>()
        };

    private static bool IsAccessGrant(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (GpMinimalApi.TryGetResultMethod(model, invocation, out var resultMethod))
        {
            return IsSuccessfulResponse(resultMethod.Name, invocation.ArgumentList.Arguments, model);
        }

        return GpMvcResults.TryGetResultMethod(model, invocation, out var mvcMethod)
               && IsSuccessfulResponse(mvcMethod.Name, invocation.ArgumentList.Arguments, model);
    }

    private static bool IsSuccessfulResponse(string methodName, SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model)
    {
        if (methodName is "Ok" or "Created" or "CreatedAtAction" or "CreatedAtRoute" or "Accepted" or "AcceptedAtAction"
            or "AcceptedAtRoute" or "NoContent" or "Content" or "Json" or "File")
        {
            return true;
        }

        return methodName == "StatusCode"
               && arguments.FirstOrDefault()?.Expression is { } statusCodeExpression
               && model.GetConstantValue(statusCodeExpression) is { HasValue: true, Value: int statusCode }
               && statusCode is >= 200 and < 300;
    }

    private static bool IsHasClaimInvocation(SemanticModel model, InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var methodName }
        && ((methodName == "HasClaim" && IsClaimsPrincipalMethod(model, invocation))
            || (JunoClaimPresenceChecks.Contains(methodName) && IsJunoSecurityMethod(model, invocation)));

    private static bool IsClaimsPrincipalMethod(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && GpJunoTypes.DerivesFrom(method.ContainingType, "System.Security.Claims.ClaimsPrincipal");

    // The name alone must never be enough: a same-named method on an unrelated type checks something else entirely,
    // so the declaring namespace decides.
    private static bool IsJunoSecurityMethod(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && (IsJunoNamespace(method.ContainingNamespace) || IsJunoNamespace(method.ContainingType?.ContainingNamespace));

    private static bool IsJunoNamespace(INamespaceSymbol namespaceSymbol) =>
        (namespaceSymbol?.ToDisplayString() ?? string.Empty).StartsWith("GP.Juno", StringComparison.Ordinal);
}
