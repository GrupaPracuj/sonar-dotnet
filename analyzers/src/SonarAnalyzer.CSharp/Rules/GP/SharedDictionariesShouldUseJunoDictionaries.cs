namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SharedDictionariesShouldUseJunoDictionaries : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0007";

    private const string MessageFormat = "Use Juno.Dictionaries instead of calling Skidblandir API directly.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !GpHttpCallHelper.IsHttpCall(method)
            || !ContainsSkidblandirUrl(invocation))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool ContainsSkidblandirUrl(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments
            .Select(x => x.Expression)
            .Any(ContainsSkidblandir);

    private static bool ContainsSkidblandir(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) =>
                HasSkidblandirToken(literal.Token.ValueText),
            InterpolatedStringExpressionSyntax interpolated =>
                interpolated.Contents
                    .OfType<InterpolatedStringTextSyntax>()
                    .Any(x => HasSkidblandirToken(x.TextToken.ValueText)),
            _ => false
        };

    private static bool HasSkidblandirToken(string value) =>
        value.IndexOf("skidblandir", StringComparison.OrdinalIgnoreCase) >= 0;
}
