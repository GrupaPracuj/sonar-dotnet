namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class AuthResponseShouldMatchAuthCheckCodeFix : SonarCodeFix
{
    internal const string Title = "Return the matching status code";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AuthResponseShouldMatchAuthCheck.RuleId);

    protected override Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation
            || DetermineReplacement(invocation) is not { } replacementCall)
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var replacement = SyntaxFactory.ParseExpression(replacementCall).WithTriviaFrom(invocation);
                var newRoot = root.ReplaceNode(invocation, replacement);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);

        return Task.CompletedTask;
    }

    private static string DetermineReplacement(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => string.Empty
        };

        return name switch
        {
            "Unauthorized" => "Forbid()",
            "Forbid" => "Unauthorized()",
            "StatusCode" when IsStatusCodeArg(invocation, 401) => "Forbid()",
            "StatusCode" when IsStatusCodeArg(invocation, 403) => "Unauthorized()",
            _ => null
        };
    }

    private static bool IsStatusCodeArg(InvocationExpressionSyntax invocation, int expected) =>
        invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
        && literal.Token.Value is int value
        && value == expected;
}
