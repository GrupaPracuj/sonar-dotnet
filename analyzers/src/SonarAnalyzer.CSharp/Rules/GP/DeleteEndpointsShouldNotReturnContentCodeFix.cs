/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class DeleteEndpointsShouldNotReturnContentCodeFix : SonarCodeFix
{
    internal const string Title = "Return 204 (NoContent)";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DeleteEndpointsShouldNotReturnContent.RuleId);

    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation)
        {
            return;
        }
        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        var isMinimalApiResult = model is not null && GpMinimalApi.TryGetResultMethod(model, invocation, out _);
        if (isMinimalApiResult && IsInsideTypedResultUnion(invocation, model))
        {
            return;
        }

        var minimalApiReplacement = isMinimalApiResult && invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? invocation.WithExpression(memberAccess.WithName(SyntaxFactory.IdentifierName("NoContent")))
                .WithArgumentList(SyntaxFactory.ArgumentList())
                .WithTriviaFrom(invocation)
            : null;
        if (isMinimalApiResult && minimalApiReplacement is null)
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var replacement = minimalApiReplacement
                                  ?? SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("NoContent")).WithTriviaFrom(invocation);
                var newRoot = root.ReplaceNode(invocation, replacement);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }

    private static bool IsInsideTypedResultUnion(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var handler = invocation.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        return handler is not null
               && model.GetTypeInfo(handler).ConvertedType is INamedTypeSymbol { DelegateInvokeMethod.ReturnType: INamedTypeSymbol returnType }
               && returnType.Name == "Results"
               && returnType.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Http.HttpResults";
    }
}
