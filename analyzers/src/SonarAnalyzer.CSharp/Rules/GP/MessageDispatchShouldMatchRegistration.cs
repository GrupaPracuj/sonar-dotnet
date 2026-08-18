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
public sealed class MessageDispatchShouldMatchRegistration : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0106";

    private const string MessageFormat = "'{0}' is registered with '{1}' but dispatched with '{2}'.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly IReadOnlyDictionary<string, UseKind> InvocationKinds =
        new Dictionary<string, UseKind>(StringComparer.Ordinal)
        {
            ["Sends"] = UseKind.Sends,
            ["Publishes"] = UseKind.Publishes,
            ["Send"] = UseKind.Send,
            ["Publish"] = UseKind.Publish,
            ["PublishBatch"] = UseKind.Publish,
        };
    private static readonly HashSet<string> SupportedMethods = new(InvocationKinds.Keys, StringComparer.Ordinal);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var uses = new ConcurrentDictionary<string, MessageUse>(StringComparer.Ordinal);
            start.RegisterNodeAction(c => Collect(c, uses), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => Report(c, uses.Values));
        });

    private static void Collect(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, MessageUse> uses)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || InvocationKind(method.Name) is not { } kind
            || GpMessageContracts.MessagingPayloadType(context.Model, invocation, SupportedMethods) is not { } messageType)
        {
            return;
        }

        uses.GetOrAdd(GpMessageContracts.TypeKey(messageType), _ => new MessageUse(messageType))
            .Locations[kind]
            .Add(invocation.GetLocation());
    }

    private static void Report(SonarCompilationReportingContext context, IEnumerable<MessageUse> uses)
    {
        foreach (var use in uses)
        {
            var hasSends = use.Locations[UseKind.Sends].Any();
            var hasPublishes = use.Locations[UseKind.Publishes].Any();
            if (hasSends && !hasPublishes)
            {
                Report(context, use, UseKind.Publish, "Sends", "Publish");
            }
            if (hasPublishes && !hasSends)
            {
                Report(context, use, UseKind.Send, "Publishes", "Send");
            }
        }
    }

    private static void Report(SonarCompilationReportingContext context,
                               MessageUse use,
                               UseKind dispatchKind,
                               string registration,
                               string dispatch)
    {
        foreach (var location in use.Locations[dispatchKind])
        {
            context.ReportIssue(
                CSharpGeneratedCodeRecognizer.Instance,
                Rule,
                location,
                messageArgs: new[] { use.Type.Name, registration, dispatch });
        }
    }

    private static UseKind? InvocationKind(string methodName) =>
        InvocationKinds.TryGetValue(methodName, out var kind) ? kind : null;

    private sealed class MessageUse(INamedTypeSymbol type)
    {
        public INamedTypeSymbol Type { get; } = type;

        public IReadOnlyDictionary<UseKind, ConcurrentBag<Location>> Locations { get; } =
            Enum.GetValues(typeof(UseKind))
                .Cast<UseKind>()
                .ToDictionary(x => x, _ => new ConcurrentBag<Location>());
    }

    private enum UseKind
    {
        Sends,
        Publishes,
        Send,
        Publish,
    }
}
