/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class MigrationAuditColumnShouldUseDateTime2CodeFix : SonarCodeFix
{
    internal const string Title = "Use AsDateTime2";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MigrationAuditColumnShouldUseDateTime2.RuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not SimpleNameSyntax methodName)
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            _ => Task.FromResult(
                context.Document.WithSyntaxRoot(
                    root.ReplaceNode(methodName, SyntaxFactory.IdentifierName("AsDateTime2").WithTriviaFrom(methodName)))),
            context.Diagnostics);
        return Task.CompletedTask;
    }
}
