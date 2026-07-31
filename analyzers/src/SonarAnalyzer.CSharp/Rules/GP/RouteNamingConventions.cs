namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RouteNamingConventions : SonarDiagnosticAnalyzer
{
    internal const string KebabCaseRuleId = "GP0012";
    internal const string NoVerbRuleId = "GP0013";
    internal const string NoTrailingSlashRuleId = "GP0014";

    private const string KebabCaseMessage = "Rename route segment '{0}' to kebab-case.";
    private const string NoVerbMessage = "Remove the verb '{0}' from the route; the HTTP method already expresses the action.";
    private const string NoTrailingSlashMessage = "Remove the trailing slash from the route.";

    private static readonly DiagnosticDescriptor KebabCaseRule = DescriptorFactory.Create(KebabCaseRuleId, KebabCaseMessage);
    private static readonly DiagnosticDescriptor NoVerbRule = DescriptorFactory.Create(NoVerbRuleId, NoVerbMessage);
    private static readonly DiagnosticDescriptor NoTrailingSlashRule = DescriptorFactory.Create(NoTrailingSlashRuleId, NoTrailingSlashMessage);

    private static readonly HashSet<string> VerbSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        // CRUD / HTTP-method-duplicating verbs
        "get", "create", "update", "delete", "remove", "add", "fetch", "list", "edit", "modify", "set", "retrieve", "save", "post", "put", "patch", "insert",
        // Retrieval / query verbs
        "find", "search", "query", "load", "read",
        // Validation verbs
        "validate", "verify", "check",
        // Lifecycle / workflow action verbs
        "cancel", "approve", "reject", "confirm", "activate", "deactivate", "enable", "disable", "start", "stop", "assign", "register",
        // Transfer / notification verbs
        "upload", "download", "send", "submit", "notify", "sync", "refresh",
        // Generic action verbs
        "execute", "run", "perform", "process", "generate", "calculate"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(KebabCaseRule, NoVerbRule, NoTrailingSlashRule);

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
                context.ReportIssue(NoTrailingSlashRule, attributeSyntax);
            }

            foreach (var segment in template.Split('/').Where(x => x.Length > 0 && !IsParameterOrToken(x)))
            {
                if (StartsWithVerb(segment, out var matchedVerb))
                {
                    context.ReportIssue(NoVerbRule, attributeSyntax, matchedVerb);
                }

                if (!IsKebabCase(segment))
                {
                    context.ReportIssue(KebabCaseRule, attributeSyntax, segment);
                }
            }
        }
    }

    private static bool IsParameterOrToken(string segment) =>
        segment[0] is '{' or '[';

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
}
