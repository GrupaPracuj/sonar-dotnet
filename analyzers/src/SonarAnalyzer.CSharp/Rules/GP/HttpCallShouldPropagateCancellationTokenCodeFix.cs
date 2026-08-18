/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

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
        if (model is null
            || HttpCallShouldPropagateCancellationToken.AvailableCancellationToken(model, invocation) is not { } tokenParameter
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol invokedMethod
            || HttpCallShouldPropagateCancellationToken.CancellationTokenParameter(invokedMethod) is not { } targetParameter)
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var replacement = SyntaxFactory.IdentifierName(tokenParameter.Name);
                var existingArgument = HttpCallShouldPropagateCancellationToken.CancellationTokenArgument(invocation, invokedMethod);
                var newArgumentList = existingArgument is not null
                    ? invocation.ArgumentList.ReplaceNode(existingArgument, existingArgument.WithExpression(replacement))
                    : invocation.ArgumentList.AddArguments(
                        SyntaxFactory.Argument(replacement)
                            .WithNameColon(SyntaxFactory.NameColon(targetParameter.Name)));
                var newInvocation = invocation.WithArgumentList(newArgumentList);
                var newRoot = root.ReplaceNode(invocation, newInvocation);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }
}
