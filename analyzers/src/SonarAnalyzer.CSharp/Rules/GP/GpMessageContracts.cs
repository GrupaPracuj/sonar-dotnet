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

    private static readonly Dictionary<string, string> ShapelessTypes = new(StringComparer.Ordinal)
    {
        ["System.Dynamic.ExpandoObject"] = "an ExpandoObject",
        ["System.Text.Json.JsonElement"] = "a JsonElement",
        ["System.Text.Json.JsonDocument"] = "a JsonDocument",
        ["Newtonsoft.Json.Linq.JObject"] = "a JObject",
    };

    // Publish/Send/Consume only carry real messaging semantics when they come from GP.Juno or MassTransit - the same
    // namespace-based test CommitAndPublishShouldNotBeADualWrite (GP0048) and PublishedMessageShouldHaveExplicitContract
    // (GP0055) already rely on, so a same-named member on an unrelated type (MediatR, Prism, Rx, a hand-rolled bus,
    // AppConfig.Publishes<T> for some other config surface) is never mistaken for one of these APIs.
    internal static bool IsMessagingType(ITypeSymbol type) =>
        type?.ContainingNamespace?.ToDisplayString() is { } containingNamespace
        && (IsWithinNamespace(containingNamespace, "GP.Juno") || IsWithinNamespace(containingNamespace, "MassTransit"));

    private static bool IsWithinNamespace(string containingNamespace, string root) =>
        containingNamespace == root || containingNamespace.StartsWith(root + ".", StringComparison.Ordinal);

    // True when the method itself is declared by GP.Juno/MassTransit, when it is inherited through an interface a
    // wrapper type implements (a class implementing IPublisher or IConsumer<T> directly), or when it is a reduced
    // extension method whose receiver is one of these types (AppConfig.Publishes<T>() reduces to an extension on
    // GP.Juno's AppConfig, so the receiver - not the static class hosting the extension - is what has to be checked).
    internal static bool IsMessagingMethod(IMethodSymbol method) =>
        method is not null
        && (IsMessagingType(method.ContainingType)
            || (method.ContainingType?.AllInterfaces.Any(IsMessagingType) ?? false)
            || (method.IsExtensionMethod && IsMessagingType(method.ReceiverType))
            || (method.IsExtensionMethod && (method.ReceiverType?.AllInterfaces.Any(IsMessagingType) ?? false)));

    internal static bool IsConsumerInterface(INamedTypeSymbol @interface) =>
        @interface is { Name: "IConsumer", IsGenericType: true, TypeArguments.Length: 1 } && IsMessagingType(@interface);

    internal static bool IsConsumeMethod(IMethodSymbol method) =>
        method is not null
        && method.ContainingType.AllInterfaces
            .Where(IsConsumerInterface)
            .SelectMany(x => x.GetMembers("Consume").OfType<IMethodSymbol>())
            .Any(x => method.Equals(method.ContainingType.FindImplementationForInterfaceMember(x)));

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
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !PublishMethods.Contains(method.Name)
            || !IsMessagingMethod(method))
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

    internal static string DescribeShapelessType(ITypeSymbol type)
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

        return type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 2 } dictionary
               && GpCollectionEndpointHelper.IsCollectionLike(dictionary)
               && dictionary.TypeArguments[0].SpecialType == SpecialType.System_String
               && dictionary.TypeArguments[1].SpecialType == SpecialType.System_Object
            ? "a loose dictionary"
            : null;
    }

    // The public properties of the type and of everything it inherits - which covers a positional record too, since
    // the compiler turns each of its parameters into exactly such a property.
    //
    // Grouped by name because an inherited property that a derived type overrides or hides appears once per level of
    // the hierarchy, while the serialized message has one member for it; counting it twice would overstate the shape.
    internal static IEnumerable<(string Name, ITypeSymbol Type)> DataMembers(INamedTypeSymbol type) =>
        BaseTypesAndSelf(type)
            .SelectMany(x => x.GetMembers().OfType<IPropertySymbol>())
            .Where(x => x is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsIndexer: false })
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.First())
            .Select(x => (x.Name, x.Type));

    private static IEnumerable<INamedTypeSymbol> BaseTypesAndSelf(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
