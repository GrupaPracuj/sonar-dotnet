namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class HttpCallShouldPropagateCancellationTokenCodeFix : SonarCodeFix
{
    internal const string Title = "Pass the CancellationToken";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(HttpCallShouldPropagateCancellationToken.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } methodDeclaration
            || model?.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol method
            || method.Parameters.FirstOrDefault(x => x.Type.Is(KnownType.System_Threading_CancellationToken)) is not { } tokenParameter)
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newArgument = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenParameter.Name));
                var newArgumentList = invocation.ArgumentList.AddArguments(newArgument);
                var newInvocation = invocation.WithArgumentList(newArgumentList);
                var newRoot = root.ReplaceNode(invocation, newInvocation);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }
}
