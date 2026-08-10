namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControllersShouldNotUseDbContextDirectly : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0032";

    private const string MessageFormat = "Do not use '{0}' in a controller - reach data through a service or repository instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);

    private static void AnalyzeClass(SonarSyntaxNodeReportingContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(classDeclaration) is not { } type || !type.IsControllerType())
        {
            return;
        }

        foreach (var declaredType in DeclaredTypes(classDeclaration))
        {
            if (DbContextName(context.Model, declaredType) is { } name)
            {
                context.ReportIssue(Rule, declaredType, name);
            }
        }
    }

    // The three ways a context reaches a controller: injected into a field, taken as a constructor parameter, or
    // created as a local. Action parameters are not included - the model binder never binds a DbContext.
    private static IEnumerable<TypeSyntax> DeclaredTypes(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var parameter in classDeclaration.ParameterList()?.Parameters ?? [])
        {
            if (parameter.Type is { } parameterType)
            {
                yield return parameterType;
            }
        }

        foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            yield return field.Declaration.Type;
        }

        foreach (var parameter in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().SelectMany(x => x.ParameterList.Parameters))
        {
            if (parameter.Type is { } parameterType)
            {
                yield return parameterType;
            }
        }

        foreach (var local in classDeclaration.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            yield return local.Declaration.Type;
        }
    }

    private static string DbContextName(SemanticModel model, TypeSyntax typeSyntax) =>
        model.GetTypeInfo(typeSyntax).Type is { } type && IsDbContext(type)
            ? type.Name
            : null;

    private static bool IsDbContext(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsAny(KnownType.Microsoft_EntityFrameworkCore_DbContext, KnownType.Microsoft_EntityFramework_DbContext))
            {
                return true;
            }
        }

        return false;
    }
}
