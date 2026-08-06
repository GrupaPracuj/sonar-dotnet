namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticConstructorShouldNotThrow : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0078";

    private const string MessageFormat = "Static constructors should not throw - it permanently poisons '{0}' for the rest of the process.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);

    private static void AnalyzeConstructor(SonarSyntaxNodeReportingContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;
        if (!constructor.Modifiers.Any(SyntaxKind.StaticKeyword) || !ThrowsDirectly(constructor))
        {
            return;
        }

        context.ReportIssue(Rule, constructor.Identifier, constructor.Identifier.ValueText);
    }

    // A throw that runs synchronously as part of the type initializer - as opposed to one that only fires later,
    // from inside a local function or lambda the static constructor merely declares (e.g. assigns to a field to
    // run on demand) rather than calls right away while the type is being initialized.
    // "ExpressionBody" is ambiguous as a plain member access here: two different lightup/shim layers each add an
    // extension for it on this project's compile-time (older) Roslyn reference, so the call is qualified to pick one.
    private static bool ThrowsDirectly(ConstructorDeclarationSyntax constructor)
    {
        SyntaxNode body = constructor.Body ?? (SyntaxNode)StyleCop.Analyzers.Lightup.BaseMethodDeclarationSyntaxExtensions.ExpressionBody(constructor);
        return body is not null && body.DescendantNodes(DoesNotBelongToANestedFunction).Any(IsThrow);
    }

    // A throw inside a lambda or local function exits that function, not the static constructor, so it does not
    // run while the type is being initialized and cannot poison the type.
    private static bool DoesNotBelongToANestedFunction(SyntaxNode node) =>
        node.Kind() != SyntaxKindEx.LocalFunctionStatement && node is not AnonymousFunctionExpressionSyntax;

    private static bool IsThrow(SyntaxNode node) =>
        node is ThrowStatementSyntax || node.Kind() == SyntaxKindEx.ThrowExpression;
}
