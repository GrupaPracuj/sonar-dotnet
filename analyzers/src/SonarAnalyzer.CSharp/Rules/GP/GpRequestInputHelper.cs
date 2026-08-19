/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// Identifies values that arrive straight from the current HTTP request: the parameters of the enclosing controller
// action, which the model binder fills from the route, the query string or the body.
// This is deliberately shallow - a value that travels through a local variable, a field or another method is not
// tracked - so it complements, but does not replace, real taint analysis.
internal static class GpRequestInputHelper
{
    // Returns the name of the enclosing action's parameter that the expression reads, or null when it reads none.
    internal static string ActionParameterName(SemanticModel model, SyntaxNode expression)
    {
        if (model.GetEnclosingSymbol(expression.SpanStart) is not IMethodSymbol enclosing
            || !enclosing.IsControllerActionMethod)
        {
            return null;
        }

        var parameterNames = enclosing.Parameters.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (parameterNames.Contains(identifier.Identifier.ValueText)
                && model.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter)
            {
                return parameter.Name;
            }
        }

        return null;
    }

    // Returns the name of an inline Minimal API handler parameter read by the expression.
    internal static string InlineHandlerParameterName(SemanticModel model,
                                                      SyntaxNode expression,
                                                      AnonymousFunctionExpressionSyntax handler)
    {
        var parameters = handler switch
        {
            SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
            AnonymousMethodExpressionSyntax { ParameterList: { } parameterList } => parameterList.Parameters,
            _ => Enumerable.Empty<ParameterSyntax>(),
        };
        var parameterSymbols = parameters
            .Select(x => model.GetDeclaredSymbol(x))
            .OfType<IParameterSymbol>()
            .ToArray();

        return expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(x => model.GetSymbolInfo(x))
            .Select(x => x.Symbol)
            .OfType<IParameterSymbol>()
            .FirstOrDefault(x => parameterSymbols.Any(y => y.Equals(x)))
            ?.Name;
    }
}
