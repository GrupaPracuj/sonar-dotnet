/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeclaredResponseStatusShouldBeReturned : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0125";

    private const string MessageFormat = "HTTP status {0} is declared but no action path returns it.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not { } method
            || !GpOpenApiMetadata.IsOpenApiAction(method)
            || method.IsAbstract
            || GpOpenApiMetadata.IsIgnored(method)
            || GpOpenApiMetadata.UsesApiConvention(method))
        {
            return;
        }

        var returnedExpressions = ReturnedExpressions(declaration).ToArray();
        var returnedStatuses = returnedExpressions
            .Select(x => x is InvocationExpressionSyntax invocation
                ? GpOpenApiMetadata.ResponseStatusCode(context.Model, invocation)
                : null)
            .ToArray();
        if (returnedStatuses.Length == 0 || returnedStatuses.Any(x => x is null))
        {
            return;
        }

        var returned = returnedStatuses.WhereNotNull().ToHashSet();
        foreach (var declarationGroup in DeclaredResponses(method)
                     .Where(x => x.Status is >= 200 and < 400 && !returned.Contains(x.Status))
                     .GroupBy(x => x.Status)
                     .OrderBy(x => x.Key))
        {
            context.ReportIssue(Rule, declarationGroup.First().Syntax, declarationGroup.Key.ToString());
        }
    }

    private static IEnumerable<(int Status, SyntaxNode Syntax)> DeclaredResponses(IMethodSymbol method) =>
        method.GetAttributes()
            .Where(GpOpenApiMetadata.IsResponseAttribute)
            .Select(x => (Status: GpOpenApiMetadata.ResponseStatusCode(x), Syntax: x.ApplicationSyntaxReference?.GetSyntax()))
            .Where(x => x.Status is not null && x.Syntax is not null)
            .Select(x => (x.Status.Value, x.Syntax));

    private static IEnumerable<ExpressionSyntax> ReturnedExpressions(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody?.Expression is { } expressionBody)
        {
            return ResponseExpressions(expressionBody);
        }

        return method.Body is null
            ? Enumerable.Empty<ExpressionSyntax>()
            : method.Body.DescendantNodes(x =>
                    x.Kind() is not (SyntaxKindEx.LocalFunctionStatement
                        or SyntaxKind.SimpleLambdaExpression
                        or SyntaxKind.ParenthesizedLambdaExpression
                        or SyntaxKind.AnonymousMethodExpression))
                .OfType<ReturnStatementSyntax>()
                .Select(x => x.Expression)
                .WhereNotNull()
                .SelectMany(ResponseExpressions);
    }

    private static IEnumerable<ExpressionSyntax> ResponseExpressions(ExpressionSyntax expression)
    {
        expression = expression.RemoveParentheses() as ExpressionSyntax ?? expression;
        if (SwitchExpressionSyntaxWrapper.IsInstance(expression))
        {
            return ((SwitchExpressionSyntaxWrapper)expression).Arms.SelectMany(x => ResponseExpressions(x.Expression));
        }
        if (ThrowExpressionSyntaxWrapper.IsInstance(expression))
        {
            return Enumerable.Empty<ExpressionSyntax>();
        }

        return expression switch
        {
            ConditionalExpressionSyntax conditional => ResponseExpressions(conditional.WhenTrue).Concat(ResponseExpressions(conditional.WhenFalse)),
            _ => [expression],
        };
    }
}
