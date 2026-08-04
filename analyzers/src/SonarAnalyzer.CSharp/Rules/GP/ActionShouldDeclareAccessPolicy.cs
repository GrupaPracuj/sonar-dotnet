namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ActionShouldDeclareAccessPolicy : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0020";

    private const string MessageFormat = "Method '{0}' has neither [Authorize] nor [AllowAnonymous], but other actions in '{1}' are explicitly protected with [Authorize].";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);

    private static void AnalyzeClass(SonarSyntaxNodeReportingContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(classDeclaration) is not { } type)
        {
            return;
        }

        var actionMethods = type.GetMembers().OfType<IMethodSymbol>().Where(x => x.IsControllerActionMethod()).ToList();
        if (!actionMethods.Any(HasAttribute("Authorize")) || HasAttribute("Authorize")(type) || HasAttribute("AllowAnonymous")(type))
        {
            // No per-action [Authorize] convention to compare against, or access control is already declared at class level.
            return;
        }

        foreach (var method in actionMethods.Where(x => !HasAttribute("Authorize")(x) && !HasAttribute("AllowAnonymous")(x)))
        {
            if (method.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault(x => x.SyntaxTree == classDeclaration.SyntaxTree) is { } methodDeclaration)
            {
                context.ReportIssue(Rule, methodDeclaration.Identifier, method.Name, type.Name);
            }
        }
    }

    private static Func<ISymbol, bool> HasAttribute(string name) =>
        symbol => symbol.GetAttributes().Any(x => x.AttributeClass?.Name is var className && (className == name || className == name + "Attribute"));
}
