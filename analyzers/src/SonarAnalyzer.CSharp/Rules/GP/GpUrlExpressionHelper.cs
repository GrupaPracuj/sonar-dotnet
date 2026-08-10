using System.Text;

namespace SonarAnalyzer.CSharp.Rules;

// Decides whether request input reaching a URL expression can still influence where that URL points, or whether the
// literal text around it has already pinned the destination down.
// An id substituted into "https://images.internal/images/{id}" only picks a path segment; the same value used as
// "https://{id}/images" picks the host. The difference is what the literal text before the value already contains.
internal static class GpUrlExpressionHelper
{
    // Returns the name of the enclosing action's parameter that can still steer the destination, or null when none can.
    internal static string ActionParameterSteeringDestination(SemanticModel model, ExpressionSyntax urlExpression)
    {
        var literalPrefix = new StringBuilder();
        foreach (var part in Parts(urlExpression))
        {
            if (part.Literal is { } literal)
            {
                literalPrefix.Append(literal);
            }
            else if (GpRequestInputHelper.ActionParameterName(model, part.Node) is { } parameterName)
            {
                if (!DestinationIsFixed(literalPrefix.ToString()))
                {
                    return parameterName;
                }
            }
            // A value that is not request input contributes no known text, so it neither fixes the destination
            // nor is worth reporting - the loop simply moves past it.
        }

        return null;
    }

    // The destination stops being caller-controllable once the text so far has moved past the authority into a path.
    // A single leading slash is not enough because caller input beginning with slash would form a protocol-relative URL.
    private static bool DestinationIsFixed(string literalPrefix)
    {
        var schemeEnd = literalPrefix.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            return literalPrefix.IndexOf('/', schemeEnd + 3) >= 0;
        }

        if (!literalPrefix.StartsWith("/", StringComparison.Ordinal))
        {
            return literalPrefix.IndexOf('/') >= 0;
        }

        var firstNonSlash = 0;
        while (firstNonSlash < literalPrefix.Length && literalPrefix[firstNonSlash] == '/')
        {
            firstNonSlash++;
        }

        return (firstNonSlash == 1 && firstNonSlash < literalPrefix.Length)
               || (firstNonSlash < literalPrefix.Length && literalPrefix.IndexOf('/', firstNonSlash) >= 0);
    }

    // Flattens the expression into the order the pieces appear in the resulting string.
    private static IEnumerable<UrlPart> Parts(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                foreach (var part in Parts(parenthesized.Expression))
                {
                    yield return part;
                }

                break;

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                foreach (var part in Parts(binary.Left).Concat(Parts(binary.Right)))
                {
                    yield return part;
                }

                break;

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                yield return UrlPart.OfLiteral(literal.Token.ValueText);
                break;

            case InterpolatedStringExpressionSyntax interpolated:
                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax text)
                    {
                        yield return UrlPart.OfLiteral(text.TextToken.ValueText);
                    }
                    else if (content is InterpolationSyntax { Expression: { } interpolationExpression })
                    {
                        yield return UrlPart.OfNode(interpolationExpression);
                    }
                }

                break;

            default:
                yield return UrlPart.OfNode(expression);
                break;
        }
    }

    private readonly struct UrlPart
    {
        public string Literal { get; }

        public SyntaxNode Node { get; }

        private UrlPart(string literal, SyntaxNode node)
        {
            Literal = literal;
            Node = node;
        }

        public static UrlPart OfLiteral(string literal) => new(literal, null);

        public static UrlPart OfNode(SyntaxNode node) => new(null, node);
    }
}
