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
        if (!actionMethods.Any(HasAttribute("Authorize")) || DeclaresAccessPolicyForWholeType(type))
        {
            // No per-action [Authorize] convention to compare against, or access control is already declared at class level.
            return;
        }

        foreach (var method in actionMethods.Where(x => !HasAttribute("Authorize")(x) && !HasAttribute("AllowAnonymous")(x)))
        {
            if (method.DeclaringSyntaxReferences.Select(x => x.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault(x => x.Parent == classDeclaration) is { } methodDeclaration)
            {
                context.ReportIssue(Rule, methodDeclaration.Identifier, method.Name, type.Name);
            }
        }
    }

    // A shared base controller carrying [Authorize] (or [AllowAnonymous]) already declares the policy for every action
    // it inherits down, and ASP.NET Core honours that, so the derived controller has nothing left to declare. Both
    // attributes are Inherited by default, hence AttributesWithInherited rather than the type's own attributes.
    private static bool DeclaresAccessPolicyForWholeType(INamedTypeSymbol type) =>
        type.AttributesWithInherited.Any(x => IsNamed(x, "Authorize") || IsNamed(x, "AllowAnonymous"));

    private static Func<ISymbol, bool> HasAttribute(string name) =>
        symbol => symbol.GetAttributes().Any(x => IsNamed(x, name));

    private static bool IsNamed(AttributeData attribute, string name) =>
        attribute.AttributeClass?.Name is var className && (className == name || className == name + "Attribute");
}
