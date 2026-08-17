namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpActionShouldDocumentResponseStatusCodes : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0100";

    private const string MessageFormat = "Document the non-200 response {0} with ProducesResponseType.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);

    private static void AnalyzeMethod(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (context.Model.GetDeclaredSymbol(declaration) is not { } method
            || !GpOpenApiMetadata.IsOpenApiAction(method)
            || GpOpenApiMetadata.IsIgnored(method)
            || GpOpenApiMetadata.UsesApiConvention(method))
        {
            return;
        }

        var documented = GpOpenApiMetadata.ResponseAttributes(method)
            .Select(GpOpenApiMetadata.ResponseStatusCode)
            .WhereNotNull()
            .ToHashSet();
        var missing = GpOpenApiMetadata.ReturnedInvocations(declaration)
            .Select(x => GpOpenApiMetadata.ResponseStatusCode(context.Model, x))
            .Where(x => x is not null and not 200)
            .Select(x => x.Value)
            .Where(x => !documented.Contains(x))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        if (missing.Length > 0)
        {
            context.ReportIssue(Rule, declaration.Identifier, string.Join(", ", missing));
        }
    }

}
