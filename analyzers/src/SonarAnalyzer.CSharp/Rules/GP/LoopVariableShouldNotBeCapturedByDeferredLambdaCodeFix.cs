using Microsoft.CodeAnalysis.Formatting;

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class LoopVariableShouldNotBeCapturedByDeferredLambdaCodeFix : SonarCodeFix
{
    internal const string Title = "Copy the loop variable to a local before capturing it";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(LoopVariableShouldNotBeCapturedByDeferredLambda.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var span = diagnostic.Location.SourceSpan;

        if (root.FindNode(span, getInnermostNodeForTie: true) is not AnonymousFunctionExpressionSyntax lambda
            || lambda.FirstAncestorOrSelf<ForStatementSyntax>() is not ForStatementSyntax { Declaration: { } declaration }
            || lambda.FirstAncestorOrSelf<StatementSyntax>() is not { } containingStatement
            || containingStatement.Parent is not BlockSyntax block)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model is null)
        {
            return;
        }

        var loopVariables = LoopVariableShouldNotBeCapturedByDeferredLambda.LoopVariables(model, declaration);
        if (LoopVariableShouldNotBeCapturedByDeferredLambda.CapturedLoopVariable(model, lambda, loopVariables) is not { } capturedSymbol)
        {
            return;
        }

        var newName = UniqueCopyName(model, containingStatement, capturedSymbol.Name);

        context.RegisterCodeFix(
            Title,
            _ =>
            {
                var newLambda = lambda.ReplaceNodes(
                    lambda.DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Where(x => capturedSymbol.Equals(model.GetSymbolInfo(x).Symbol)),
                    (original, _) => SyntaxFactory.IdentifierName(newName).WithTriviaFrom(original));

                var newContainingStatement = containingStatement.ReplaceNode(lambda, newLambda);

                var copyDeclaration = SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                            .WithVariables(SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(newName))
                                    .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.IdentifierName(capturedSymbol.Name))))))
                    .WithLeadingTrivia(containingStatement.GetLeadingTrivia())
                    .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed)
                    .WithAdditionalAnnotations(Formatter.Annotation);

                var statementIndex = block.Statements.IndexOf(containingStatement);
                var newStatements = block.Statements.Replace(containingStatement, newContainingStatement).Insert(statementIndex, copyDeclaration);
                var newBlock = block.WithStatements(newStatements);
                var newRoot = root.ReplaceNode(block, newBlock);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }

    // "<name>Copy", falling back to "<name>Copy2", "<name>Copy3", ... if a symbol with that name is already visible at this point.
    private static string UniqueCopyName(SemanticModel model, StatementSyntax containingStatement, string capturedName)
    {
        var candidate = capturedName + "Copy";
        for (var suffix = 2; model.LookupSymbols(containingStatement.SpanStart, name: candidate).Any(); suffix++)
        {
            candidate = capturedName + "Copy" + suffix;
        }

        return candidate;
    }
}
