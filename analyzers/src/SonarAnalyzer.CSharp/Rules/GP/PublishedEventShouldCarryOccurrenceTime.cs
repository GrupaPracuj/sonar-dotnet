using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedEventShouldCarryOccurrenceTime : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0051";

    private const string MessageFormat = "'{0}' is published as an event but does not state when it occurred - add a DateTimeOffset OccurredAt.";

    private static readonly HashSet<string> OccurrenceTimeNames = new(StringComparer.Ordinal)
    {
        "OccurredAt",
        "OccurredAtUtc",
    };

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var publishedEvents = new ConcurrentDictionary<string, PublishedEventUse>(StringComparer.Ordinal);
            start.RegisterNodeAction(c => CollectPublishedEvent(c, publishedEvents), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => Report(c, publishedEvents.Values));
        });

    private static void CollectPublishedEvent(SonarSyntaxNodeReportingContext context,
                                              ConcurrentDictionary<string, PublishedEventUse> publishedEvents)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (GpMessageContracts.PublishedType(context.Model, invocation) is not { } eventType)
        {
            return;
        }

        publishedEvents
            .GetOrAdd(GpMessageContracts.TypeKey(eventType), _ => new PublishedEventUse(eventType))
            .PublishLocations
            .Add(invocation.GetLocation());
    }

    private static void Report(SonarCompilationReportingContext context, IEnumerable<PublishedEventUse> publishedEvents)
    {
        foreach (var publishedEvent in publishedEvents.Where(x => !HasOccurrenceTime(x.Type)))
        {
            var location = DeclarationLocation(publishedEvent.Type) ?? FirstPublishLocation(publishedEvent.PublishLocations);
            if (location is not null)
            {
                context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, Rule, location, messageArgs: new[] { publishedEvent.Type.Name });
            }
        }
    }

    private static Location DeclarationLocation(INamedTypeSymbol eventType) =>
        eventType.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<BaseTypeDeclarationSyntax>()
            .OrderBy(x => x.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(x => x.SpanStart)
            .Select(x => x.Identifier.GetLocation())
            .FirstOrDefault();

    // A referenced contract has no declaration in the current compilation. Keep one deterministic usage-level
    // finding in that case rather than silently dropping the problem.
    private static Location FirstPublishLocation(IEnumerable<Location> locations) =>
        locations
            .OrderBy(x => x.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(x => x.SourceSpan.Start)
            .FirstOrDefault();

    // DateTimeOffset rather than DateTime, on the same grounds as S6566: an instant that crosses a service boundary
    // needs its offset to be interpretable on the other side.
    private static bool HasOccurrenceTime(INamedTypeSymbol eventType) =>
        GpMessageContracts.DataMembers(eventType)
            .Any(x => OccurrenceTimeNames.Contains(x.Name)
                      && x.Type.Is(KnownType.System_DateTimeOffset));

    private sealed class PublishedEventUse(INamedTypeSymbol type)
    {
        public INamedTypeSymbol Type { get; } = type;

        public ConcurrentBag<Location> PublishLocations { get; } = new();
    }
}
