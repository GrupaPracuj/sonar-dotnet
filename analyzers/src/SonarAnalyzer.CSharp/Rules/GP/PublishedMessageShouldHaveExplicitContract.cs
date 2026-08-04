namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedMessageShouldHaveExplicitContract : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0055";

    private const string MessageFormat = "Publish a declared contract type instead of {0}.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    private static readonly HashSet<string> PublishMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
    };

    private static readonly Dictionary<string, string> ShapelessTypes = new(StringComparer.Ordinal)
    {
        ["System.Dynamic.ExpandoObject"] = "an ExpandoObject",
        ["System.Text.Json.JsonElement"] = "a JsonElement",
        ["System.Text.Json.JsonDocument"] = "a JsonDocument",
        ["Newtonsoft.Json.Linq.JObject"] = "a JObject",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !PublishMethods.Contains(method.Name)
            || !IsMessagingCall(method)
            || MessageType(context.Model, invocation, method) is not { } messageType
            || Describe(messageType) is not { } description)
        {
            return;
        }

        context.ReportIssue(Rule, invocation, description);
    }

    private static bool IsMessagingCall(IMethodSymbol method)
    {
        var containing = method.ContainingType?.ToDisplayString() ?? string.Empty;
        return containing.StartsWith("GP.Juno", StringComparison.Ordinal)
               || containing.StartsWith("MassTransit", StringComparison.Ordinal)
               || (method.ContainingType?.AllInterfaces.Any(x => x.ToDisplayString() is { } name
                   && (name.StartsWith("GP.Juno", StringComparison.Ordinal) || name.StartsWith("MassTransit", StringComparison.Ordinal))) ?? false);
    }

    private static ITypeSymbol MessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.TypeArguments.FirstOrDefault()
        ?? (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } firstArgument
            ? model.GetTypeInfo(firstArgument).Type
            : null);

    // Returns how to name the problem, or null when the type is a proper contract.
    private static string Describe(ITypeSymbol type)
    {
        if (type.IsAnonymousType)
        {
            return "an anonymous type";
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            return "'object'";
        }

        if (type.TypeKind == TypeKind.Dynamic)
        {
            return "'dynamic'";
        }

        if (ShapelessTypes.TryGetValue(type.ToDisplayString(), out var known))
        {
            return known;
        }

        // A dictionary keyed by string with object values is a payload with no declared shape.
        return type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 2 } dictionary
               && GpCollectionEndpointHelper.IsCollectionLike(dictionary)
               && dictionary.TypeArguments[0].SpecialType == SpecialType.System_String
               && dictionary.TypeArguments[1].SpecialType == SpecialType.System_Object
            ? "a loose dictionary"
            : null;
    }
}
