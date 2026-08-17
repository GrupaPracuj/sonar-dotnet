namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DapperTypeHandlersShouldBeRegisteredOnce : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0107";

    private const string MessageFormat = "Register Dapper type handlers once during application startup, not in an instance constructor.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
            {
                Name: "AddTypeHandler",
                IsStatic: true,
                ContainingType: { } containingType,
            }
            || !containingType.Is(KnownType.Dapper_SqlMapper)
            || invocation.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not { } constructor
            || invocation.Ancestors().TakeWhile(x => x != constructor).Any(IsNestedFunction)
            || constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return;
        }

        context.ReportIssue(Rule, invocation);
    }

    private static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax || node.Kind() == SyntaxKindEx.LocalFunctionStatement;
}
