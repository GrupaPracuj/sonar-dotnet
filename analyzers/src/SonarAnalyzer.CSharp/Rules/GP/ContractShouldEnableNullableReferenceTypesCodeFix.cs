namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class ContractShouldEnableNullableReferenceTypesCodeFix : SonarCodeFix
{
    internal const string Title = "Enable nullable reference types";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ContractShouldEnableNullableReferenceTypes.RuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var declaration = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>();
        context.RegisterCodeFix(
            Title,
            c =>
            {
                var disablingDirective = root.DescendantTrivia(descendIntoTrivia: true)
                    .Where(x => x.SpanStart < (declaration?.SpanStart ?? diagnostic.Location.SourceSpan.Start))
                    .LastOrDefault(x => x.GetStructure() is { } structure
                                        && NullableDirectiveTriviaSyntaxWrapper.IsInstance(structure)
                                        && ((NullableDirectiveTriviaSyntaxWrapper)structure).SettingToken.ValueText == "disable");
                if (disablingDirective.RawKind != 0)
                {
                    var text = disablingDirective.ToFullString();
                    var disableOffset = text.IndexOf("disable", StringComparison.Ordinal);
                    var enabledText = text.Substring(0, disableOffset) + "enable" + text.Substring(disableOffset + "disable".Length);
                    var enabledDirective = SyntaxFactory.ParseLeadingTrivia(enabledText).First(x => x.HasStructure);
                    return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceTrivia(disablingDirective, enabledDirective)));
                }

                var firstToken = root.GetFirstToken(includeZeroWidth: true);
                var newLeadingTrivia = SyntaxFactory.ParseLeadingTrivia("#nullable enable\n\n").AddRange(firstToken.LeadingTrivia);
                var newFirstToken = firstToken.WithLeadingTrivia(newLeadingTrivia);
                var newRoot = root.ReplaceToken(firstToken, newFirstToken);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }
}
