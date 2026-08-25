/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublishedEventShouldCarryBusinessIdentifier : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0073";

    private const string MessageFormat = "'{0}' carries no business identifier, so a consumer cannot tell what it is about.";

    // Surrogate keys first, then the natural keys a domain event is just as often identified by: an address, a
    // login, a raw GUID. Without them the rule asks for an Id that the contract has no reason to carry.
    private const string DefaultIdentifierSuffixes =
        "Id,Number,Reference,Code,Key,Guid,Uuid,Email,EmailAddress,Login,Username";
    private const int MaxNestedContractDepth = 5;

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

        if (eventType.Name.EndsWith("Command", StringComparison.Ordinal)
            || GpMessageContracts.IsNestedMessageEnvelope(eventType))
        {
            return;
        }

        var members = GpMessageContracts.DataMembers(eventType).ToList();
        var suffixes = GpEntityTypes.SplitParameter(IdentifierSuffixes);

        // A marker event has nothing to identify, and demanding a key would only produce an unused field.
        if (members.Count == 0 || suffixes.Length == 0 || HasBusinessIdentifier(eventType, suffixes))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, eventType.Name);
    }

    private static bool IsBusinessIdentifier(string memberName, string[] suffixes) =>
        !TransportIdentifiers.Contains(memberName)
        && Array.Exists(suffixes, x => memberName.EndsWith(x, StringComparison.Ordinal));

    private static bool HasBusinessIdentifier(INamedTypeSymbol type, string[] suffixes) =>
        HasBusinessIdentifier(type, suffixes, 0, new HashSet<string>(StringComparer.Ordinal));

    // Traverse nested contract-like members by symbol shape rather than invocation syntax. Depth is capped and already
    // visited types are skipped, so a self-referential payload cannot recurse forever.
    private static bool HasBusinessIdentifier(INamedTypeSymbol type, string[] suffixes, int depth, HashSet<string> visited)
    {
        if (!visited.Add(GpMessageContracts.TypeKey(type)))
        {
            return false;
        }

        var members = GpMessageContracts.DataMembers(type).ToList();
        if (members.Any(x => IsBusinessIdentifier(x.Name, suffixes)))
        {
            return true;
        }

        if (depth >= MaxNestedContractDepth)
        {
            return false;
        }

        foreach (var nested in members
                     .SelectMany(x => NestedContractTypes(x.Type))
                     .GroupBy(x => GpMessageContracts.TypeKey(x), StringComparer.Ordinal)
                     .Select(x => x.First()))
        {
            if (HasBusinessIdentifier(nested, suffixes, depth + 1, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> NestedContractTypes(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            foreach (var nested in NestedContractTypes(array.ElementType))
            {
                yield return nested;
            }

            yield break;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } generic
            && (generic.OriginalDefinition.Is(KnownType.System_Nullable_T) || GpCollectionEndpointHelper.IsCollectionLike(generic)))
        {
            foreach (var argument in generic.TypeArguments)
            {
                foreach (var nested in NestedContractTypes(argument))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None } named
            && !IsFrameworkType(named))
        {
            yield return named;
        }
    }

    private static bool IsFrameworkType(ITypeSymbol type) =>
        (type.ContainingNamespace?.ToDisplayString() ?? string.Empty) is var containing
        && (containing == "System"
            || containing.StartsWith("System.", StringComparison.Ordinal)
            || containing.StartsWith("Microsoft.", StringComparison.Ordinal));
}
