using System.Collections.Concurrent;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotCarrySecrets : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0044";

    private const string MessageFormat = "'{0}' looks like a secret - a message contract is persisted on the broker and readable by every subscriber.";
    private const string DefaultContractAssemblyNames = "Contracts";

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

    [RuleParameter("contractAssemblyNames", PropertyType.String, "Comma-separated names or suffixes identifying contract assemblies", DefaultContractAssemblyNames)]
    public string ContractAssemblyNames { get; set; } = DefaultContractAssemblyNames;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var candidates = new ConcurrentDictionary<string, SecretCandidate>(StringComparer.Ordinal);
            var messageTypes = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var contractAssembly = IsContractAssembly(start.Compilation, ContractAssemblyNames);
            start.RegisterNodeAction(c => AnalyzeProperty(c, candidates), SyntaxKind.PropertyDeclaration);
            start.RegisterNodeAction(c => AnalyzeRecordParameters(c, candidates), SyntaxKindEx.RecordDeclaration);
            start.RegisterNodeAction(c => AnalyzeMessagingUse(c, messageTypes), SyntaxKind.InvocationExpression);
            start.RegisterCompilationEndAction(c => Report(c, candidates.Values, messageTypes, contractAssembly));
        });

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, SecretCandidate> candidates)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (GpIdentifierWords.ContainsSecretWord(declaration.Identifier.ValueText)
            && context.Model.GetDeclaredSymbol(declaration) is { ContainingType: { } containingType })
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
        foreach (var parameter in parameterList.Parameters.Where(x => GpIdentifierWords.ContainsSecretWord(x.Identifier.ValueText)))
        {
            AddCandidate(candidates, containingType, parameter.Identifier);
        }
    }

    private static ParameterListSyntax ParameterList(TypeDeclarationSyntax declaration) =>
        RecordDeclarationSyntaxWrapper.IsInstance(declaration)
            ? ((RecordDeclarationSyntaxWrapper)declaration).ParameterList
            : null;

    private static void AnalyzeMessagingUse(SonarSyntaxNodeReportingContext context, ConcurrentDictionary<string, byte> messageTypes)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !MessagingMethods.Contains(method.Name)
            || !GpMessageContracts.IsMessagingMethod(method)
            || MessageType(context.Model, invocation, method) is not INamedTypeSymbol messageType)
        {
            return;
        }

        messageTypes.TryAdd(TypeKey(messageType), 0);
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
            var candidate = new SecretCandidate(TypeKey(containingType), identifier.GetLocation(), identifier.ValueText);
            candidates.TryAdd($"{candidate.TypeKey}|{identifier.SyntaxTree.GetHashCode()}|{candidate.Location.SourceSpan.Start}", candidate);
        }
    }

    private static void Report(SonarCompilationReportingContext context,
                               IEnumerable<SecretCandidate> candidates,
                               ConcurrentDictionary<string, byte> messageTypes,
                               bool contractAssembly)
    {
        foreach (var candidate in candidates.Where(x => contractAssembly || messageTypes.ContainsKey(x.TypeKey)))
        {
            context.ReportIssue(CSharpGeneratedCodeRecognizer.Instance, Rule, candidate.Location, messageArgs: new[] { candidate.MemberName });
        }
    }

    private static bool IsContractAssembly(Compilation compilation, string configuredNames)
    {
        var assemblyName = compilation.AssemblyName ?? string.Empty;
        return GpEntityTypes.SplitParameter(configuredNames).Any(x => GpAssemblyNames.Matches(assemblyName, x));
    }

    private static string TypeKey(INamedTypeSymbol type) =>
        $"{type.ContainingAssembly?.Identity}|{type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";

    private readonly record struct SecretCandidate(string TypeKey, Location Location, string MemberName);
}
