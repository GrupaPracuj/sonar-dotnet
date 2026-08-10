namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IdentifierShouldUseStandardCompoundWordSpelling : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0075";

    private const string MessageFormat = "Rename '{0}' to '{1}' - that is the standard spelling for this compound word.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKindEx.RecordDeclaration, SyntaxKindEx.RecordStructDeclaration);
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
        context.RegisterNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
        context.RegisterNodeAction(AnalyzeEnumMember, SyntaxKind.EnumMemberDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is BaseTypeDeclarationSyntax { Identifier: var identifier })
        {
            Report(context, identifier);
        }
    }

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is IMethodSymbol method && IsFreelyRenamable(method))
        {
            Report(context, declaration.Identifier);
        }
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is IPropertySymbol property && IsFreelyRenamable(property))
        {
            Report(context, declaration.Identifier);
        }
    }

    private static void AnalyzeField(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (FieldDeclarationSyntax)context.Node;
        foreach (var variable in declaration.Declaration.Variables)
        {
            Report(context, variable.Identifier);
        }
    }

    private static void AnalyzeParameter(SonarSyntaxNodeReportingContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (parameter.Identifier.ValueText.Length > 0)
        {
            Report(context, parameter.Identifier);
        }
    }

    private static void AnalyzeEnumMember(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (EnumMemberDeclarationSyntax)context.Node;
        Report(context, declaration.Identifier);
    }

    private static void Report(SonarSyntaxNodeReportingContext context, SyntaxToken identifier)
    {
        if (GpIdentifierWords.TryFixCompoundWord(identifier.ValueText, out var suggested))
        {
            context.ReportIssue(Rule, identifier, identifier.ValueText, suggested);
        }
    }

    // A parameter of an override or of an interface implementation can be renamed without breaking anything at
    // compile time - parameter names are not part of a signature - but the new name then disagrees with the
    // base/interface declaration, which is exactly what S927 reports. The misspelling is still worth pointing out, so
    // the analyzer reports it; the automatic rename is withheld (see the code fix) rather than trading one issue for
    // another. The fix belongs on the base declaration, from where it propagates.
    internal static bool ParameterIsFreelyRenamable(IParameterSymbol parameter) =>
        parameter.ContainingSymbol is not IMethodSymbol method || IsFreelyRenamable(method);

    // A method whose name is constrained by an override or an interface implementation (explicit or implicit) is
    // not the author's free choice to make - renaming it would either fail to override the base member, or (for an
    // implicit interface implementation, which binds purely by name+signature) silently stop implementing the
    // interface. A field, enum member, or a type name is always safe to rename on its own, so those get no such check.
    private static bool IsFreelyRenamable(IMethodSymbol method) =>
        !method.IsOverride
        && method.ExplicitInterfaceImplementations.IsEmpty
        && !ImplementsInterfaceMemberByName(method, method.Name, method.ContainingType);

    private static bool IsFreelyRenamable(IPropertySymbol property) =>
        !property.IsOverride
        && property.ExplicitInterfaceImplementations.IsEmpty
        && !ImplementsInterfaceMemberByName(property, property.Name, property.ContainingType);

    private static bool ImplementsInterfaceMemberByName(ISymbol member, string name, INamedTypeSymbol containingType) =>
        containingType is not null
        && containingType.AllInterfaces
            .SelectMany(x => x.GetMembers(name))
            .Any(candidate => member.Equals(containingType.FindImplementationForInterfaceMember(candidate)));
}
