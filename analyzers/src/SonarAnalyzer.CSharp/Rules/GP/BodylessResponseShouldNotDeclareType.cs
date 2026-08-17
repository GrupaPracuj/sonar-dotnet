namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BodylessResponseShouldNotDeclareType : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0104";

    private const string MessageFormat = "Remove the response body type from status {0}; this status cannot contain a body.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<int> BodylessStatusCodes = new() { 204, 205, 304 };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

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
        foreach (var attribute in attributes.Where(x => GpOpenApiMetadata.IsResponseAttribute(x)
                                                        && GpOpenApiMetadata.HasConcreteResponseType(x)
                                                        && GpOpenApiMetadata.ResponseStatusCode(x) is { } statusCode
                                                        && BodylessStatusCodes.Contains(statusCode)))
        {
            if (attribute.ApplicationSyntaxReference?.GetSyntax() is { } syntax)
            {
                context.ReportIssue(Rule, syntax, GpOpenApiMetadata.ResponseStatusCode(attribute).Value.ToString());
            }
        }
    }
}
