namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotRedirectToUserControlledUrl : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0031";

    private const string MessageFormat = "Do not redirect to a URL taken from parameter '{0}' - use LocalRedirect or check it with Url.IsLocalUrl first.";
    private const string UrlHelperType = "Microsoft.AspNetCore.Mvc.IUrlHelper";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // LocalRedirect/LocalRedirectPermanent are absent on purpose: they perform the check themselves.
    private static readonly HashSet<string> UncheckedRedirectMethods = new(StringComparer.Ordinal)
    {
        "Redirect",
        "RedirectPermanent",
        "RedirectPreserveMethod",
        "RedirectPermanentPreserveMethod",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { Arguments.Count: > 0 } argumentList
            || !UncheckedRedirectMethods.Contains(GpCollectionEndpointHelper.GetInvokedMethodName(invocation) ?? string.Empty)
            || argumentList.Arguments[0] is not { } urlArgument
            || GpUrlExpressionHelper.ActionParameterSteeringDestination(context.Model, urlArgument.Expression) is not { } parameterName
            || HasLocalUrlGuard(invocation, urlArgument.Expression, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, parameterName);
    }

    private static bool HasLocalUrlGuard(InvocationExpressionSyntax redirect, ExpressionSyntax redirectedUrl, SemanticModel model) =>
        HasPositiveLocalUrlGuard(redirect, redirectedUrl, model)
        || HasNegativeLocalUrlGuardWithEarlyExit(redirect, redirectedUrl, model);

    private static bool HasPositiveLocalUrlGuard(InvocationExpressionSyntax redirect, ExpressionSyntax redirectedUrl, SemanticModel model) =>
        redirect.Ancestors()
            .OfType<IfStatementSyntax>()
            .Any(x => x.Statement.Span.Contains(redirect.Span)
                      && IsPositiveLocalUrlCondition(x.Condition, redirectedUrl, model)
                      && !IsReassignedBeforeRedirect(x.Statement, redirectedUrl, redirect, model));

    private static bool HasNegativeLocalUrlGuardWithEarlyExit(InvocationExpressionSyntax redirect, ExpressionSyntax redirectedUrl, SemanticModel model)
    {
        if (redirect.Ancestors().OfType<StatementSyntax>().FirstOrDefault(x => x.Parent is BlockSyntax) is not { Parent: BlockSyntax block } redirectStatement)
        {
            return false;
        }

        var redirectIndex = block.Statements.IndexOf(redirectStatement);
        return redirectIndex > 0
               && block.Statements[redirectIndex - 1] is IfStatementSyntax { Else: null } guard
               && IsNegativeLocalUrlCondition(guard.Condition, redirectedUrl, model)
               && AlwaysExits(guard.Statement);
    }

    private static bool IsPositiveLocalUrlCondition(ExpressionSyntax condition, ExpressionSyntax redirectedUrl, SemanticModel model)
    {
        condition = RemoveParentheses(condition);
        if (condition is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression))
        {
            return IsPositiveLocalUrlCondition(binary.Left, redirectedUrl, model)
                   || IsPositiveLocalUrlCondition(binary.Right, redirectedUrl, model);
        }

        return IsLocalUrlInvocation(condition, redirectedUrl, model);
    }

    private static bool IsNegativeLocalUrlCondition(ExpressionSyntax condition, ExpressionSyntax redirectedUrl, SemanticModel model)
    {
        condition = RemoveParentheses(condition);
        return condition is PrefixUnaryExpressionSyntax unary
               && unary.IsKind(SyntaxKind.LogicalNotExpression)
               && IsLocalUrlInvocation(RemoveParentheses(unary.Operand), redirectedUrl, model);
    }

    private static bool IsLocalUrlInvocation(ExpressionSyntax condition, ExpressionSyntax redirectedUrl, SemanticModel model) =>
        condition is InvocationExpressionSyntax invocation
               && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "IsLocalUrl" } method
               && method.ContainingType?.ToDisplayString() == UrlHelperType
               && method.Parameters.Length == 1
               && new CSharpMethodParameterLookup(invocation, method).TryGetSyntax(method.Parameters[0], out var checkedUrls)
               && checkedUrls.Length == 1
               && AreSameExpression((ExpressionSyntax)checkedUrls[0], redirectedUrl, model);

    private static bool AlwaysExits(StatementSyntax statement) =>
        statement is ReturnStatementSyntax or ThrowStatementSyntax
        || statement is BlockSyntax { Statements.Count: 1 } block
            && block.Statements[0] is ReturnStatementSyntax or ThrowStatementSyntax;

    private static bool IsReassignedBeforeRedirect(StatementSyntax guardedStatement,
                                                   ExpressionSyntax redirectedUrl,
                                                   InvocationExpressionSyntax redirect,
                                                   SemanticModel model)
    {
        var symbols = redirectedUrl.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Select(x => model.GetSymbolInfo(x).Symbol)
            .Where(x => x is ILocalSymbol or IParameterSymbol)
            .ToArray();
        if (symbols.Length == 0)
        {
            return false;
        }

        return guardedStatement.DescendantNodes()
            .Where(x => x.SpanStart < redirect.SpanStart)
            .Any(x => x switch
            {
                AssignmentExpressionSyntax assignment => ReferencesAnySymbol(assignment.Left, symbols, model),
                PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression)
                                                         || prefix.IsKind(SyntaxKind.PreDecrementExpression) =>
                    ReferencesAnySymbol(prefix.Operand, symbols, model),
                PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression)
                                                           || postfix.IsKind(SyntaxKind.PostDecrementExpression) =>
                    ReferencesAnySymbol(postfix.Operand, symbols, model),
                ArgumentSyntax argument when !argument.RefOrOutKeyword.IsKind(SyntaxKind.None) =>
                    ReferencesAnySymbol(argument.Expression, symbols, model),
                _ => false,
            });
    }

    private static bool ReferencesAnySymbol(ExpressionSyntax expression, ISymbol[] symbols, SemanticModel model) =>
        expression.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Select(x => model.GetSymbolInfo(x).Symbol)
            .Any(candidate => candidate is not null && symbols.Any(x => x.Equals(candidate)));

    private static bool AreSameExpression(ExpressionSyntax first, ExpressionSyntax second, SemanticModel model)
    {
        first = RemoveParentheses(first);
        second = RemoveParentheses(second);
        return model.GetSymbolInfo(first).Symbol is IParameterSymbol firstParameter
               && model.GetSymbolInfo(second).Symbol is IParameterSymbol secondParameter
            ? firstParameter.Equals(secondParameter)
            : SyntaxFactory.AreEquivalent(first, second);
    }

    private static ExpressionSyntax RemoveParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
