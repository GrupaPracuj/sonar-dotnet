namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class AuthResponseShouldMatchAuthCheckCodeFix : SonarCodeFix
{
    internal const string Title = "Return the matching status code";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AuthResponseShouldMatchAuthCheck.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation
            || ReplacementName(invocation) is not { } replacementName)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        // A handler declared as a Results<...> union only accepts the exact result types it lists, so swapping
        // TypedResults.Unauthorized() for TypedResults.Forbid() there would stop compiling. The mismatch still needs
        // fixing, but not by this mechanical replacement.
        if (model is null || IsInsideTypedResultUnion(invocation, model))
        {
            return;
        }

        // The receiver has to be preserved: the reported call may be Results.Unauthorized() or TypedResults.Forbid(),
        // where a bare "Forbid()" does not exist at all. Only the invoked name changes, and any status-code argument
        // goes away with it.
        var newExpression = invocation.Expression switch
        {
            IdentifierNameSyntax => (ExpressionSyntax)SyntaxFactory.IdentifierName(replacementName),
            MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(SyntaxFactory.IdentifierName(replacementName)),
            _ => null,
        };
        if (newExpression is null)
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var replacement = invocation
                    .WithExpression(newExpression)
                    .WithArgumentList(SyntaxFactory.ArgumentList())
                    .WithTriviaFrom(invocation);
                var newRoot = root.ReplaceNode(invocation, replacement);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }

    private static string ReplacementName(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => string.Empty
        };

        return name switch
        {
            "Unauthorized" => "Forbid",
            "Forbid" => "Unauthorized",
            "StatusCode" when IsStatusCodeArg(invocation, 401) => "Forbid",
            "StatusCode" when IsStatusCodeArg(invocation, 403) => "Unauthorized",
            _ => null
        };
    }

    private static bool IsStatusCodeArg(InvocationExpressionSyntax invocation, int expected) =>
        invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
        && literal.Token.Value is int value
        && value == expected;

    private static bool IsInsideTypedResultUnion(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var handler = invocation.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        var returnType = handler is not null
            ? (model.GetTypeInfo(handler).ConvertedType as INamedTypeSymbol)?.DelegateInvokeMethod?.ReturnType
            : (model.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol)?.ReturnType;
        return returnType is INamedTypeSymbol namedType
               && namedType.Name == "Results"
               && namedType.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Http.HttpResults";
    }
}
