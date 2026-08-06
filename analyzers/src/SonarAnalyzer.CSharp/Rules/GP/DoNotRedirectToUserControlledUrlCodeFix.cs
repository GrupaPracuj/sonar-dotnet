namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class DoNotRedirectToUserControlledUrlCodeFix : SonarCodeFix
{
    internal const string Title = "Use LocalRedirect";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DoNotRedirectToUserControlledUrl.RuleId);

    private static readonly Dictionary<string, string> LocalCounterparts = new(StringComparer.Ordinal)
    {
        ["Redirect"] = "LocalRedirect",
        ["RedirectPermanent"] = "LocalRedirectPermanent",
        ["RedirectPreserveMethod"] = "LocalRedirectPreserveMethod",
        ["RedirectPermanentPreserveMethod"] = "LocalRedirectPermanentPreserveMethod",
    };

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation
            || invocation.Expression is not IdentifierNameSyntax identifier
            || !LocalCounterparts.TryGetValue(identifier.Identifier.ValueText, out var localName))
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newIdentifier = SyntaxFactory.IdentifierName(localName).WithTriviaFrom(identifier);
                var newRoot = root.ReplaceNode(identifier, newIdentifier);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }
}
