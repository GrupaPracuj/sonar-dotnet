/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

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

    private static readonly HashSet<string> CloudEventsEnvelopeMembers = new(StringComparer.Ordinal)
    {
        "Data",
        "Id",
        "Source",
        "SpecVersion",
        "Time",
        "Type",
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

        if (eventType.Name.EndsWith("Command", StringComparison.Ordinal)
            || GpMessageContracts.IsNestedMessageEnvelope(eventType))
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
                      && x.Type.Is(KnownType.System_DateTimeOffset))
        || HasCloudEventsEnvelopeTime(eventType);

    // CloudEvents defines the occurrence timestamp as the envelope's Time attribute. Accept it only when that exact
    // CloudEvents-envelope shape is present, so an arbitrary Time member on some unrelated contract still does not
    // satisfy the rule.
    private static bool HasCloudEventsEnvelopeTime(INamedTypeSymbol eventType) =>
        BaseTypesAndSelf(eventType)
            .SelectMany(x => x.GetMembers("Time").OfType<IPropertySymbol>())
            .Any(IsCloudEventsEnvelopeTimeProperty);

    private static bool IsCloudEventsEnvelopeTimeProperty(IPropertySymbol property) =>
        property is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsIndexer: false }
        && IsDateTimeOrDateTimeOffset(property.Type)
        && DeclaredOnCloudEventsEnvelope(property);

    private static bool DeclaredOnCloudEventsEnvelope(IPropertySymbol property)
    {
        for (var current = property; current is not null; current = current.OverriddenProperty)
        {
            if (current.ContainingType is { Name: "CloudEventsEnvelope" } envelope
                && CloudEventsEnvelopeMembers.All(name =>
                    envelope.GetMembers(name).OfType<IPropertySymbol>().Any(x =>
                        x is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsIndexer: false })))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDateTimeOrDateTimeOffset(ITypeSymbol type) =>
        type.IsAny(KnownType.System_DateTime, KnownType.System_DateTimeOffset)
        || type is INamedTypeSymbol { IsGenericType: true } named
           && named.OriginalDefinition.Is(KnownType.System_Nullable_T)
           && named.TypeArguments[0].IsAny(KnownType.System_DateTime, KnownType.System_DateTimeOffset);

    private static bool IsDateTimeOffsetOrNullableDateTimeOffset(ITypeSymbol type) =>
        type.Is(KnownType.System_DateTimeOffset)
        || type is INamedTypeSymbol { IsGenericType: true } named
           && named.OriginalDefinition.Is(KnownType.System_Nullable_T)
           && named.TypeArguments[0].Is(KnownType.System_DateTimeOffset);

    private static IEnumerable<INamedTypeSymbol> BaseTypesAndSelf(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private sealed class PublishedEventUse(INamedTypeSymbol type)
    {
        public INamedTypeSymbol Type { get; } = type;

        public ConcurrentBag<Location> PublishLocations { get; } = new();
    }
}
