/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// AI003 / GP0141. Reports IChatClient and AIAgent invocations when the absence of a positive output cap is provable.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AiModelInvocationShouldSetMaxOutputTokens : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0141";

    private const string MessageFormat = "Set 'MaxOutputTokens' on the ChatOptions passed to this AI model invocation to a positive value.";
    private const string MaxOutputTokensProperty = "MaxOutputTokens";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !GpAi.IsModelInvocation(method)
            || !GpAi.TryFindArgument(invocation, method, IsChatOptionsParameter, out var optionsExpression, out _))
        {
            return;
        }

        if (IsMissingOutputCap(context.Model, optionsExpression, invocation))
        {
            context.ReportIssue(Rule, invocation);
        }
    }

    private static bool IsChatOptionsParameter(IParameterSymbol parameter) =>
        GpAi.IsChatOptions(parameter.Type);

    private static bool IsMissingOutputCap(SemanticModel model, ExpressionSyntax optionsExpression, SyntaxNode usageSite)
    {
        if (optionsExpression is null || GpAi.IsNullLiteral(optionsExpression))
        {
            return true;
        }

        if (!GpAi.TryResolveEffectivePropertyValue(model, optionsExpression, usageSite, MaxOutputTokensProperty, out var value))
        {
            return false; // Not provable from here - accept rather than guess.
        }

        if (value is null)
        {
            return true; // ChatOptions is freshly created here and never sets the cap, before or after construction.
        }

        return model.GetConstantValue(value) is { HasValue: true, Value: int limit } && limit <= 0;
    }
}
