/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class GetCollectionEndpointsShouldNotReturnNoContentCodeFix : SonarCodeFix
{
    internal const string Title = "Return 200 with an empty collection";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(GetCollectionEndpointsShouldNotReturnNoContent.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model is not null && GpMinimalApi.TryGetResultMethod(model, invocation, out _))
        {
            // The empty collection type must match the Minimal API handler's result union, so no universal fix is safe.
            return;
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
    }
}
