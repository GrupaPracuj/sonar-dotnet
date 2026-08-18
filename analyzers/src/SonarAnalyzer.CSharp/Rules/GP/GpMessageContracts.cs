/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// Shared shapes for the message-contract rules: what counts as a consumer, and which type a publish call publishes.
internal static class GpMessageContracts
{
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

    // The type an event-publishing call publishes, taken from the final generic argument or the first argument.
    // MassTransit state-machine overloads use Publish<TSaga, TData, TMessage>, so TMessage is not always first.
    internal static INamedTypeSymbol PublishedType(SemanticModel model, InvocationExpressionSyntax invocation)
        => MessagingPayloadType(model, invocation, PublishMethods);

    internal static INamedTypeSymbol MessagingPayloadType(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        HashSet<string> supportedMethods)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !supportedMethods.Contains(method.Name)
            || !IsMessagingMethod(method))
        {
            return null;
        }

        if (method.TypeArguments.LastOrDefault() is INamedTypeSymbol typeArgument)
        {
            return typeArgument;
        }

        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } firstArgument
            && model.GetTypeInfo(firstArgument).Type is INamedTypeSymbol argumentType
            && argumentType.SpecialType != SpecialType.System_Object)
        {
            return argumentType;
        }

        return invocation.ArgumentList.Arguments
            .Select(x => x.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .Select(x => model.GetTypeInfo(x.Type).Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();
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

    internal static string TypeKey(INamedTypeSymbol type) =>
        $"{type.ContainingAssembly?.Identity}|{type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";

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
