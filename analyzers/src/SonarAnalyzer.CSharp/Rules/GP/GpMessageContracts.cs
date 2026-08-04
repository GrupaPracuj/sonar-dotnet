namespace SonarAnalyzer.CSharp.Rules;

// Shared shapes for the message-contract rules: what counts as a consumer, and which type a publish call publishes.
internal static class GpMessageContracts
{
    private static readonly HashSet<string> ContractNameSuffixes = new(StringComparer.Ordinal)
    {
        "Dto", "Request", "Response", "Contract", "Event", "Command", "Message"
    };

    // Registration (AppConfig.Publishes<T>) and the publish call itself, so a contract is found whichever way the
    // service declares it. Sending is excluded on purpose: a command has no occurrence time to state.
    private static readonly HashSet<string> PublishMethods = new(StringComparer.Ordinal)
    {
        "Publishes",
        "Publish",
        "PublishBatch",
    };

    internal static bool IsConsumeMethod(IMethodSymbol method) =>
        method is { Name: "Consume" }
        && method.ContainingType.AllInterfaces.Any(x => x is { Name: "IConsumer", IsGenericType: true });

    internal static bool IsInsideConsumer(SemanticModel model, SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } methodDeclaration
        && model.GetDeclaredSymbol(methodDeclaration) is { } method
        && IsConsumeMethod(method);

    internal static bool HasContractName(string typeName) =>
        ContractNameSuffixes.Any(x => typeName.EndsWith(x, StringComparison.Ordinal));

    internal static bool IsContractMember(MemberDeclarationSyntax member) =>
        member.Parent is TypeDeclarationSyntax { Identifier.ValueText: var typeName } && HasContractName(typeName);

    // The type an event-publishing call publishes, taken from the generic argument or the first argument.
    internal static INamedTypeSymbol PublishedType(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !PublishMethods.Contains(method.Name))
        {
            return null;
        }

        if (method.TypeArguments.FirstOrDefault() is INamedTypeSymbol typeArgument)
        {
            return typeArgument;
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } firstArgument
               && model.GetTypeInfo(firstArgument).Type is INamedTypeSymbol argumentType
               && argumentType.SpecialType != SpecialType.System_Object
            ? argumentType
            : null;
    }

    // Positional records declare their members in the parameter list, so both shapes have to be inspected.
    internal static IEnumerable<(string Name, ITypeSymbol Type)> DataMembers(INamedTypeSymbol type) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(x => x is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsIndexer: false })
            .Select(x => (x.Name, x.Type));
}
