/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.Runtime.CompilerServices;

namespace SonarAnalyzer.CSharp.Rules;

// Shared semantic definition of a source contract. The compilation-wide scan is cached because several rules ask
// the same question and response/message uses can occur in a different syntax tree from the declaration.
internal sealed class GpSemanticContractDetector
{
    private static readonly ConditionalWeakTable<Compilation, Lazy<GpSemanticContractDetector>> Cache = new();

    private static readonly HashSet<string> MessagingMethods = new(StringComparer.Ordinal)
    {
        "Publish",
        "PublishBatch",
        "Send",
        "RespondAsync",
        "Publishes",
        "Sends",
    };

    private static readonly HashSet<string> ResponseFactoryMethods = new(StringComparer.Ordinal)
    {
        "Accepted",
        "AcceptedAtAction",
        "AcceptedAtRoute",
        "BadRequest",
        "Conflict",
        "Created",
        "CreatedAtAction",
        "CreatedAtRoute",
        "Json",
        "NotFound",
        "Ok",
        "StatusCode",
        "UnprocessableEntity",
    };

    private static readonly string[] MinimalApiMapMethods = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete"];

    private readonly Dictionary<string, INamedTypeSymbol> contracts;

    private GpSemanticContractDetector(Compilation compilation)
    {
        contracts = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var type in SourceTypes(compilation.Assembly.GlobalNamespace))
        {
            if (IsContractsNamespace(type.ContainingNamespace?.ToDisplayString() ?? string.Empty))
            {
                AddContract(type);
            }
        }

        foreach (var type in HttpContractTypes(compilation))
        {
            AddContract(type);
        }

        foreach (var tree in compilation.SyntaxTrees.Where(x => !x.IsGenerated(CSharpGeneratedCodeRecognizer.Instance)))
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                AddRuntimeContract(GpMessageContracts.MessagingPayloadType(model, invocation, MessagingMethods));
                AddRuntimeContract(HttpResponsePayloadType(model, invocation));
            }

            foreach (var handler in root.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
            {
                AddMinimalApiContracts(model, handler);
            }
        }
    }

    internal IEnumerable<INamedTypeSymbol> SourceContracts =>
        contracts.Values.Where(x => x.DeclaringSyntaxReferences.Length > 0);

    internal static GpSemanticContractDetector GetOrCreate(Compilation compilation) =>
        Cache.GetValue(
            compilation,
            x => new Lazy<GpSemanticContractDetector>(
                () => new GpSemanticContractDetector(x),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    internal bool IsContract(INamedTypeSymbol type) =>
        type is { DeclaringSyntaxReferences.Length: > 0 } && contracts.ContainsKey(TypeKey(type));

    private void AddMinimalApiContracts(SemanticModel model, AnonymousFunctionExpressionSyntax handler)
    {
        if (!GpMinimalApi.TryGetInlineHandler(handler.Body, model, MinimalApiMapMethods, out _, out _, out _, out _))
        {
            return;
        }

        if (handler.Body is ExpressionSyntax expression)
        {
            AddRuntimeContract(model.GetTypeInfo(expression).Type);
            return;
        }

        foreach (var returned in handler.Body.DescendantNodes(x =>
                     x.Kind() is not (SyntaxKindEx.LocalFunctionStatement or SyntaxKind.SimpleLambdaExpression or SyntaxKind.ParenthesizedLambdaExpression))
                 .OfType<ReturnStatementSyntax>()
                 .Select(x => x.Expression)
                 .WhereNotNull())
        {
            AddRuntimeContract(model.GetTypeInfo(returned).Type);
        }
    }

    private void AddRuntimeContract(ITypeSymbol type)
    {
        if (ContractPayloadType(type) is INamedTypeSymbol contract && !IsFrameworkType(contract))
        {
            AddContract(contract);
        }
    }

    private void AddContract(INamedTypeSymbol type) =>
        contracts[TypeKey(type)] = type.OriginalDefinition;

    private static IEnumerable<INamedTypeSymbol> HttpContractTypes(Compilation compilation)
    {
        foreach (var controller in ControllerTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var action in controller.GetMembers().OfType<IMethodSymbol>().Where(x => x.IsControllerActionMethod()))
            {
                if (ContractPayloadType(action.ReturnType) is INamedTypeSymbol contract)
                {
                    yield return contract;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> ControllerTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers().Where(x => x.IsControllerType()))
        {
            yield return type;
        }

        foreach (var nested in root.GetNamespaceMembers())
        {
            foreach (var type in ControllerTypes(nested))
            {
                yield return type;
            }
        }
    }

    private static ITypeSymbol HttpResponsePayloadType(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !ResponseFactoryMethods.Contains(method.Name)
            || !IsHttpResponseFactory(model, invocation))
        {
            return null;
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        var payload = new[] { "value", "data", "error" }
            .SelectMany(x => lookup.TryGetSyntax(x, out var arguments)
                ? arguments.AsEnumerable()
                : Enumerable.Empty<SyntaxNode>())
            .OfType<ExpressionSyntax>()
            .FirstOrDefault();
        return payload is null ? null : model.GetTypeInfo(payload).Type;
    }

    private static bool IsHttpResponseFactory(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (GpMvcResults.TryGetResultMethod(model, invocation, out _))
        {
            return model.GetEnclosingSymbol(invocation.SpanStart) is IMethodSymbol enclosing && enclosing.IsControllerActionMethod();
        }

        return GpMinimalApi.TryGetResultMethod(model, invocation, out _)
               && GpMinimalApi.TryGetInlineHandler(invocation, model, MinimalApiMapMethods, out _, out _, out _, out _);
    }

    private static ITypeSymbol ContractPayloadType(ITypeSymbol type)
    {
        var current = type;
        while (current is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named && IsResponseWrapper(named))
        {
            current = named.TypeArguments[0];
        }

        if (current is IArrayTypeSymbol array)
        {
            return array.ElementType;
        }

        return current is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } collection
               && GpCollectionEndpointHelper.IsCollectionLike(collection)
            ? collection.TypeArguments[0]
            : current;
    }

    private static bool IsResponseWrapper(INamedTypeSymbol type) =>
        type.ConstructedFrom.IsAny(KnownType.System_Threading_Tasks_Task_T, KnownType.System_Threading_Tasks_ValueTask_TResult)
        || (type.Name == "ActionResult" && type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc");

    private static bool IsFrameworkType(ITypeSymbol type) =>
        (type.ContainingNamespace?.ToDisplayString() ?? string.Empty) is var containing
        && (containing == "System"
            || containing.StartsWith("System.", StringComparison.Ordinal)
            || containing.StartsWith("Microsoft.", StringComparison.Ordinal));

    private static bool IsContractsNamespace(string containingNamespace) =>
        containingNamespace == "Contracts" || containingNamespace.EndsWith(".Contracts", StringComparison.Ordinal);

    private static string TypeKey(INamedTypeSymbol type) =>
        $"{type.ContainingAssembly?.Identity}|{type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in SourceTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var nestedNamespace in root.GetNamespaceMembers())
        {
            foreach (var type in SourceTypes(nestedNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamedTypeSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in SourceTypes(type))
            {
                yield return nested;
            }
        }
    }
}
