using Microsoft.CodeAnalysis.Text;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbrakadabraWord : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0001";

    private const string Keyword = "abrakadabra";
    private const string MessageFormat = "Remove the word 'abrakadabra' from the code.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterTreeAction(c =>
        {
            // Raw text rather than syntax, so the word is found in identifiers, string literals and comments alike.
            var text = c.Tree.GetText(c.Cancel);
            foreach (var line in text.Lines)
            {
                var lineText = line.ToString();
                var index = lineText.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    c.ReportIssue(Rule, Location.Create(c.Tree, new TextSpan(line.Start + index, Keyword.Length)));
                }
            }
        });
}
