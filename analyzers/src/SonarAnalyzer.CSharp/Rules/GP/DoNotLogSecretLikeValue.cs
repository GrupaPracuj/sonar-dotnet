namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotLogSecretLikeValue : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0019";

    private const string MessageFormat = "Do not log '{0}' - its name suggests it holds a secret.";

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

        var arguments = argumentList.Arguments;
        var templateIndex = arguments.IndexOf(arguments.FirstOrDefault(x =>
            x.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)));

        if (templateIndex >= 0
            && arguments[templateIndex].Expression is LiteralExpressionSyntax template)
        {
            var valueArguments = arguments.Skip(templateIndex + 1).ToArray();
            var placeholders = GpLoggingHelper.ExtractPlaceholderNames(template.Token.ValueText).ToArray();
            for (var i = 0; i < Math.Min(placeholders.Length, valueArguments.Length); i++)
            {
                if (GpIdentifierWords.ContainsSecretWord(placeholders[i])
                    && !IsCancellationToken(context.Model, valueArguments[i].Expression))
                {
                    context.ReportIssue(Rule, arguments[templateIndex], placeholders[i]);
                    return;
                }
            }
        }

        foreach (var argument in arguments.Where((_, index) => index != templateIndex))
        {
            if (!IsCancellationToken(context.Model, argument.Expression)
                && GpLoggingHelper.CandidateNames(argument.Expression).FirstOrDefault(GpIdentifierWords.ContainsSecretWord) is { } name)
            {
                context.ReportIssue(Rule, argument, name);
                return; // one finding per logging call is enough
            }
        }
    }

    private static bool IsCancellationToken(SemanticModel model, ExpressionSyntax expression) =>
        model.GetTypeInfo(expression).Type.Is(KnownType.System_Threading_CancellationToken);
}
