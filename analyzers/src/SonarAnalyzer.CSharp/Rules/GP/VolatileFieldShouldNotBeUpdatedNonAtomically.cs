/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

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
        if (IsInsideLock(context.Node))
        {
            return;
        }

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
        if (IsInsideLock(assignment) || VolatileField(context.Model, assignment.Left) is not { } field)
        {
            return;
        }

        // A compound assignment always reads the field first. A simple assignment only does when the field appears
        // on the right-hand side - "_shouldStop = true" is the atomic write volatile actually guarantees.
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || ReadsField(context.Model, assignment.Left, assignment.Right, field))
        {
            context.ReportIssue(Rule, assignment, field.Name);
        }
    }

    private static bool ReadsField(SemanticModel model, ExpressionSyntax target, ExpressionSyntax value, IFieldSymbol field) =>
        value.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Where(x => x is not IdentifierNameSyntax { Parent: MemberAccessExpressionSyntax memberAccess } || memberAccess.Name != x)
            .Any(x => model.GetSymbolInfo(x).Symbol is IFieldSymbol other
                      && other.Equals(field)
                      && SameReceiver(model, target, x, field));

    private static bool SameReceiver(SemanticModel model, ExpressionSyntax left, ExpressionSyntax right, IFieldSymbol field)
    {
        if (field.IsStatic)
        {
            return true;
        }

        var leftReceiver = Receiver(left);
        var rightReceiver = Receiver(right);
        if (IsCurrentInstance(leftReceiver) && IsCurrentInstance(rightReceiver))
        {
            return true;
        }

        return leftReceiver is not null
            && rightReceiver is not null
            && model.GetSymbolInfo(leftReceiver).Symbol is ILocalSymbol or IParameterSymbol
            && model.GetSymbolInfo(leftReceiver).Symbol.Equals(model.GetSymbolInfo(rightReceiver).Symbol);
    }

    private static ExpressionSyntax Receiver(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null;

    private static bool IsCurrentInstance(ExpressionSyntax receiver) =>
        receiver is null or ThisExpressionSyntax or BaseExpressionSyntax;

    private static IFieldSymbol VolatileField(SemanticModel model, ExpressionSyntax expression) =>
        expression is not null && model.GetSymbolInfo(expression).Symbol is IFieldSymbol { IsVolatile: true } field
            ? field
            : null;

    private static bool IsInsideLock(SyntaxNode node) =>
        node.Ancestors().Any(x => x.IsKind(SyntaxKind.LockStatement));
}
