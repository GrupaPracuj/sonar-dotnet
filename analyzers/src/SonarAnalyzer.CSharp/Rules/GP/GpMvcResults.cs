/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// "Ok", "NoContent", "NotFound" and "StatusCode" are plausible names for a controller's own helper or for a member of
// some result-building type, so the response-shape rules resolve them to a real response factory rather than trusting
// the spelling at the call site.
//
// Two surfaces count as one: MVC's ControllerBase/Controller helpers, and the Minimal API
// Results/TypedResults factories - an MVC action can return an IResult as well, so a rule about the shape of a
// response has to recognise both wherever the response is produced.
internal static class GpMvcResults
{
    internal static bool TryGetResultMethod(SemanticModel model, InvocationExpressionSyntax invocation, out IMethodSymbol method)
    {
        method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return method?.ContainingType?.ToDisplayString() is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller";
    }

    internal static bool IsResponseFactory(SemanticModel model, InvocationExpressionSyntax invocation, string name) =>
        ResponseFactoryMethod(model, invocation)?.Name == name;

    // Either the dedicated factory for that status - NoContent(), NotFound() - or an explicit StatusCode(code).
    internal static bool IsStatusResponse(SemanticModel model, InvocationExpressionSyntax invocation, string factoryName, int statusCode)
    {
        if (ResponseFactoryMethod(model, invocation) is not { } method)
        {
            return false;
        }

        if (method.Name == factoryName)
        {
            return true;
        }

        return method.Name == "StatusCode"
               && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } codeExpression
               && model.GetConstantValue(codeExpression) is { HasValue: true, Value: int actual }
               && actual == statusCode;
    }

    private static IMethodSymbol ResponseFactoryMethod(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (TryGetResultMethod(model, invocation, out var mvcMethod))
        {
            return mvcMethod;
        }

        return GpMinimalApi.TryGetResultMethod(model, invocation, out var minimalApiMethod) ? minimalApiMethod : null;
    }
}
