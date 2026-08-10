using Microsoft.CodeAnalysis.Text;

namespace SonarAnalyzer.CSharp.Rules;

// Both code fixes below operate on a route template string literal. The analyzer reports on a sub-range of the
// literal's characters (excluding the quotes), which the shared DocumentBasedFixAllProvider cannot map back to
// exactly: when the reported span does not equal a whole token's span, it widens the location to the smallest
// enclosing node - the whole string literal expression, quotes included - before replaying the diagnostic. So each
// fix below works out what to change from the token's own value text, using the analyzer's own
// RouteNamingConventions.Segments/IsParameterOrToken/IsKebabCase rather than a second copy of those rules, and only
// trusts the incoming span when it really is a sub-range of the literal (which identifies the reported segment).
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

    // The offset inside the value text the diagnostic points at, or null when the span is not a proper sub-range of
    // this literal's characters (a widened FixAll location, or a location on the whole attribute).
    internal static int? ValueOffset(SyntaxToken token, TextSpan diagnosticSpan)
    {
        var valueStart = token.SpanStart + 1;
        var valueEnd = token.Span.End - 1;
        return diagnosticSpan.Start >= valueStart && diagnosticSpan.End <= valueEnd
            ? diagnosticSpan.Start - valueStart
            : null;
    }

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
        var offenders = RouteNamingConventions.Segments(template)
            .Where(x => !RouteNamingConventions.IsParameterOrToken(x.Segment) && !RouteNamingConventions.IsKebabCase(x.Segment))
            .ToArray();
        // With several offending segments in one template, fixing the first one for every diagnostic would rename the
        // wrong segment, so the reported offset picks the segment this diagnostic is actually about when it is known.
        var reportedOffset = RouteLiteralCodeFixHelper.ValueOffset(token, diagnostic.Location.SourceSpan);
        var offender = reportedOffset is { } offset
            ? Array.Find(offenders, x => offset >= x.Offset && offset < x.Offset + x.Segment.Length)
            : offenders.FirstOrDefault();
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
