namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicApiShouldNotExposeConcreteDictionary : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0091";

    private const string MessageFormat = "Expose a dictionary interface instead of the concrete '{0}' type in this public {1}.";
    private const string HashtableType = "System.Collections.Hashtable";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration);
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeField, SyntaxKind.VariableDeclarator);
    }

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not BaseMethodDeclarationSyntax declaration
            || context.ContainingSymbol is not IMethodSymbol
            {
                IsPubliclyAccessible: true,
                IsOverride: false,
                MethodKind: MethodKind.Ordinary or MethodKind.Constructor,
            } method)
        {
            return;
        }

        var memberKind = method.IsConstructor ? "constructor" : "method";
        if (declaration is MethodDeclarationSyntax methodDeclaration)
        {
            ReportIfConcreteDictionary(context, method.ReturnType, methodDeclaration.ReturnType, memberKind);
        }

        var parameters = declaration.ParameterList?.Parameters ?? default;
        for (var i = 0; i < Math.Min(method.Parameters.Length, parameters.Count); i++)
        {
            ReportIfConcreteDictionary(context, method.Parameters[i].Type, parameters[i].Type, memberKind);
        }
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is PropertyDeclarationSyntax declaration
            && context.ContainingSymbol is IPropertySymbol { IsPubliclyAccessible: true, IsOverride: false } property
            && !HasXmlElementAttribute(property))
        {
            ReportIfConcreteDictionary(context, property.Type, declaration.Type, "property");
        }
    }

    private static void AnalyzeField(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax field } declaration }
            && declaration.Variables[0] == context.Node
            && context.ContainingSymbol is IFieldSymbol { IsPubliclyAccessible: true } symbol
            && !HasXmlElementAttribute(symbol))
        {
            ReportIfConcreteDictionary(context, symbol.Type, field.Declaration.Type, "field");
        }
    }

    private static void ReportIfConcreteDictionary(
        SonarSyntaxNodeReportingContext context,
        ITypeSymbol type,
        TypeSyntax syntax,
        string memberKind)
    {
        if (syntax is not null && IsConcreteDictionary(type))
        {
            context.ReportIssue(Rule, syntax, type.Name, memberKind);
        }
    }

    private static bool IsConcreteDictionary(ITypeSymbol type) =>
        type.Is(KnownType.System_Collections_Generic_Dictionary_TKey_TValue)
        || type.ToDisplayString() == HashtableType;

    private static bool HasXmlElementAttribute(ISymbol symbol) =>
        symbol.GetAttributes(KnownType.System_Xml_Serialization_XmlElementAttribute).Any();
}
