using Microsoft.CodeAnalysis.Text;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RouteNamingConventions : SonarDiagnosticAnalyzer
{
    internal const string KebabCaseRuleId = "GP0012";
    internal const string NoVerbRuleId = "GP0013";
    internal const string NoTrailingSlashRuleId = "GP0014";
    internal const string SecretRouteParameterRuleId = "GP0023";

    private const string KebabCaseMessage = "Rename route segment '{0}' to kebab-case.";
    private const string NoVerbMessage = "Remove the verb '{0}' from the route; the HTTP method already expresses the action.";
    private const string NoTrailingSlashMessage = "Remove the trailing slash from the route.";
    private const string SecretRouteParameterMessage = "Route parameter '{0}' looks like it carries a secret - it will end up in server logs, browser history and proxy caches.";

    private static readonly DiagnosticDescriptor KebabCaseRule = DescriptorFactory.Create(KebabCaseRuleId, KebabCaseMessage);
    private static readonly DiagnosticDescriptor NoVerbRule = DescriptorFactory.Create(NoVerbRuleId, NoVerbMessage);
    private static readonly DiagnosticDescriptor NoTrailingSlashRule = DescriptorFactory.Create(NoTrailingSlashRuleId, NoTrailingSlashMessage);
    private static readonly DiagnosticDescriptor SecretRouteParameterRule = DescriptorFactory.Create(SecretRouteParameterRuleId, SecretRouteParameterMessage);

    // Ordered longest-first so prefix matching always reports the longest verb that fits, independently of how the
    // collection happens to be laid out in memory.
    private static readonly string[] VerbSegments = SortedByLengthDescending(
        // CRUD / HTTP-method-duplicating verbs
        "get", "create", "update", "delete", "remove", "add", "fetch", "list", "edit", "modify", "set", "retrieve", "save", "post", "put", "patch", "insert",
        // Retrieval / query verbs
        "find", "search", "query", "load", "read",
        // Validation verbs. "validate" is intentionally excluded because validation is often a distinct operation
        // whose semantics cannot be expressed by an HTTP method alone.
        "verify", "check",
        // Lifecycle / workflow action verbs
        "cancel", "approve", "reject", "confirm", "activate", "deactivate", "enable", "disable", "start", "stop", "assign", "register",
        // Transfer / notification verbs
        "upload", "download", "send", "submit", "notify", "sync", "refresh",
        // Generic action verbs
        "execute", "run", "perform", "process", "generate", "calculate");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(KebabCaseRule, NoVerbRule, NoTrailingSlashRule, SecretRouteParameterRule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        if (context.Model.GetDeclaredSymbol(context.Node) is IMethodSymbol method)
        {
            AnalyzeAttributes(context, method.GetAttributes());
        }
    }

    private static void AnalyzeClass(SonarSyntaxNodeReportingContext context)
    {
        if (context.Model.GetDeclaredSymbol(context.Node) is INamedTypeSymbol type)
        {
            AnalyzeAttributes(context, type.GetAttributes());
        }
    }

    private static void AnalyzeAttributes(SonarSyntaxNodeReportingContext context, ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeRouteTemplate is not { Length: > 0 } template
                || attribute.ApplicationSyntaxReference?.GetSyntax() is not { } attributeSyntax)
            {
                continue;
            }

            if (template.Length > 1 && template.EndsWith("/", StringComparison.Ordinal))
            {
                Report(context, NoTrailingSlashRule, attributeSyntax, template, template.Length - 1, 1);
            }

            foreach (var (segment, offset) in Segments(template))
            {
                AnalyzeSegment(context, attributeSyntax, template, segment, offset);
            }
        }
    }

    private static void AnalyzeSegment(SonarSyntaxNodeReportingContext context, SyntaxNode attributeSyntax, string template, string segment, int offset)
    {
        if (IsParameterOrToken(segment))
        {
            if (IsParameter(segment) && ParameterName(segment) is { Length: > 0 } parameterName && GpIdentifierWords.ContainsSecretWord(parameterName))
            {
                Report(context, SecretRouteParameterRule, attributeSyntax, template, offset + 1, parameterName.Length, parameterName);
            }

            return;
        }

        if (StartsWithVerb(segment, out var matchedVerb))
        {
            Report(context, NoVerbRule, attributeSyntax, template, offset, matchedVerb.Length, matchedVerb);
        }

        if (!IsKebabCase(segment))
        {
            Report(context, KebabCaseRule, attributeSyntax, template, offset, segment.Length, segment);
        }
    }

    // Yields every non-empty segment together with its offset inside the template, so an issue can point at the
    // offending part of the route instead of stacking several issues on the whole attribute.
    private static IEnumerable<(string Segment, int Offset)> Segments(string template)
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

    private static void Report(SonarSyntaxNodeReportingContext context,
                              DiagnosticDescriptor rule,
                              SyntaxNode attributeSyntax,
                              string template,
                              int offset,
                              int length,
                              params string[] messageArgs) =>
        context.ReportIssue(rule, TemplateLocation(attributeSyntax, template, offset, length), messageArgs: messageArgs);

    // Points inside the route template string when the offsets can be mapped safely, which requires a plain string
    // literal whose text is exactly the value plus the two quotes - a verbatim prefix or any escape sequence would
    // shift every position after it. Otherwise the whole attribute is used.
    private static Location TemplateLocation(SyntaxNode attributeSyntax, string template, int offset, int length)
    {
        if (attributeSyntax is AttributeSyntax { ArgumentList.Arguments: { Count: > 0 } arguments }
            && arguments.Select(x => x.Expression)
                .OfType<LiteralExpressionSyntax>()
                .FirstOrDefault(x => x.IsKind(SyntaxKind.StringLiteralExpression) && x.Token.ValueText == template) is { Token: var token }
            && token.Text.Length == template.Length + 2)
        {
            return Location.Create(token.SyntaxTree, new TextSpan(token.SpanStart + 1 + offset, length));
        }

        return attributeSyntax.GetLocation();
    }

    private static bool IsParameterOrToken(string segment) =>
        segment[0] is '{' or '[';

    private static bool IsParameter(string segment) =>
        segment[0] == '{' && segment.EndsWith("}", StringComparison.Ordinal);

    // Strips the enclosing braces and any constraint/default value, e.g. "{apiKey:guid=null}" -> "apiKey".
    private static string ParameterName(string segment)
    {
        var name = segment.Substring(1, segment.Length - 2).TrimStart('*');
        var boundary = name.IndexOfAny(new[] { ':', '=' });
        name = boundary >= 0 ? name.Substring(0, boundary) : name;
        return name.TrimEnd('?');
    }

    private static bool StartsWithVerb(string segment, out string matchedVerb)
    {
        foreach (var verb in VerbSegments)
        {
            if (segment.Equals(verb, StringComparison.OrdinalIgnoreCase))
            {
                matchedVerb = segment;
                return true;
            }

            if (segment.Length > verb.Length && segment.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
            {
                var boundary = segment[verb.Length];
                if (boundary is '-' or '_' || char.IsUpper(boundary))
                {
                    matchedVerb = segment.Substring(0, verb.Length);
                    return true;
                }
            }
        }

        matchedVerb = null;
        return false;
    }

    private static bool IsKebabCase(string segment) =>
        segment[0] != '-'
        && segment[segment.Length - 1] != '-'
        && !segment.Contains("--")
        && segment.All(x => char.IsLower(x) || char.IsDigit(x) || x == '-');

    private static string[] SortedByLengthDescending(params string[] verbs)
    {
        var sorted = (string[])verbs.Clone();
        Array.Sort(sorted, (left, right) => right.Length.CompareTo(left.Length) is var byLength && byLength != 0
            ? byLength
            : string.CompareOrdinal(left, right));
        return sorted;
    }
}
