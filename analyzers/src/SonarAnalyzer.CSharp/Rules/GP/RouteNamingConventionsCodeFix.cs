using Microsoft.CodeAnalysis.Text;

namespace SonarAnalyzer.CSharp.Rules;

// Both code fixes below operate on a route template string literal. The analyzer reports on a sub-range of the
// literal's characters (excluding the quotes), which the shared DocumentBasedFixAllProvider cannot map back to
// exactly: when the reported span does not equal a whole token's span, it widens the location to the smallest
// enclosing node - the whole string literal expression, quotes included - before replaying the diagnostic. So
// rather than trusting the incoming diagnostic span for anything but "which literal token is this about", each fix
// below re-derives what needs changing straight from the token's own value text, using the same rules as the
// analyzer (see RouteNamingConventions.Segments/IsKebabCase and the trailing-slash check in AnalyzeAttributes).
internal static class RouteLiteralCodeFixHelper
{
    internal static bool TryGetPlainLiteralToken(SyntaxNode root, TextSpan diagnosticSpan, out SyntaxToken token)
    {
        token = default;
        var candidate = root.FindToken(diagnosticSpan.Start);
        if (!candidate.IsKind(SyntaxKind.StringLiteralToken) || candidate.Text.Length != candidate.ValueText.Length + 2)
        {
            return false;
        }

        token = candidate;
        return true;
    }

    internal static IEnumerable<(string Segment, int Offset)> Segments(string template)
    {
        var start = 0;
        while (start <= template.Length)
        {
            var end = template.IndexOf('/', start);
            if (end < 0)
            {
                end = template.Length;
            }

            if (end > start)
            {
                yield return (template.Substring(start, end - start), start);
            }

            start = end + 1;
        }
    }

    internal static bool IsParameterOrToken(string segment) =>
        segment[0] is '{' or '[';

    internal static bool IsKebabCase(string segment) =>
        segment[0] != '-'
        && segment[segment.Length - 1] != '-'
        && !segment.Contains("--")
        && segment.All(x => char.IsLower(x) || char.IsDigit(x) || x == '-');

    // Replaces the [start, start + length) range of the value text (i.e. offsets excluding the opening quote) and
    // rebuilds a literal token whose raw text and value text stay consistent with each other.
    internal static SyntaxToken WithReplacedValueRange(SyntaxToken token, int start, int length, string replacement)
    {
        var rawText = token.Text;
        var newRawText = rawText.Substring(0, 1 + start) + replacement + rawText.Substring(1 + start + length);
        var newValueText = newRawText.Substring(1, newRawText.Length - 2);
        return SyntaxFactory.Literal(token.LeadingTrivia, newRawText, newValueText, token.TrailingTrivia);
    }
}

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class RouteSegmentShouldBeKebabCaseCodeFix : SonarCodeFix
{
    internal const string Title = "Convert to kebab-case";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(RouteNamingConventions.KebabCaseRuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!RouteLiteralCodeFixHelper.TryGetPlainLiteralToken(root, diagnostic.Location.SourceSpan, out var token))
        {
            return Task.CompletedTask;
        }

        var template = token.ValueText;
        var offender = RouteLiteralCodeFixHelper.Segments(template)
            .FirstOrDefault(x => !RouteLiteralCodeFixHelper.IsParameterOrToken(x.Segment) && !RouteLiteralCodeFixHelper.IsKebabCase(x.Segment));
        if (offender.Segment is not { Length: > 0 } segment)
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var kebab = ToKebabCase(segment);
                var newToken = RouteLiteralCodeFixHelper.WithReplacedValueRange(token, offender.Offset, segment.Length, kebab);
                var newRoot = root.ReplaceToken(token, newToken);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }

    // Deterministic PascalCase/camelCase/snake_case -> kebab-case: lowercase everything, insert '-' at a
    // lower-to-upper boundary, and turn '_' into '-'.
    private static string ToKebabCase(string segment)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];
            if (c == '_')
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }
            }
            else if (char.IsUpper(c))
            {
                if (i > 0 && builder.Length > 0 && builder[builder.Length - 1] != '-'
                    && (char.IsLower(segment[i - 1]) || char.IsDigit(segment[i - 1])))
                {
                    builder.Append('-');
                }
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class RouteShouldNotHaveTrailingSlashCodeFix : SonarCodeFix
{
    internal const string Title = "Remove the trailing slash";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(RouteNamingConventions.NoTrailingSlashRuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!RouteLiteralCodeFixHelper.TryGetPlainLiteralToken(root, diagnostic.Location.SourceSpan, out var token))
        {
            return Task.CompletedTask;
        }

        var template = token.ValueText;
        if (template.Length <= 1 || !template.EndsWith("/", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newToken = RouteLiteralCodeFixHelper.WithReplacedValueRange(token, template.Length - 1, 1, string.Empty);
                var newRoot = root.ReplaceToken(token, newToken);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }
}
