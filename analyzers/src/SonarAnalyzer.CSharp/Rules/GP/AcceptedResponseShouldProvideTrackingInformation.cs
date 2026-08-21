/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AcceptedResponseShouldProvideTrackingInformation : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0114";

    private const string MessageFormat = "Provide a tracking URI or response body with this 202 Accepted response.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpMvcResults.TryGetResultMethod(context.Model, invocation, out var mvcMethod))
        {
            if (IsEmptyAccepted(context.Model, invocation, mvcMethod)
                || IsEmptyStatusCode202(context.Model, invocation, mvcMethod))
            {
                context.ReportIssue(Rule, invocation);
            }
        }
        else if (GpMinimalApi.TryGetResultMethod(context.Model, invocation, out var minimalMethod)
                 && (IsEmptyAccepted(context.Model, invocation, minimalMethod)
                     || IsEmptyStatusCode202(context.Model, invocation, minimalMethod)))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    private static bool IsEmptyAccepted(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.Name == "Accepted"
        && AllResponseArgumentsAreNull(model, invocation, method, null);

    private static bool IsEmptyStatusCode202(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method.Name != "StatusCode")
        {
            return false;
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        var statusCode = lookup.GetAllArgumentParameterMappings().FirstOrDefault(x => x.Symbol.Name == "statusCode");
        return statusCode.Node is not null
            && model.GetConstantValue(statusCode.Node.Expression) is { HasValue: true, Value: 202 }
            && AllResponseArgumentsAreNull(model, invocation, method, "statusCode");
    }

    private static bool AllResponseArgumentsAreNull(SemanticModel model,
                                                    InvocationExpressionSyntax invocation,
                                                    IMethodSymbol method,
                                                    string ignoredParameter) =>
        new CSharpMethodParameterLookup(invocation, method).GetAllArgumentParameterMappings()
            .Where(x => x.Symbol.Name != ignoredParameter)
            .All(x => model.GetConstantValue(x.Node.Expression) is { HasValue: true, Value: null });
}
