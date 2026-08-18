/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class DictionaryLookupShouldUseTryAddCodeFix : SonarCodeFix
{
    internal const string Title = "Replace with 'TryAdd'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DictionaryLookupShouldUseTryAdd.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();

        if (root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<IfStatementSyntax>() is not { } ifStatement)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model is null || !DictionaryLookupShouldUseTryAdd.TryGetTryAddParts(ifStatement, model, out var dictionary, out var key, out var value))
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                // NormalizeWhitespace gives the newly-built invocation itself normal single-line spacing ("key, value")
                // without touching the surrounding tree; the original if statement's own leading/trailing trivia
                // (indentation, and any trailing "// Noncompliant" comment on the same line) is then reattached so the
                // replacement reads as a plain, in-place statement rather than a freshly-formatted foreign fragment.
                var tryAddInvocation = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, dictionary.WithoutTrivia(), SyntaxFactory.IdentifierName("TryAdd")),
                        SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Argument(key.WithoutTrivia()), SyntaxFactory.Argument(value.WithoutTrivia()) })))
                    .NormalizeWhitespace();
                var tryAdd = SyntaxFactory.ExpressionStatement(tryAddInvocation)
                    .WithLeadingTrivia(ifStatement.GetLeadingTrivia())
                    .WithTrailingTrivia(ifStatement.GetTrailingTrivia());
                var newRoot = root.ReplaceNode(ifStatement, tryAdd);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }
}
