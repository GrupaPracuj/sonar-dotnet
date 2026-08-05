namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainShouldNotDependOnTransport : ParametrizedDiagnosticAnalyzer
{
    internal const string RuleId = "GP0065";

    private const string MessageFormat = "'{0}' comes from '{1}', which domain code should not depend on.";

    private const string DefaultDomainAssemblyNames = "Domain";
    private const string DefaultForbiddenNamespaces =
        "MassTransit,RabbitMQ.Client,Microsoft.AspNetCore,System.Net.Http,GP.Juno.Abstractions.EventStream,GP.Juno.Abstractions.Massaging,GP.Juno.HttpClient";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    [RuleParameter("domainNamespaces", PropertyType.String, "Comma-separated namespaces holding domain types, e.g. MyCompany.Domain", "")]
    public string DomainNamespaces { get; set; } = string.Empty;

    [RuleParameter("domainAssemblyNames", PropertyType.String, "Comma-separated fragments identifying a domain assembly by name", DefaultDomainAssemblyNames)]
    public string DomainAssemblyNames { get; set; } = DefaultDomainAssemblyNames;

    [RuleParameter("forbiddenNamespaces", PropertyType.String, "Comma-separated namespaces domain code must not depend on", DefaultForbiddenNamespaces)]
    public string ForbiddenNamespaces { get; set; } = DefaultForbiddenNamespaces;

    protected override void Initialize(SonarParametrizedAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var forbidden = GpEntityTypes.SplitParameter(ForbiddenNamespaces);
            var domainNamespaces = GpEntityTypes.SplitParameter(DomainNamespaces);
            var assemblyIsDomain = IsDomainAssembly(start.Compilation);

            // With no domain namespaces configured and a non-domain assembly name there is nothing this rule could
            // ever match, so it is not wired up at all.
            if (forbidden.Length == 0 || (!assemblyIsDomain && domainNamespaces.Length == 0))
            {
                return;
            }

            start.RegisterNodeAction(
                c => AnalyzeMember(c, forbidden, domainNamespaces, assemblyIsDomain),
                SyntaxKind.PropertyDeclaration,
                SyntaxKind.FieldDeclaration,
                SyntaxKind.Parameter,
                SyntaxKind.MethodDeclaration);
        });

    private bool IsDomainAssembly(Compilation compilation)
    {
        var names = GpEntityTypes.SplitParameter(DomainAssemblyNames);
        var assemblyName = compilation.AssemblyName ?? string.Empty;
        return names.Length > 0 && Array.Exists(names, x => assemblyName.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void AnalyzeMember(SonarSyntaxNodeReportingContext context, string[] forbidden, string[] domainNamespaces, bool assemblyIsDomain)
    {
        if (!assemblyIsDomain && !IsInDomainNamespace(context, domainNamespaces))
        {
            return;
        }

        foreach (var typeSyntax in DeclaredTypes(context.Node))
        {
            if (context.Model.GetTypeInfo(typeSyntax).Type is { } type
                && ForbiddenType(type, forbidden) is var (offending, forbiddenNamespace))
            {
                context.ReportIssue(Rule, typeSyntax, offending.Name, forbiddenNamespace);
            }
        }
    }

    private static bool IsInDomainNamespace(SonarSyntaxNodeReportingContext context, string[] domainNamespaces)
    {
        var containing = context.Model.GetEnclosingSymbol(context.Node.SpanStart)?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return Array.Exists(domainNamespaces, x => containing == x || containing.StartsWith(x + ".", StringComparison.Ordinal));
    }

    // A method contributes its return type; its parameters arrive as Parameter nodes of their own.
    private static IEnumerable<TypeSyntax> DeclaredTypes(SyntaxNode node) =>
        node switch
        {
            PropertyDeclarationSyntax property => [property.Type],
            FieldDeclarationSyntax field => [field.Declaration.Type],
            ParameterSyntax { Type: { } parameterType } => [parameterType],
            MethodDeclarationSyntax method => [method.ReturnType],
            _ => [],
        };

    // Looks inside generic arguments too, so Task<HttpResponseMessage> and IConsumer<T> are both caught.
    private static (ITypeSymbol Type, string Namespace)? ForbiddenType(ITypeSymbol type, string[] forbidden)
    {
        var containing = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (Array.Find(forbidden, x => containing == x || containing.StartsWith(x + ".", StringComparison.Ordinal)) is { } match)
        {
            return (type, match);
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (ForbiddenType(argument, forbidden) is { } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
