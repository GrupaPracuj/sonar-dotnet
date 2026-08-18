/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotRedirectToUserControlledUrl : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0031";
    internal const string MinimalApiDiagnosticProperty = "MinimalApi";

    private const string MessageFormat = "Do not redirect to a URL taken from parameter '{0}' - use LocalRedirect or check it with Url.IsLocalUrl first.";
    private const string MinimalApiMessageFormat = "Do not redirect to a URL taken from parameter '{0}' - validate that it is local or against an allowlist first.";
    private const string ControllerBaseType = "Microsoft.AspNetCore.Mvc.ControllerBase";
    private const string UrlHelperType = "Microsoft.AspNetCore.Mvc.IUrlHelper";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly DiagnosticDescriptor MinimalApiRule = DescriptorFactory.Create(RuleId, MinimalApiMessageFormat);
    private static readonly ImmutableDictionary<string, string> MinimalApiDiagnosticProperties =
        ImmutableDictionary<string, string>.Empty.Add(MinimalApiDiagnosticProperty, string.Empty);

    // LocalRedirect/LocalRedirectPermanent are absent on purpose: they perform the check themselves.
    private static readonly HashSet<string> UncheckedRedirectMethods = new(StringComparer.Ordinal)
    {
        "Redirect",
        "RedirectPermanent",
        "RedirectPreserveMethod",
        "RedirectPermanentPreserveMethod",
    };
    private static readonly HashSet<string> MinimalApiMapMethods = new(StringComparer.Ordinal)
    {
        "MapGet",
        "MapPost",
        "MapPut",
        "MapPatch",
        "MapDelete",
        "MapMethods",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule, MinimalApiRule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (AnalyzeMinimalApiRedirect(context, invocation))
        {
            return;
        }

        if (invocation.ArgumentList is not { Arguments.Count: > 0 } argumentList
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsMvcRedirectMethod(method)
            || argumentList.Arguments[0] is not { } urlArgument
            || GpUrlExpressionHelper.ActionParameterSteeringDestination(context.Model, urlArgument.Expression) is not { } parameterName
            || HasLocalUrlGuard(invocation, urlArgument.Expression, context.Model))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, parameterName);
    }

    internal static bool IsMvcRedirectMethod(IMethodSymbol method) =>
        UncheckedRedirectMethods.Contains(method.Name)
        && method.ContainingType?.ToDisplayString() == ControllerBaseType;

    private static bool AnalyzeMinimalApiRedirect(SonarSyntaxNodeReportingContext context, InvocationExpressionSyntax invocation)
    {
        if (!GpMinimalApi.TryGetResultMethod(context.Model, invocation, out var method)
            || method.Name != "Redirect")
        {
            return false;
        }

        if (!GpMinimalApi.TryGetInlineHandler(invocation, context.Model, MinimalApiMapMethods, out var handler, out _, out _, out _))
        {
            return false;
        }

        if (UrlExpression(invocation, method) is not { } urlExpression
            || GpUrlExpressionHelper.InlineHandlerParameterSteeringDestination(context.Model, urlExpression, handler) is not { } parameterName)
        {
            return true;
        }

        context.ReportIssue(
            MinimalApiRule,
            invocation.GetLocation(),
            properties: MinimalApiDiagnosticProperties,
            messageArgs: new[] { parameterName });
        return true;
    }

    private static ExpressionSyntax UrlExpression(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        var lookup = new CSharpMethodParameterLookup(invocation, method);
        return lookup.GetAllArgumentParameterMappings()
            .FirstOrDefault(x => x.Symbol.Name == "url" && x.Symbol.Type.Is(KnownType.System_String))
            .Node?.Expression;
    }

    private static bool HasLocalUrlGuard(InvocationExpressionSyntax redirect, ExpressionSyntax redirectedUrl, SemanticModel model) =>
        HasPositiveLocalUrlGuard(redirect, redirectedUrl, model)
        || HasNegativeLocalUrlGuardWithEarlyExit(redirect, redirectedUrl, model);

    private static bool HasPositiveLocalUrlGuard(InvocationExpressionSyntax redirect,
                                                 ExpressionSyntax redirectedUrl,
                                                 SemanticModel model) =>
        redirect.Ancestors()
            .OfType<IfStatementSyntax>()
            .Any(x => x.Statement.Span.Contains(redirect.Span)
                      && IsPositiveLocalUrlCondition(x.Condition, redirectedUrl, model)
                      && !IsReassignedBeforeRedirect(x.Statement, redirectedUrl, redirect, model));

    private static bool HasNegativeLocalUrlGuardWithEarlyExit(InvocationExpressionSyntax redirect,
                                                              ExpressionSyntax redirectedUrl,
                                                              SemanticModel model)
    {
        if (redirect.Ancestors().OfType<StatementSyntax>().FirstOrDefault(x => x.Parent is BlockSyntax) is not { Parent: BlockSyntax block } redirectStatement)
        {
            return false;
        }

        var redirectIndex = block.Statements.IndexOf(redirectStatement);
        return block.Statements.Take(redirectIndex)
            .OfType<IfStatementSyntax>()
            .Any(guard => guard.Else is null
                          && IsNegativeLocalUrlCondition(guard.Condition, redirectedUrl, model)
                          && AlwaysExits(guard.Statement)
                          && !IsReassignedBetween(block, guard.Span.End, redirect.SpanStart, redirectedUrl, model));
    }

    private static bool IsPositiveLocalUrlCondition(ExpressionSyntax condition,
                                                    ExpressionSyntax redirectedUrl,
                                                    SemanticModel model)
    {
        condition = RemoveParentheses(condition);
        if (condition is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression))
        {
            return IsPositiveLocalUrlCondition(binary.Left, redirectedUrl, model)
                   || IsPositiveLocalUrlCondition(binary.Right, redirectedUrl, model);
        }

        return IsLocalUrlInvocation(condition, redirectedUrl, model);
    }

    private static bool IsNegativeLocalUrlCondition(ExpressionSyntax condition,
                                                    ExpressionSyntax redirectedUrl,
                                                    SemanticModel model)
    {
        condition = RemoveParentheses(condition);
        return condition is PrefixUnaryExpressionSyntax unary
               && unary.IsKind(SyntaxKind.LogicalNotExpression)
               && IsLocalUrlInvocation(RemoveParentheses(unary.Operand), redirectedUrl, model);
    }

    private static bool IsLocalUrlInvocation(ExpressionSyntax condition,
                                             ExpressionSyntax redirectedUrl,
                                             SemanticModel model) =>
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
                                                   SemanticModel model) =>
        IsReassignedBetween(guardedStatement, guardedStatement.SpanStart, redirect.SpanStart, redirectedUrl, model);

    private static bool IsReassignedBetween(SyntaxNode scope,
                                            int start,
                                            int end,
                                            ExpressionSyntax redirectedUrl,
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

        return scope.DescendantNodes()
            .Where(x => x.SpanStart >= start && x.SpanStart < end)
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
