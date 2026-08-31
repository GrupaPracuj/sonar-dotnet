/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// Finds calls that directly depend on a variable declared by the enclosing loop. Besides avoiding deferred callbacks,
// this excludes retry/polling loops whose request does not change between iterations.
internal static class GpLoopCallHelper
{
    private static readonly HashSet<SyntaxKind> LoopKinds = new()
    {
        SyntaxKind.ForStatement,
        SyntaxKind.ForEachStatement,
        SyntaxKindEx.ForEachVariableStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement
    };

    internal static bool DependsOnDirectLoopVariable(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var loop = invocation.Ancestors().FirstOrDefault(x => LoopKinds.Contains(x.Kind()) || IsScopeBoundary(x));
        if (loop is null || !LoopKinds.Contains(loop.Kind()) || IteratesBatches(loop, model))
        {
            return false;
        }

        var body = LoopBody(loop);
        return invocation.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(x => model.GetSymbolInfo(x).Symbol)
            .OfType<ILocalSymbol>()
            .Any(x => IsDeclaredByLoop(x, loop, body));
    }

    // The classic N+1: one call fetches a collection and the loop over that result issues another call per element.
    // The defect is visible in the method itself, so unlike the synchronous-API-path check this needs no call graph -
    // which is what makes it reach data access that lives in its own assembly, away from the controller.
    internal static bool IteratesFetchedSequence(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var loop = invocation.Ancestors().FirstOrDefault(x => LoopKinds.Contains(x.Kind()) || IsScopeBoundary(x));
        return loop is ForEachStatementSyntax forEach
            && !IteratesBatches(forEach, model)
            && IsFetched(forEach.Expression, model);
    }

    private static bool IsFetched(ExpressionSyntax expression, SemanticModel model)
    {
        switch (expression?.RemoveParentheses())
        {
            case AwaitExpressionSyntax awaited:
                return IsFetched(awaited.Expression, model);
            case InvocationExpressionSyntax call:
                return ChainInvocations(call).Any(x => IsFetchCall(x, model));
            case IdentifierNameSyntax identifier:
                return model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                    && Initializer(local) is { } initializer
                    && IsFetched(initializer, model);
            default:
                return false;
        }
    }

    private static bool IsFetchCall(InvocationExpressionSyntax invocation, SemanticModel model) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && (GpDatabaseCallHelper.IsDatabaseCall(model, invocation, method) || GpHttpCallHelper.IsHttpCall(method));

    private static ExpressionSyntax Initializer(ILocalSymbol local) =>
        local.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .Select(x => x.Initializer?.Value)
            .FirstOrDefault(x => x is not null);

    private static IEnumerable<InvocationExpressionSyntax> ChainInvocations(InvocationExpressionSyntax invocation)
    {
        for (var current = invocation; current is not null;)
        {
            yield return current;
            current = current.Expression is MemberAccessExpressionSyntax { Expression: { } receiver }
                ? receiver.RemoveParentheses() as InvocationExpressionSyntax
                    ?? (receiver.RemoveParentheses() as AwaitExpressionSyntax)?.Expression.RemoveParentheses() as InvocationExpressionSyntax
                : null;
        }
    }

    private static bool IsDeclaredByLoop(ILocalSymbol local, SyntaxNode loop, StatementSyntax body) =>
        local.DeclaringSyntaxReferences
            .Select(x => x.GetSyntax())
            .Any(x => x.AncestorsAndSelf().Contains(loop)
                      && (body is null || !x.AncestorsAndSelf().Contains(body)));

    private static StatementSyntax LoopBody(SyntaxNode loop) =>
        loop switch
        {
            ForStatementSyntax x => x.Statement,
            ForEachStatementSyntax x => x.Statement,
            WhileStatementSyntax x => x.Statement,
            DoStatementSyntax x => x.Statement,
            _ => loop.ChildNodes().OfType<StatementSyntax>().LastOrDefault()
        };

    // One call per batch is the remedy this rule steers towards, so a loop whose element is itself a collection -
    // Chunk(), GroupBy() or a hand-rolled batcher yielding List<T> - must not be reported. Strings are enumerable
    // over their characters, which would otherwise read as a batch.
    private static bool IteratesBatches(SyntaxNode loop, SemanticModel model) =>
        loop is ForEachStatementSyntax forEach
        && model.GetForEachStatementInfo(forEach).ElementType is { } elementType
        && elementType.SpecialType != SpecialType.System_String
        && (elementType is IArrayTypeSymbol
            || elementType.AllInterfaces.Any(x => x.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T));

    private static bool IsScopeBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or MemberDeclarationSyntax or AccessorDeclarationSyntax
        || node.Kind() == SyntaxKindEx.LocalFunctionStatement;
}
