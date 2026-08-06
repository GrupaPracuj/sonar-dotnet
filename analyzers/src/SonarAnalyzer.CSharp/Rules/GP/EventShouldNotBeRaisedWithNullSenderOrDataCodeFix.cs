namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class EventShouldNotBeRaisedWithNullSenderOrDataCodeFix : SonarCodeFix
{
    internal const string SenderTitle = "Use 'this' as the sender";
    internal const string DataTitle = "Use 'System.EventArgs.Empty' as the event data";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(EventShouldNotBeRaisedWithNullSenderOrData.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        // getInnermostNodeForTie: true - the null literal and its enclosing ArgumentSyntax share the exact same
        // span (no ref/out/named-argument tokens), so without it FindNode would return the outer, tied node.
        if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not { } nullLiteral
            || !nullLiteral.IsKind(SyntaxKind.NullLiteralExpression)
            || nullLiteral.Parent is not ArgumentSyntax argument
            || argument.Parent is not ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } argumentList)
        {
            return;
        }

        switch (argumentList.Arguments.IndexOf(argument))
        {
            case 0:
                RegisterReplacement(context, root, nullLiteral, SyntaxFactory.ThisExpression(), SenderTitle);
                break;
            case 1 when await IsExactlyEventArgsAsync(invocation, context).ConfigureAwait(false):
                // Registered only when the delegate's second parameter type is exactly System.EventArgs - EventArgs.Empty's
                // static type is EventArgs, which does not implicitly convert to a more-derived custom EventArgs subclass.
                RegisterReplacement(context, root, nullLiteral, SyntaxFactory.ParseExpression("System.EventArgs.Empty"), DataTitle);
                break;
        }
    }

    private static void RegisterReplacement(SonarCodeFixContext context, SyntaxNode root, SyntaxNode nodeToReplace, ExpressionSyntax replacement, string title) =>
        context.RegisterCodeFix(
            title,
            c =>
            {
                var newRoot = root.ReplaceNode(nodeToReplace, replacement.WithTriviaFrom(nodeToReplace));
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

    private static async Task<bool> IsExactlyEventArgsAsync(InvocationExpressionSyntax invocation, SonarCodeFixContext context)
    {
        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        return model is not null
            && EventShouldNotBeRaisedWithNullSenderOrData.ResolveEventSymbol(invocation, model) is { Type: INamedTypeSymbol { DelegateInvokeMethod: { Parameters: { Length: 2 } parameters } } }
            && parameters[1].Type.Is(KnownType.System_EventArgs);
    }
}
