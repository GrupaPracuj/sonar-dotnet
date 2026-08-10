namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedMessageShouldHaveExplicitContract : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0055";

    private const string MessageFormat = "Publish a declared contract type instead of {0}.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> PublishMethods = new(StringComparer.Ordinal)
    {
        "Publishes",
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !PublishMethods.Contains(method.Name)
            || !GpMessageContracts.IsMessagingMethod(method)
            || HasExplicitRuntimeType(context.Model, invocation, method)
            || MessageType(context.Model, invocation, method) is not { } messageType
            || GpMessageContracts.DescribeShapelessType(messageType) is not { } description)
        {
            return;
        }

        context.ReportIssue(Rule, invocation, description);
    }

    private static bool HasExplicitRuntimeType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.Name == "Publish"
        && method.ContainingNamespace.ToDisplayString() == "MassTransit"
        && invocation.ArgumentList.Arguments.Select((argument, index) => (Argument: argument, Index: index)).Any(x =>
            (x.Argument.NameColon is { Name.Identifier.ValueText: var parameterName }
                ? method.Parameters.FirstOrDefault(parameter => parameter.Name == parameterName)
                : x.Index < method.Parameters.Length ? method.Parameters[x.Index] : null)
            ?.Type.Is(KnownType.System_Type) == true);

    private static ITypeSymbol MessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.TypeArguments.FirstOrDefault()
        ?? (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } firstArgument
            ? model.GetTypeInfo(firstArgument).Type
            : null);
}
