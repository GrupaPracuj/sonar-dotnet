using Microsoft.CodeAnalysis.Rename;

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class IdentifierShouldUseStandardCompoundWordSpellingCodeFix : SonarCodeFix
{
    internal const string Title = "Use the standard spelling";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(IdentifierShouldUseStandardCompoundWordSpelling.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        if (token.Parent is not { } declaringNode)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(declaringNode) is not { } symbol)
        {
            return;
        }

        if (!GpIdentifierWords.TryFixCompoundWord(symbol.Name, out var newName) || string.IsNullOrEmpty(newName) || newName == symbol.Name)
        {
            return;
        }

        // Renaming a parameter of an override or of an interface implementation would make its name disagree with the
        // base/interface declaration - the very thing S927 reports - so no fix is offered there. The misspelling is
        // still reported, and is meant to be fixed on the base declaration.
        if (symbol is IParameterSymbol parameter && !IdentifierShouldUseStandardCompoundWordSpelling.ParameterIsFreelyRenamable(parameter))
        {
            return;
        }

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
