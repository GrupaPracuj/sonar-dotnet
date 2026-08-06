namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class MessageContractMustBePublicCodeFix : SonarCodeFix
{
    internal const string Title = "Make the contract public";

    private static readonly HashSet<SyntaxKind> AccessibilityKeywords = new()
    {
        SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword,
    };

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(MessageContractMustBePublic.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);

        // The fix target - the contract type's own declaration - is a different node than the one reported (the
        // publishing invocation, or the consumer class). It can in theory live in another file; that is only safe
        // to fix when it is declared in this same syntax tree.
        if (model is null
            || ResolveContractType(node, model) is not { } contractType
            || contractType.DeclaringSyntaxReferences.FirstOrDefault(x => x.SyntaxTree == root.SyntaxTree) is not { } reference
            || await reference.GetSyntaxAsync(context.Cancel).ConfigureAwait(false) is not TypeDeclarationSyntax typeDeclaration)
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newDeclaration = MakePublic(typeDeclaration);
                var newRoot = root.ReplaceNode(typeDeclaration, newDeclaration);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }

    private static ITypeSymbol ResolveContractType(SyntaxNode node, SemanticModel model) =>
        node switch
        {
            InvocationExpressionSyntax invocation when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method =>
                method.TypeArguments.FirstOrDefault()
                ?? (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } argument ? model.GetTypeInfo(argument).Type : null),
            ClassDeclarationSyntax classDeclaration when model.GetDeclaredSymbol(classDeclaration) is { } type =>
                type.AllInterfaces
                    .Where(GpMessageContracts.IsConsumerInterface)
                    .Select(x => x.TypeArguments[0])
                    .FirstOrDefault(x => x.DeclaredAccessibility != Accessibility.Public && x.ContainingAssembly?.Name == model.Compilation.AssemblyName),
            _ => null,
        };

    // Swaps whatever accessibility the type declaration has (or its absence, which C# treats as internal or
    // private by default) for "public", carrying the original indentation onto the new token.
    // TypeDeclarationSyntax has no WithModifiers (that is generated per derived class, and this project's
    // compile-time Roslyn reference predates the concrete RecordDeclarationSyntax type), so both branches stay on
    // the generic, node-type-agnostic SyntaxNode.ReplaceToken overloads instead - one token in, one or two out.
    private static TypeDeclarationSyntax MakePublic(TypeDeclarationSyntax typeDeclaration)
    {
        if (typeDeclaration.Modifiers.FirstOrDefault(x => AccessibilityKeywords.Contains(x.Kind())) is { RawKind: not 0 } existingAccessibility)
        {
            var publicToken = SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTriviaFrom(existingAccessibility);
            return typeDeclaration.ReplaceToken(existingAccessibility, publicToken);
        }

        var anchor = typeDeclaration.Modifiers.Count > 0 ? typeDeclaration.Modifiers[0] : typeDeclaration.Keyword;
        var newPublicToken = SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithLeadingTrivia(anchor.LeadingTrivia).WithTrailingTrivia(SyntaxFactory.Space);
        var strippedAnchor = anchor.WithLeadingTrivia(SyntaxTriviaList.Empty);
        return typeDeclaration.ReplaceToken(anchor, new[] { newPublicToken, strippedAnchor });
    }
}
