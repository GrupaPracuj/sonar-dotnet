using Microsoft.CodeAnalysis.Rename;

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class DateOnlyPropertyShouldNotBeNamedUtcCodeFix : SonarCodeFix
{
    internal const string Title = "Remove 'Utc' from the name";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DateOnlyPropertyShouldNotBeNamedUtc.RuleId);

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

        var newName = RemoveUtcWord(symbol.Name);
        if (string.IsNullOrEmpty(newName) || newName == symbol.Name)
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

    private static string RemoveUtcWord(string identifier)
    {
        var words = GpIdentifierWords.SplitWords(identifier).Where(w => !w.Equals("Utc", StringComparison.OrdinalIgnoreCase)).ToList();
        var result = string.Concat(words);
        if (result.Length == 0)
        {
            return identifier;
        }

        if (identifier.Length > 0 && char.IsLower(identifier[0]) && char.IsUpper(result[0]))
        {
            result = char.ToLowerInvariant(result[0]) + result.Substring(1);
        }

        return result;
    }
}
