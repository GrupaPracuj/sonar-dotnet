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
        if (loop is null || !LoopKinds.Contains(loop.Kind()))
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

    private static bool IsScopeBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or MemberDeclarationSyntax or AccessorDeclarationSyntax
        || node.Kind() == SyntaxKindEx.LocalFunctionStatement;
}
