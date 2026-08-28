/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotLogPersonalData : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0022";

    private const string MessageFormat = "Do not log '{0}' - its name suggests it holds personal data.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { } argumentList || !GpLoggingHelper.IsLoggingCall(context.Model, invocation))
        {
            return;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (!IsException(context.Model, argument.Expression)
                && GpLoggingHelper.CandidateNames(argument.Expression).FirstOrDefault(GpIdentifierWords.ContainsPiiWord) is { } name)
            {
                context.ReportIssue(Rule, argument, name);
                return; // one finding per logging call is enough
            }
        }
    }

    // A caught exception is routinely named after the operation that failed - "emailEx" for a failed mail send - and
    // logging it is the right thing to do: the object carries a stack trace, not the address. Only the name looked
    // like personal data, so the type has to decide.
    private static bool IsException(SemanticModel model, ExpressionSyntax expression) =>
        GpJunoTypes.DerivesFrom(model.GetTypeInfo(expression).Type, "System.Exception");
}
