namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UsingShouldNotUseThrowingObjectInitializer : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0089";

    private const string MessageFormat = "This 'using' constructs '{0}' via an object initializer - if a member assignment throws, the instance is "
                                          + "never bound and 'Dispose' is never called. Assign the risky members in separate statements after construction.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeUsingDeclaration, SyntaxKind.LocalDeclarationStatement);
        context.RegisterNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
    }

    // using var x = new Foo { ... };
    private static void AnalyzeUsingDeclaration(SonarSyntaxNodeReportingContext context)
    {
        var localDeclaration = (LocalDeclarationStatementSyntax)context.Node;
        if (localDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            AnalyzeVariables(context, localDeclaration.Declaration);
        }
    }

    // using (var x = new Foo { ... }) { ... }
    private static void AnalyzeUsingStatement(SonarSyntaxNodeReportingContext context)
    {
        var usingStatement = (UsingStatementSyntax)context.Node;
        if (usingStatement.Declaration is { } declaration)
        {
            AnalyzeVariables(context, declaration);
        }
    }

    private static void AnalyzeVariables(SonarSyntaxNodeReportingContext context, VariableDeclarationSyntax declaration)
    {
        foreach (var variable in declaration.Variables)
        {
            if (variable.Initializer?.Value is ObjectCreationExpressionSyntax
                {
                    Initializer: { RawKind: (int)SyntaxKind.ObjectInitializerExpression, Expressions.Count: > 0 } initializer
                } objectCreation
                && initializer.Expressions.Any(HasRiskyValue))
            {
                context.ReportIssue(Rule, objectCreation, variable.Identifier.ValueText);
            }
        }
    }

    private static bool HasRiskyValue(ExpressionSyntax memberInitializer) =>
        memberInitializer is AssignmentExpressionSyntax { Right: { } value } && IsRisky(value);

    // The "safe, never flag" list is deliberately narrow: a literal, or a bare identifier/parameter read, cannot realistically
    // throw. Anything else - a call, a member access chain, a cast, a binary expression, ... - can, so it is treated as risky.
    private static bool IsRisky(ExpressionSyntax value) =>
        value switch
        {
            LiteralExpressionSyntax => false,
            IdentifierNameSyntax => false,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax } => false,
            _ => true,
        };
}
