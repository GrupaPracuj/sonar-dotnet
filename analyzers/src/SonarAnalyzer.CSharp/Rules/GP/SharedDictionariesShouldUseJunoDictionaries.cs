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
            || !ContainsSkidblandirUrl(invocation, method))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool ContainsSkidblandirUrl(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (IsJunoHttpCall(method))
        {
            // Juno carries the service name in the fluent receiver chain:
            // builder.Service("skidblandir").AddPath(...).GetJson<T>().
            return invocation.Expression is MemberAccessExpressionSyntax memberAccess
                   && ContainsSkidblandir(memberAccess.Expression);
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        return lookup.GetAllArgumentParameterMappings()
            .Where(x => x.Symbol.Name is "address" or "requestUri" or "requestUriString" or "url")
            .Any(x => ContainsSkidblandir(x.Node.Expression));
    }

    private static bool IsJunoHttpCall(IMethodSymbol method) =>
        (method.ReceiverType ?? method.ContainingType)?.ToDisplayString()
            .StartsWith("GP.Juno.HttpClient.", StringComparison.Ordinal) == true;

    private static bool ContainsSkidblandir(SyntaxNode node) =>
        node.DescendantNodesAndSelf().Any(x =>
            x is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && HasSkidblandirToken(literal.Token.ValueText)
            || x is InterpolatedStringTextSyntax interpolated
                && HasSkidblandirToken(interpolated.TextToken.ValueText));

    private static bool HasSkidblandirToken(string value) =>
        value.IndexOf("skidblandir", StringComparison.OrdinalIgnoreCase) >= 0;
}
