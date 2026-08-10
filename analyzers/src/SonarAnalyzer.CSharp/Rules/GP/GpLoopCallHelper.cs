namespace SonarAnalyzer.CSharp.Rules;

// Finds whether a call runs directly and synchronously inside a loop, once per iteration - not merely inside a loop somewhere in its ancestry.
// The ancestor walk stops at the first lambda/local function/member boundary it meets, so a call handed to deferred execution (a callback stored
// for later, a local function invoked once after the loop, a nested lambda passed to LINQ) is never attributed to the outer loop.
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

    internal static bool IsDirectlyInsideLoop(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(x => LoopKinds.Contains(x.Kind()) || IsScopeBoundary(x)) is { } ancestor
        && LoopKinds.Contains(ancestor.Kind());

    private static bool IsScopeBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or MemberDeclarationSyntax or AccessorDeclarationSyntax
        || node.Kind() == SyntaxKindEx.LocalFunctionStatement;
}
