namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VolatileFieldShouldNotBeUpdatedNonAtomically : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0042";

    private const string MessageFormat = "'{0}' is volatile, which does not make this update atomic - use Interlocked or a lock.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(
            AnalyzeIncrementOrDecrement,
            SyntaxKind.PreIncrementExpression,
            SyntaxKind.PreDecrementExpression,
            SyntaxKind.PostIncrementExpression,
            SyntaxKind.PostDecrementExpression);

        context.RegisterNodeAction(
            AnalyzeAssignment,
            SyntaxKind.AddAssignmentExpression,
            SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression,
            SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression,
            SyntaxKind.OrAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression,
            SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeIncrementOrDecrement(SonarSyntaxNodeReportingContext context)
    {
        var operand = context.Node switch
        {
            PrefixUnaryExpressionSyntax prefix => prefix.Operand,
            PostfixUnaryExpressionSyntax postfix => postfix.Operand,
            _ => null,
        };

        if (VolatileField(context.Model, operand) is { } field)
        {
            context.ReportIssue(Rule, context.Node, field.Name);
        }
    }

    private static void AnalyzeAssignment(SonarSyntaxNodeReportingContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (VolatileField(context.Model, assignment.Left) is not { } field)
        {
            return;
        }

        // A compound assignment always reads the field first. A simple assignment only does when the field appears
        // on the right-hand side - "_shouldStop = true" is the atomic write volatile actually guarantees.
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || ReadsField(context.Model, assignment.Right, field))
        {
            context.ReportIssue(Rule, assignment, field.Name);
        }
    }

    private static bool ReadsField(SemanticModel model, ExpressionSyntax expression, IFieldSymbol field) =>
        expression.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Any(x => model.GetSymbolInfo(x).Symbol is IFieldSymbol other && other.Name == field.Name && other.IsVolatile);

    private static IFieldSymbol VolatileField(SemanticModel model, ExpressionSyntax expression) =>
        expression is not null && model.GetSymbolInfo(expression).Symbol is IFieldSymbol { IsVolatile: true } field
            ? field
            : null;
}
