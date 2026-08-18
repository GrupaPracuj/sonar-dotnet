/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using Microsoft.CodeAnalysis.Formatting;

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class CancellationShouldNotBeSuppressedCodeFix : SonarCodeFix
{
    internal const string Title = "Rethrow the cancellation";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(CancellationShouldNotBeSuppressed.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        if (root.FindNode(diagnosticSpan).FirstAncestorOrSelf<CatchClauseSyntax>() is not { Block: { } block } catchClause)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model is null || !model.AnalyzeControlFlow(block).EndPointIsReachable)
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var throwStatement = SyntaxFactory.ThrowStatement().WithAdditionalAnnotations(Formatter.Annotation);
                var newBlock = block.WithStatements(block.Statements.Add(throwStatement));
                var newRoot = root.ReplaceNode(block, newBlock);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }
}
