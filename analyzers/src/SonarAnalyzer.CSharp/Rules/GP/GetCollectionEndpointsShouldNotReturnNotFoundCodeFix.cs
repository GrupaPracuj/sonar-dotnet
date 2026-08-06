namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class GetCollectionEndpointsShouldNotReturnNotFoundCodeFix : SonarCodeFix
{
    internal const string Title = "Return 200 with an empty collection";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(GetCollectionEndpointsShouldNotReturnNotFound.RuleId);

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
                var replacement = SyntaxFactory.ParseExpression("Ok(System.Array.Empty<object>())").WithTriviaFrom(invocation);
                var newRoot = root.ReplaceNode(invocation, replacement);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }
}
