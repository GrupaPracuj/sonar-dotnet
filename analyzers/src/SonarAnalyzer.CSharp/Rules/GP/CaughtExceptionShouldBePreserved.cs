/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CaughtExceptionShouldBePreserved : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0105";

    private const string MessageFormat = "Preserve '{0}' as the inner exception when wrapping it.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);

    private static void AnalyzeCatchClause(SonarSyntaxNodeReportingContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Declaration is not { Identifier.RawKind: not 0 } declaration
            || context.Model.GetDeclaredSymbol(declaration) is not ILocalSymbol caughtException)
        {
            return;
        }

        foreach (var throwStatement in catchClause.Block
                     .DescendantNodes(DoesNotEnterNestedScope)
                     .OfType<ThrowStatementSyntax>())
        {
            if (ObjectCreationFactory.TryCreate(throwStatement.Expression?.RemoveParentheses()) is not { } creation
                || creation.TypeSymbol(context.Model) is not { } createdType
                || !GpJunoTypes.DerivesFrom(createdType, "System.Exception")
                || !References(context.Model, creation.Expression, caughtException)
                || creation.ArgumentList?.Arguments.Any(x => IsCaughtExceptionValue(context.Model, x.Expression, caughtException)) == true)
            {
                continue;
            }

            context.ReportIssue(Rule, creation.Expression, caughtException.Name);
        }
    }

    private static bool DoesNotEnterNestedScope(SyntaxNode node) =>
        node is not CatchClauseSyntax
        && node is not AnonymousFunctionExpressionSyntax
        && node.Kind() != SyntaxKindEx.LocalFunctionStatement;

    private static bool References(SemanticModel model, SyntaxNode node, ISymbol symbol) =>
        node.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(x => symbol.Equals(model.GetSymbolInfo(x).Symbol));

    private static bool IsCaughtExceptionValue(SemanticModel model, ExpressionSyntax expression, ISymbol caughtException)
    {
        expression = (ExpressionSyntax)expression.RemoveParentheses();
        return expression switch
        {
            IdentifierNameSyntax identifier =>
                caughtException.Equals(model.GetSymbolInfo(identifier).Symbol),
            CastExpressionSyntax cast => IsCaughtExceptionValue(model, cast.Expression, caughtException),
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKindEx.SuppressNullableWarningExpression) =>
                IsCaughtExceptionValue(model, postfix.Operand, caughtException),
            _ => false,
        };
    }
}
