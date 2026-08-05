namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedEventShouldCarryBusinessIdentifier : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0073";

    private const string MessageFormat = "'{0}' carries no business identifier, so a consumer cannot tell what it is about.";

    private const string DefaultIdentifierSuffixes = "Id,Number,Reference,Code,Key";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // These identify the message, not the thing it is about: a redelivery changes them for the same fact.
    private static readonly HashSet<string> TransportIdentifiers = new(StringComparer.Ordinal)
    {
        "MessageId",
        "CorrelationId",
        "ConversationId",
        "RequestId",
        "InitiatorId",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("identifierSuffixes", PropertyType.String, "Comma-separated member name suffixes accepted as a business identifier", DefaultIdentifierSuffixes)]
    public string IdentifierSuffixes { get; set; } = DefaultIdentifierSuffixes;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpMessageContracts.PublishedType(context.Model, invocation) is not { } eventType)
        {
            return;
        }

        var members = GpMessageContracts.DataMembers(eventType).ToList();
        var suffixes = GpEntityTypes.SplitParameter(IdentifierSuffixes);

        // A marker event has nothing to identify, and demanding a key would only produce an unused field.
        if (members.Count == 0 || suffixes.Length == 0 || members.Any(x => IsBusinessIdentifier(x.Name, suffixes)))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, eventType.Name);
    }

    private static bool IsBusinessIdentifier(string memberName, string[] suffixes) =>
        !TransportIdentifiers.Contains(memberName)
        && Array.Exists(suffixes, x => memberName.EndsWith(x, StringComparison.Ordinal));
}
