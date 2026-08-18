/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

internal enum GpMinimalApiResultFactory
{
    Results,
    TypedResults,
}

internal static class GpMinimalApi
{
    private const string ResultsType = "Microsoft.AspNetCore.Http.Results";
    private const string TypedResultsType = "Microsoft.AspNetCore.Http.TypedResults";
    private const string HttpMethodsType = "Microsoft.AspNetCore.Http.HttpMethods";

    private static readonly HashSet<string> MapExtensionTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions",
        "Microsoft.AspNetCore.Builder.RouteHandlerBuilderExtensions",
    };

    internal static bool TryGetInlineHandler(SyntaxNode nodeInHandler,
                                             SemanticModel model,
                                             string mapMethodName,
                                             out AnonymousFunctionExpressionSyntax handler,
                                             out InvocationExpressionSyntax mapInvocation,
                                             out IMethodSymbol mapMethod,
                                             out string routeTemplate) =>
        TryGetInlineHandler(nodeInHandler, model, x => x == mapMethodName, out handler, out mapInvocation, out mapMethod, out routeTemplate);

    internal static bool TryGetInlineHandler(SyntaxNode nodeInHandler,
                                             SemanticModel model,
                                             IReadOnlyCollection<string> mapMethodNames,
                                             out AnonymousFunctionExpressionSyntax handler,
                                             out InvocationExpressionSyntax mapInvocation,
                                             out IMethodSymbol mapMethod,
                                             out string routeTemplate) =>
        TryGetInlineHandler(nodeInHandler, model, mapMethodNames.Contains, out handler, out mapInvocation, out mapMethod, out routeTemplate);

    private static bool TryGetInlineHandler(SyntaxNode nodeInHandler,
                                            SemanticModel model,
                                            Func<string, bool> acceptsMapMethod,
                                            out AnonymousFunctionExpressionSyntax handler,
                                            out InvocationExpressionSyntax mapInvocation,
                                            out IMethodSymbol mapMethod,
                                            out string routeTemplate)
    {
        var candidateHandler = nodeInHandler.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        handler = candidateHandler;
        mapInvocation = null;
        mapMethod = null;
        routeTemplate = null;
        if (candidateHandler is null
            || nodeInHandler.Ancestors().TakeWhile(x => x != candidateHandler).Any(x => x.Kind() == SyntaxKindEx.LocalFunctionStatement)
            || candidateHandler.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax candidateMapInvocation } } handlerArgument
            || model.GetSymbolInfo(candidateMapInvocation).Symbol is not IMethodSymbol { Name: var methodName } candidateMapMethod
            || !acceptsMapMethod(methodName)
            || !MapExtensionTypes.Contains((candidateMapMethod.ReducedFrom ?? candidateMapMethod).ContainingType?.ToDisplayString() ?? string.Empty))
        {
            return false;
        }

        var lookup = new CSharpMethodParameterLookup(candidateMapInvocation, candidateMapMethod);
        if (!lookup.TryGetSymbol(handlerArgument, out var handlerParameter)
            || handlerParameter.Name is not ("handler" or "requestDelegate"))
        {
            return false;
        }

        routeTemplate = lookup.GetAllArgumentParameterMappings()
            .Where(x => x.Symbol.Name is "pattern" or "routePattern")
            .Select(x => model.GetConstantValue(x.Node.Expression))
            .Where(x => x is { HasValue: true, Value: string })
            .Select(x => (string)x.Value)
            .FirstOrDefault();
        mapInvocation = candidateMapInvocation;
        mapMethod = candidateMapMethod;
        return true;
    }

    internal static ImmutableArray<string> HttpMethods(InvocationExpressionSyntax mapInvocation, IMethodSymbol mapMethod, SemanticModel model)
    {
        if (mapMethod.Name != "MapMethods")
        {
            return mapMethod.Name.StartsWith("Map", StringComparison.Ordinal)
                ? ImmutableArray.Create(mapMethod.Name.Substring("Map".Length).ToUpperInvariant())
                : ImmutableArray<string>.Empty;
        }

        var lookup = new CSharpMethodParameterLookup(mapInvocation, mapMethod);
        if (!lookup.TryGetSyntax("httpMethods", out var arguments) || arguments.Length != 1)
        {
            return ImmutableArray<string>.Empty;
        }

        if (DirectMethodListElements(arguments[0]).ToArray() is not { Length: > 0 } elements)
        {
            return ImmutableArray<string>.Empty;
        }

        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in elements)
        {
            if (IsDirectMethodElement(expression)
                && KnownHttpMethod(expression, model) is { } method
                && seen.Add(method))
            {
                result.Add(method);
            }
        }
        return result.ToImmutable();
    }

    private static IEnumerable<ExpressionSyntax> DirectMethodListElements(SyntaxNode argument) =>
        argument switch
        {
            ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            InitializerExpressionSyntax initializer => initializer.Expressions,
            _ => Enumerable.Empty<ExpressionSyntax>(),
        };

    private static bool IsDirectMethodElement(ExpressionSyntax expression) =>
        expression.RemoveParentheses() is LiteralExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax;

    internal static bool HandlerReturnsCollection(AnonymousFunctionExpressionSyntax handler, SemanticModel model) =>
        HandlerInvocations(handler).Any(x =>
            TryGetResultMethod(model, x, out var method)
            && method.Name == "Ok"
            && x.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } value
            && model.GetTypeInfo(value).Type is { } valueType
            && GpCollectionEndpointHelper.IsCollectionLike(valueType));

    internal static bool TryGetResultMethod(SemanticModel model, InvocationExpressionSyntax invocation, out IMethodSymbol method) =>
        TryGetResultMethod(model, invocation, out method, out _);

    internal static bool TryGetResultMethod(SemanticModel model,
                                            InvocationExpressionSyntax invocation,
                                            out IMethodSymbol method,
                                            out GpMinimalApiResultFactory factory)
    {
        method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        factory = method?.ContainingType?.ToDisplayString() switch
        {
            ResultsType => GpMinimalApiResultFactory.Results,
            TypedResultsType => GpMinimalApiResultFactory.TypedResults,
            _ => default,
        };
        return method?.ContainingType?.ToDisplayString() is ResultsType or TypedResultsType;
    }

    private static string KnownHttpMethod(ExpressionSyntax expression, SemanticModel model)
    {
        if (model.GetConstantValue(expression) is { HasValue: true, Value: string value })
        {
            return value.ToUpperInvariant();
        }

        var symbol = model.GetSymbolInfo(expression).Symbol;
        return symbol?.ContainingType?.ToDisplayString() == HttpMethodsType && symbol.Name is "Get" or "Head"
            ? symbol.Name.ToUpperInvariant()
            : null;
    }

    private static IEnumerable<InvocationExpressionSyntax> HandlerInvocations(AnonymousFunctionExpressionSyntax handler)
    {
        var body = handler switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Body,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            _ => null,
        };

        return body is null
            ? Enumerable.Empty<InvocationExpressionSyntax>()
            : body.DescendantNodesAndSelf(x => x.Kind() != SyntaxKindEx.LocalFunctionStatement && x is not AnonymousFunctionExpressionSyntax)
                .OfType<InvocationExpressionSyntax>();
    }
}
