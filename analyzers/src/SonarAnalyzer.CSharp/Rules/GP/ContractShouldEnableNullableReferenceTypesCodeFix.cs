namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class ContractShouldEnableNullableReferenceTypesCodeFix : SonarCodeFix
{
    internal const string Title = "Enable nullable reference types";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ContractShouldEnableNullableReferenceTypes.RuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        context.RegisterCodeFix(
            Title,
            c =>
            {
                // Any later "#nullable" directive in the file only overrides the context from that point on, so
                // inserting an enabling directive at the very top is always safe, regardless of what follows.
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
