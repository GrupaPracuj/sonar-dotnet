namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class DeleteEndpointsShouldNotReturnContentCodeFix : SonarCodeFix
{
    internal const string Title = "Return 204 (NoContent)";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DeleteEndpointsShouldNotReturnContent.RuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation)
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var replacement = SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("NoContent")).WithTriviaFrom(invocation);
                var newRoot = root.ReplaceNode(invocation, replacement);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }
}
