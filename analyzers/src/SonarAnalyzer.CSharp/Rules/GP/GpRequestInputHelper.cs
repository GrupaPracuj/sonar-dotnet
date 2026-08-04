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
            || !enclosing.IsControllerActionMethod())
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
}
