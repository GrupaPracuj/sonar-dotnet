/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class OpenApiSecuritySchemeIdsShouldMatchCodeFix : SonarCodeFix
{
    internal const string Title = "Match the security scheme definition casing";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OpenApiSecuritySchemeIdsShouldMatch.RuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(OpenApiSecuritySchemeIdsShouldMatch.CanonicalIdProperty, out var canonicalId)
            || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var replacement = SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(canonicalId))
                    .WithTriviaFrom(literal);
                return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(literal, replacement)));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }
}
