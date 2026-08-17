namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpActionShouldHaveOpenApiDescription : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0101";

    private const string MessageFormat = "Describe this HTTP action for OpenAPI consumers.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not { } method
            || !GpOpenApiMetadata.IsOpenApiAction(method)
            || method.IsAbstract
            || GpOpenApiMetadata.IsIgnored(method)
            || HasDescription(method, declaration))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier);
    }

    private static bool HasDescription(IMethodSymbol method, MethodDeclarationSyntax declaration) =>
        method.AttributesWithInherited.Any(HasDescription)
        || HasXmlDescription(method.GetDocumentationCommentXml())
        || HasAdjacentXmlDocumentation(declaration);

    private static bool HasAdjacentXmlDocumentation(MethodDeclarationSyntax declaration)
    {
        var start = declaration.AttributeLists.FirstOrDefault()?.SpanStart ?? declaration.SpanStart;
        var text = declaration.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(start).LineNumber - 1;
        var documentation = new List<string>();
        while (line >= 0 && text.Lines[line].ToString().TrimStart().StartsWith("///", StringComparison.Ordinal))
        {
            documentation.Insert(0, text.Lines[line].ToString().TrimStart().Substring(3));
            line--;
        }

        return documentation.Count > 0 && HasXmlDescription(string.Join(Environment.NewLine, documentation));
    }

    private static bool HasXmlDescription(string documentation)
    {
        if (documentation.IndexOf("<inheritdoc", StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        return HasNonEmptyElement(documentation, "summary") || HasNonEmptyElement(documentation, "remarks");
    }

    private static bool HasNonEmptyElement(string documentation, string elementName)
    {
        var openingTag = $"<{elementName}>";
        var closingTag = $"</{elementName}>";
        var start = documentation.IndexOf(openingTag, StringComparison.Ordinal);
        var end = documentation.IndexOf(closingTag, StringComparison.Ordinal);
        return start >= 0
               && end > start
               && !string.IsNullOrWhiteSpace(documentation.Substring(start + openingTag.Length, end - start - openingTag.Length));
    }

    private static bool HasDescription(AttributeData attribute)
    {
        var name = attribute.AttributeClass?.Name;
        if (name == "SwaggerOperationAttribute")
        {
            return attribute.ConstructorArguments.Any(x => x.Value is string { Length: > 0 })
                   || attribute.NamedArguments.Any(x =>
                       x.Key is "Summary" or "Description" && x.Value.Value is string { Length: > 0 });
        }

        return name is "EndpointSummaryAttribute" or "EndpointDescriptionAttribute"
               && attribute.ConstructorArguments.Any(x => x.Value is string { Length: > 0 });
    }
}
