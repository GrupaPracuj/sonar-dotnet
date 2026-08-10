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

        if (httpVerbAttribute == "HttpGet" && MutatingVerbs.Contains(leadingWord) && !RendersView(methodDeclaration, method, context.Model))
        {
            context.ReportIssue(Rule, methodDeclaration.Identifier, method.Name, "mutating", httpVerbAttribute);
        }
        else if (httpVerbAttribute == "HttpDelete" && ReadOrCreationVerbs.Contains(leadingWord))
        {
            context.ReportIssue(Rule, methodDeclaration.Identifier, method.Name, "read or creation", httpVerbAttribute);
        }
    }

    // A classic MVC action that renders a view legitimately pairs [HttpGet] with a mutating name: [HttpGet] Edit(id)
    // serves the edit form and [HttpPost] Edit(model) applies it. Rendering a form changes no state, so the name
    // describes what the form is for rather than what the request does.
    private static bool RendersView(MethodDeclarationSyntax methodDeclaration, IMethodSymbol method, SemanticModel model) =>
        IsViewResultType(method.ReturnType)
        || methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(x => model.GetSymbolInfo(x).Symbol is IMethodSymbol { Name: "View" or "PartialView" } viewMethod
                      && IsMvcControllerType(viewMethod.ContainingType));

    private static bool IsViewResultType(ITypeSymbol returnType)
    {
        var type = returnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } wrapper
                   && wrapper.OriginalDefinition.IsAny(KnownType.System_Threading_Tasks_Task_T, KnownType.System_Threading_Tasks_ValueTask_TResult)
            ? wrapper.TypeArguments[0]
            : returnType;
        return type?.Name is "ViewResult" or "PartialViewResult"
               && type.ContainingNamespace?.ToDisplayString() is "Microsoft.AspNetCore.Mvc" or "System.Web.Mvc";
    }

    private static bool IsMvcControllerType(ITypeSymbol type) =>
        type?.ToDisplayString() is "Microsoft.AspNetCore.Mvc.Controller" or "System.Web.Mvc.Controller" or "System.Web.Mvc.ControllerBase";

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
