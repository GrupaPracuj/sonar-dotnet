/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

internal static class GpCollectionEndpointHelper
{
    private static readonly ImmutableArray<KnownType> CollectionTypes = ImmutableArray.Create(
        KnownType.System_Collections_Generic_IEnumerable_T,
        KnownType.System_Collections_Generic_IReadOnlyCollection_T,
        KnownType.System_Collections_Generic_IReadOnlyList_T,
        KnownType.System_Collections_Generic_ICollection_T,
        KnownType.System_Collections_Generic_IList_T);

    internal static bool IsHttpGetMethod(IMethodSymbol method) =>
        method.IsControllerActionMethod()
        && method.GetAttributes().Select(x => x.AttributeClass?.Name).Any(x => x is "HttpGet" or "HttpGetAttribute");

    /// <summary>
    /// True when the method's declared return type is a collection (including Task/ValueTask- and ActionResult{T}-wrapped),
    /// or, for a plain IActionResult/ActionResult/Task{IActionResult} signature, when some other return statement in the
    /// same method body responds with Ok(...) of a collection-typed value.
    /// </summary>
    internal static bool ReturnsCollection(IMethodSymbol method, SemanticModel model, SyntaxNode nodeInMethod)
    {
        if (IsCollectionLike(UnwrapDeclaredReturnType(method.ReturnType)))
        {
            return true;
        }

        if (!IsPlainActionResultReturnType(method))
        {
            return false;
        }

        return method.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .Where(x => x.SyntaxTree == nodeInMethod.SyntaxTree)
            .SelectMany(x => x.DescendantNodes(n =>
                n.Kind() is not (SyntaxKindEx.LocalFunctionStatement or SyntaxKind.SimpleLambdaExpression or SyntaxKind.ParenthesizedLambdaExpression)))
            .OfType<ReturnStatementSyntax>()
            .Any(x => IsCollectionOkReturn(model, x));
    }

    private static ITypeSymbol UnwrapDeclaredReturnType(ITypeSymbol type)
    {
        var current = UnwrapAsyncWrapper(type);
        return current is INamedTypeSymbol { IsGenericType: true } namedType && IsActionResultOfT(namedType)
            ? namedType.TypeArguments[0]
            : current;
    }

    private static bool IsPlainActionResultReturnType(IMethodSymbol method)
    {
        var returnType = UnwrapAsyncWrapper(method.ReturnType);
        return returnType.Is(KnownType.Microsoft_AspNetCore_Mvc_IActionResult)
               || (returnType is INamedTypeSymbol { IsGenericType: false, Name: "ActionResult" } namedType && IsMvcNamespace(namedType));
    }

    private static ITypeSymbol UnwrapAsyncWrapper(ITypeSymbol type) =>
        type.IsAny(KnownType.System_Threading_Tasks_Task_T, KnownType.System_Threading_Tasks_ValueTask_TResult) && type is INamedTypeSymbol namedType
            ? namedType.TypeArguments[0]
            : type;

    private static bool IsActionResultOfT(INamedTypeSymbol type) =>
        type.Name == "ActionResult" && IsMvcNamespace(type);

    private static bool IsMvcNamespace(ITypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Mvc";

    private static bool IsCollectionOkReturn(SemanticModel model, ReturnStatementSyntax returnStatement)
    {
        if (returnStatement.Expression is not InvocationExpressionSyntax invocation
            || !GpMvcResults.IsResponseFactory(model, invocation, "Ok"))
        {
            return false;
        }

        if (invocation.Expression is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } genericName
            && model.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]).Type is { } explicitType)
        {
            return IsCollectionLike(explicitType);
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } argumentExpression
               && model.GetTypeInfo(argumentExpression).Type is { } argumentType
               && IsCollectionLike(argumentType);
    }

    internal static bool IsCollectionLike(ITypeSymbol type) =>
        type.SpecialType != SpecialType.System_String
        && (type is IArrayTypeSymbol || type.IsAny(CollectionTypes) || type.ImplementsAny(CollectionTypes));
}
