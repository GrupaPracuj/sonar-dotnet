/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using Microsoft.CodeAnalysis.Rename;

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class TypeShouldEndWithStreamOrDictionaryCodeFix : SonarCodeFix
{
    internal const string Title = "Append the missing suffix";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(TypeShouldEndWithStreamOrDictionary.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        if (token.Parent is not { } declaringNode)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(declaringNode) is not INamedTypeSymbol symbol
            || TypeShouldEndWithStreamOrDictionary.MissingSuffix(symbol) is not { } suffix)
        {
            return;
        }

        var newName = symbol.Name + suffix;

        context.RegisterCodeFix(
            Title,
            async c =>
            {
                var newSolution = await Renamer.RenameSymbolAsync(context.Document.Project.Solution, symbol, newName, optionSet: null, c).ConfigureAwait(false);
                return newSolution;
            },
            context.Diagnostics);
    }
}
