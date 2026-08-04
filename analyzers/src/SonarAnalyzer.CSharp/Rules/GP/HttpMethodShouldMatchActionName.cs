namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpMethodShouldMatchActionName : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0015";

    private const string MessageFormat = "Method '{0}' looks like it performs a {1} action but is annotated with [{2}].";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Deliberately narrow and unambiguous: unlike HttpGet/HttpDelete, HttpPost/HttpPut/HttpPatch are used both for
    // CRUD and for arbitrary non-CRUD actions, so a leading-verb mismatch there is not a reliable signal.
    private static readonly HashSet<string> MutatingVerbs = new(StringComparer.Ordinal)
    {
        "Create", "Update", "Delete", "Remove", "Add", "Insert", "Save", "Modify", "Edit"
    };

    private static readonly HashSet<string> ReadOrCreationVerbs = new(StringComparer.Ordinal)
    {
        "Get", "Find", "Search", "List", "Fetch", "Retrieve", "Create", "Add", "Insert"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not MethodDeclarationSyntax methodDeclaration
            || context.Model.GetDeclaredSymbol(methodDeclaration) is not { } method
            || !method.IsControllerActionMethod())
        {
            return;
        }

        var httpVerbAttribute = GetHttpVerbAttributeName(method);
        if (httpVerbAttribute is null)
        {
            return;
        }

        var leadingWord = GpIdentifierWords.LeadingWord(method.Name);

        if (httpVerbAttribute == "HttpGet" && MutatingVerbs.Contains(leadingWord))
        {
            context.ReportIssue(Rule, methodDeclaration.Identifier, method.Name, "mutating", httpVerbAttribute);
        }
        else if (httpVerbAttribute == "HttpDelete" && ReadOrCreationVerbs.Contains(leadingWord))
        {
            context.ReportIssue(Rule, methodDeclaration.Identifier, method.Name, "read or creation", httpVerbAttribute);
        }
    }

    private static string GetHttpVerbAttributeName(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is "HttpGetAttribute" or "HttpGet")
            {
                return "HttpGet";
            }

            if (attribute.AttributeClass?.Name is "HttpDeleteAttribute" or "HttpDelete")
            {
                return "HttpDelete";
            }
        }

        return null;
    }
}
