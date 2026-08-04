namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedEventShouldCarryOccurrenceTime : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0051";

    private const string MessageFormat = "'{0}' is published as an event but does not state when it occurred - add a DateTimeOffset {1}.";

    private const string DefaultOccurrenceTimeNames = "OccurredAt,OccurredOn,Timestamp";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("occurrenceTimeNames", PropertyType.String, "Comma-separated member names accepted as the occurrence time", DefaultOccurrenceTimeNames)]
    public string OccurrenceTimeNames { get; set; } = DefaultOccurrenceTimeNames;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpMessageContracts.PublishedType(context.Model, invocation) is not { } eventType)
        {
            return;
        }

        var accepted = GpEntityTypes.SplitParameter(OccurrenceTimeNames);
        if (accepted.Length == 0 || HasOccurrenceTime(eventType, accepted))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, eventType.Name, accepted[0]);
    }

    // DateTimeOffset rather than DateTime, on the same grounds as S6566: an instant that crosses a service boundary
    // needs its offset to be interpretable on the other side.
    private static bool HasOccurrenceTime(INamedTypeSymbol eventType, string[] acceptedNames) =>
        GpMessageContracts.DataMembers(eventType)
            .Any(x => Array.Exists(acceptedNames, y => string.Equals(x.Name, y, StringComparison.Ordinal))
                      && x.Type.Is(KnownType.System_DateTimeOffset));
}
