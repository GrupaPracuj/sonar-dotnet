using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotCarrySecrets : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0044";

    private const string MessageFormat = "'{0}' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> MessagingMethods = new(StringComparer.Ordinal)
    {
        "Publishes",
        "Publish",
        "PublishBatch",
        "Send",
        "Sends",
        "RespondAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var candidates = new ConcurrentDictionary<string, SecretCandidate>(StringComparer.Ordinal);
            var messageUses = new ConcurrentDictionary<string, MessageUse>(StringComparer.Ordinal);
            start.RegisterNodeAction(c => AnalyzeProperty(c, candidates), SyntaxKind.PropertyDeclaration);
            start.RegisterNodeAction(c => AnalyzeRecordParameters(c, candidates), SyntaxKindEx.RecordDeclaration);
            start.RegisterNodeAction(c => AnalyzeMessagingUse(c, messageUses), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => Report(c, candidates.Values, messageUses));
        });

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, SecretCandidate> candidates)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (GpIdentifierWords.ContainsSecretWord(declaration.Identifier.ValueText)
            && context.Model.GetDeclaredSymbol(declaration) is { ContainingType: { } containingType, Type: { } type }
            && CanCarrySecret(type))
        {
            AddCandidate(candidates, containingType, declaration.Identifier);
        }
    }

    // A positional record declares its members in the parameter list, so those need checking too.
    private static void AnalyzeRecordParameters(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, SecretCandidate> candidates)
    {
        if (context.Node is not TypeDeclarationSyntax declaration
            || ParameterList(declaration) is not { } parameterList)
        {
            return;
        }

        var containingType = context.Model.GetDeclaredSymbol(declaration);
        foreach (var parameter in parameterList.Parameters.Where(x =>
                     GpIdentifierWords.ContainsSecretWord(x.Identifier.ValueText)
                     && context.Model.GetDeclaredSymbol(x) is IParameterSymbol parameterSymbol
                     && CanCarrySecret(parameterSymbol.Type)))
        {
            AddCandidate(candidates, containingType, parameter.Identifier);
        }
    }

    private static ParameterListSyntax ParameterList(TypeDeclarationSyntax declaration) =>
        RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            ? ((RecordDeclarationSyntaxWrapper)declaration).ParameterList
            : null;

    private static void AnalyzeMessagingUse(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, MessageUse> messageUses)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !MessagingMethods.Contains(method.Name)
            || !GpMessageContracts.IsMessagingMethod(method)
            || MessageType(context.Model, invocation, method) is not INamedTypeSymbol messageType)
        {
            return;
        }

        messageUses
            .GetOrAdd(GpMessageContracts.TypeKey(messageType), _ => new MessageUse(messageType))
            .Locations
            .Add(invocation.GetLocation());
    }

    private static ITypeSymbol MessageType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.TypeArguments.FirstOrDefault()
        ?? (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } argument
            ? model.GetTypeInfo(argument).Type
            : null);

    private static void AddCandidate(ConcurrentDictionary<string, SecretCandidate> candidates,
                                     INamedTypeSymbol containingType,
                                     SyntaxToken identifier)
    {
        if (containingType is not null)
        {
            var candidate = new SecretCandidate(GpMessageContracts.TypeKey(containingType), identifier.GetLocation(), identifier.ValueText);
            candidates.TryAdd($"{candidate.TypeKey}|{identifier.SyntaxTree.GetHashCode()}|{candidate.Location.SourceSpan.Start}", candidate);
        }
    }

    private static void Report(SonarCompilationReportingContext context,
                               IEnumerable<SecretCandidate> candidates,
                               ConcurrentDictionary<string, MessageUse> messageUses)
    {
        var candidatesByType = candidates.ToLookup(x => x.TypeKey, StringComparer.Ordinal);
        foreach (var candidate in candidates.Where(x => messageUses.ContainsKey(x.TypeKey)))
        {
            context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, Rule, candidate.Location, messageArgs: new[] { candidate.MemberName });
        }

        foreach (var entry in messageUses.Where(x => !candidatesByType.Contains(x.Key)))
        {
            var use = entry.Value;
            var location = FirstLocation(use.Locations);
            foreach (var memberName in GpMessageContracts.DataMembers(use.Type)
                         .Where(x => GpIdentifierWords.ContainsSecretWord(x.Name) && CanCarrySecret(x.Type))
                         .Select(x => x.Name)
                         .Distinct(StringComparer.Ordinal))
            {
                context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, Rule, location, messageArgs: new[] { memberName });
            }
        }
    }

    private static bool CanCarrySecret(ITypeSymbol type) =>
        !type.IsValueType;

    private static Location FirstLocation(IEnumerable<Location> locations) =>
        locations
            .OrderBy(x => x.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(x => x.SourceSpan.Start)
            .First();

    private readonly record struct SecretCandidate(string TypeKey, Location Location, string MemberName);

    private sealed class MessageUse(INamedTypeSymbol type)
    {
        public INamedTypeSymbol Type { get; } = type;

        public ConcurrentBag<Location> Locations { get; } = new();
    }
}
