/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CreateEndpointsShouldReturnCreated : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0026";

    private const string MessageFormat = "Method '{0}' looks like it creates a resource - return 201 (Created/CreatedAtAction) instead of 200 (Ok).";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> CreationVerbs = new(StringComparer.Ordinal) { "Create", "Add", "Insert", "Register" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeReturnStatement, SyntaxKind.ReturnStatement);

    private static void AnalyzeReturnStatement(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not ReturnStatementSyntax { Expression: InvocationExpressionSyntax invocation }
            || context.Model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol method
            || !IsHttpPostCreateMethod(method)
            || !GpMvcResults.IsResponseFactory(context.Model, invocation, "Ok"))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, method.Name);
    }

    private static bool IsHttpPostCreateMethod(IMethodSymbol method) =>
        method.IsControllerActionMethod
        && method.GetAttributes().Select(x => x.AttributeClass?.Name).Any(x => x is "HttpPost" or "HttpPostAttribute")
        && CreationVerbs.Contains(GpIdentifierWords.LeadingWord(method.Name))
        && !IsAddActionOnExistingResource(method)
        && !IsDryRunMethod(method);

    private static bool IsAddActionOnExistingResource(IMethodSymbol method)
    {
        if (GpIdentifierWords.LeadingWord(method.Name) != "Add")
        {
            return false;
        }

        return method.GetAttributes()
            .Where(x => x.AttributeClass?.Name is "HttpPost" or "HttpPostAttribute")
            .SelectMany(x => x.ConstructorArguments)
            .Select(x => x.Value as string)
            .Any(IsSingleRouteParameter);
    }

    private static bool IsSingleRouteParameter(string route)
    {
        route = route?.Trim('/');
        return route is { Length: > 2 }
               && route[0] == '{'
               && route[route.Length - 1] == '}'
               && route.IndexOf('/') < 0;
    }

    private static bool IsDryRunMethod(IMethodSymbol method)
    {
        var words = GpIdentifierWords.SplitWords(method.Name).ToArray();
        for (var i = 0; i < words.Length - 1; i++)
        {
            if (words[i].Equals("Dry", StringComparison.OrdinalIgnoreCase)
                && words[i + 1].Equals("Run", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
