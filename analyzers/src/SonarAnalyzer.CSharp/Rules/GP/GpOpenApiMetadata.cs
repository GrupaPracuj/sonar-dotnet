/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

internal static class GpOpenApiMetadata
{
    private static readonly Dictionary<string, int> FactoryStatusCodes = new(StringComparer.Ordinal)
    {
        ["Accepted"] = 202,
        ["AcceptedAtAction"] = 202,
        ["AcceptedAtRoute"] = 202,
        ["BadRequest"] = 400,
        ["Challenge"] = 401,
        ["Conflict"] = 409,
        ["Created"] = 201,
        ["CreatedAtAction"] = 201,
        ["CreatedAtRoute"] = 201,
        ["Forbid"] = 403,
        ["LocalRedirect"] = 302,
        ["LocalRedirectPermanent"] = 301,
        ["LocalRedirectPermanentPreserveMethod"] = 308,
        ["LocalRedirectPreserveMethod"] = 307,
        ["NoContent"] = 204,
        ["NotFound"] = 404,
        ["Ok"] = 200,
        ["Redirect"] = 302,
        ["RedirectPermanent"] = 301,
        ["RedirectPermanentPreserveMethod"] = 308,
        ["RedirectPreserveMethod"] = 307,
        ["Unauthorized"] = 401,
        ["UnprocessableEntity"] = 422,
    };

    internal static bool IsOpenApiAction(IMethodSymbol method) =>
        method.IsControllerActionMethod && method.ContainingType.IsCoreApiController;

    internal static bool IsIgnored(IMethodSymbol method) =>
        method.AttributesWithInherited.Concat(method.ContainingType.AttributesWithInherited)
            .Any(x => x.AttributeClass?.Name == "ApiExplorerSettingsAttribute"
                      && x.NamedArguments.Any(y => y.Key == "IgnoreApi" && y.Value.Value is true));

    internal static bool UsesApiConvention(IMethodSymbol method) =>
        method.AttributesWithInherited.Any(x => x.AttributeClass?.Name == "ApiConventionMethodAttribute")
        || method.ContainingType.AttributesWithInherited.Any(x => x.AttributeClass?.Name == "ApiConventionTypeAttribute")
        || method.ContainingAssembly.GetAttributes().Any(x => x.AttributeClass?.Name == "ApiConventionTypeAttribute");

    internal static IEnumerable<AttributeData> ResponseAttributes(IMethodSymbol method) =>
        method.AttributesWithInherited
            .Concat(method.ContainingType.AttributesWithInherited)
            .Where(IsResponseAttribute);

    internal static bool IsResponseAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } attributeType
        && (attributeType.DerivesFrom(KnownType.Microsoft_AspNetCore_Mvc_ProducesResponseTypeAttribute)
            || attributeType.DerivesFrom(KnownType.Microsoft_AspNetCore_Mvc_ProducesResponseTypeAttribute_T)
            || attributeType.Name == "SwaggerResponseAttribute");

    internal static int? ResponseStatusCode(AttributeData attribute) =>
        attribute.ConstructorArguments
            .Where(x => x.Type?.SpecialType == SpecialType.System_Int32)
            .Select(x => x.Value)
            .OfType<int>()
            .Cast<int?>()
            .FirstOrDefault()
        ?? attribute.NamedArguments
            .Where(x => x.Key == "StatusCode")
            .Select(x => x.Value.Value)
            .OfType<int>()
            .Cast<int?>()
            .FirstOrDefault();

    internal static bool HasConcreteProducedType(IMethodSymbol method) =>
        method.AttributesWithInherited
            .Concat(method.ContainingType.AttributesWithInherited)
            .Any(x => x.AttributeClass is { } attributeType
                      && HasConcreteResponseType(x)
                      && attributeType.Name == "ProducesAttribute");

    internal static bool HasConcreteResponseTypeForStatus(IMethodSymbol method, int statusCode) =>
        ResponseAttributes(method).Any(x => ResponseStatusCode(x) == statusCode && HasConcreteResponseType(x));

    internal static bool HasConcreteResponseType(AttributeData attribute) =>
        ResponseType(attribute) is { } type && IsConcreteType(type);

    internal static ITypeSymbol ResponseType(AttributeData attribute) =>
        attribute.AttributeClass is { IsGenericType: true, TypeArguments.Length: > 0 } attributeType
            ? attributeType.TypeArguments.FirstOrDefault(IsConcreteType)
            : attribute.ConstructorArguments
                .Where(x => x.Type.Is(KnownType.System_Type))
                .Select(x => x.Value)
                .OfType<ITypeSymbol>()
                .FirstOrDefault(IsConcreteType)
              ?? attribute.NamedArguments
                  .Where(x => x.Key == "Type")
                  .Select(x => x.Value.Value)
                  .OfType<ITypeSymbol>()
                  .FirstOrDefault(IsConcreteType);

    internal static int? ResponseStatusCode(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (!TryGetResponseMethod(model, invocation, out var method))
        {
            return null;
        }

        if (FactoryStatusCodes.TryGetValue(method.Name, out var known))
        {
            return known;
        }

        return method.Name == "StatusCode"
               && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } expression
               && model.GetConstantValue(expression) is { HasValue: true, Value: int explicitCode }
            ? explicitCode
            : null;
    }

    internal static bool HasPayload(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (!TryGetResponseMethod(model, invocation, out var method))
        {
            return false;
        }

        var lookup = new CSharpMethodParameterLookup(invocation, method);
        return invocation.ArgumentList.Arguments.Any(x =>
            lookup.TryGetSymbol(x, out var parameter) && parameter.Name is "value" or "data" or "error");
    }

    internal static bool TryGetResponseMethod(SemanticModel model, InvocationExpressionSyntax invocation, out IMethodSymbol method) =>
        GpMvcResults.TryGetResultMethod(model, invocation, out method)
        || GpMinimalApi.TryGetResultMethod(model, invocation, out method);

    internal static IEnumerable<InvocationExpressionSyntax> ReturnedInvocations(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody?.Expression is { } expressionBody)
        {
            foreach (var invocation in ResponseInvocations(expressionBody))
            {
                yield return invocation;
            }
        }

        if (method.Body is null)
        {
            yield break;
        }

        foreach (var expression in method.Body.DescendantNodes(x =>
                     x.Kind() is not (SyntaxKindEx.LocalFunctionStatement
                         or SyntaxKind.SimpleLambdaExpression
                         or SyntaxKind.ParenthesizedLambdaExpression
                         or SyntaxKind.AnonymousMethodExpression))
                 .OfType<ReturnStatementSyntax>()
                 .Select(x => x.Expression)
                 .WhereNotNull())
        {
            foreach (var invocation in ResponseInvocations(expression))
            {
                yield return invocation;
            }
        }
    }

    internal static IEnumerable<InvocationExpressionSyntax> ReturnedInvocations(AnonymousFunctionExpressionSyntax handler)
    {
        if (handler.Body is ExpressionSyntax expressionBody)
        {
            return ResponseInvocations(expressionBody);
        }

        return handler.Body.DescendantNodes(x =>
                x.Kind() is not (SyntaxKindEx.LocalFunctionStatement
                    or SyntaxKind.SimpleLambdaExpression
                    or SyntaxKind.ParenthesizedLambdaExpression
                    or SyntaxKind.AnonymousMethodExpression))
            .OfType<ReturnStatementSyntax>()
            .Select(x => x.Expression)
            .WhereNotNull()
            .SelectMany(ResponseInvocations);
    }

    private static IEnumerable<InvocationExpressionSyntax> ResponseInvocations(ExpressionSyntax expression)
    {
        expression = expression.RemoveParentheses() as ExpressionSyntax ?? expression;
        if (SwitchExpressionSyntaxWrapper.IsInstance(expression))
        {
            foreach (var invocation in ((SwitchExpressionSyntaxWrapper)expression).Arms.SelectMany(x => ResponseInvocations(x.Expression)))
            {
                yield return invocation;
            }
            yield break;
        }

        switch (expression)
        {
            case InvocationExpressionSyntax invocation:
                yield return invocation;
                break;
            case ConditionalExpressionSyntax conditional:
                foreach (var invocation in ResponseInvocations(conditional.WhenTrue).Concat(ResponseInvocations(conditional.WhenFalse)))
                {
                    yield return invocation;
                }
                break;
        }
    }

    private static bool IsConcreteType(ITypeSymbol type) =>
        type.SpecialType != SpecialType.System_Void;
}
