namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotInterpolateRequestInputIntoLogMessage : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0030";

    private const string MessageFormat = "Pass '{0}' as a logging argument instead of interpolating it into the message template - it comes straight from the request.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> MessageParameterNames = new(StringComparer.Ordinal)
    {
        "format",
        "message",
        "messageTemplate",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!GpLoggingHelper.IsLoggingCall(context.Model, invocation)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || method.Parameters.FirstOrDefault(x => MessageParameterNames.Contains(x.Name) && x.Type.Is(KnownType.System_String)) is not { } messageParameter
            || !new CSharpMethodParameterLookup(invocation, method).TryGetSyntax(messageParameter, out var messageArguments))
        {
            return;
        }

        foreach (var expression in messageArguments.OfType<ExpressionSyntax>().Where(IsBuiltMessage))
        {
            if (GpRequestInputHelper.ActionParameterName(context.Model, expression) is { } parameterName)
            {
                context.ReportIssue(Rule, expression, parameterName);
                return; // one finding per logging call is enough
            }
        }
    }

    // A template that is interpolated or concatenated instead of being a constant - the two forms CA2254 also
    // describes. Any other argument shape passes the value alongside the template, which is what this rule wants.
    private static bool IsBuiltMessage(ExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.InterpolatedStringExpression)
        || expression.IsKind(SyntaxKind.AddExpression);
}
