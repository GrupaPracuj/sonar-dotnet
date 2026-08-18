/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[ExportCodeFixProvider(LanguageNames.CSharp)]
public sealed class DoNotRedirectToUserControlledUrlCodeFix : SonarCodeFix
{
    internal const string Title = "Use LocalRedirect";
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DoNotRedirectToUserControlledUrl.RuleId);

    private static readonly Dictionary<string, string> LocalCounterparts = new(StringComparer.Ordinal)
    {
        ["Redirect"] = "LocalRedirect",
        ["RedirectPermanent"] = "LocalRedirectPermanent",
        ["RedirectPreserveMethod"] = "LocalRedirectPreserveMethod",
        ["RedirectPermanentPreserveMethod"] = "LocalRedirectPermanentPreserveMethod",
    };
    protected override async Task RegisterCodeFixesAsync(SyntaxNode root, SonarCodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (diagnostic.Properties.ContainsKey(DoNotRedirectToUserControlledUrl.MinimalApiDiagnosticProperty)
            || root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation
            || invocation.Expression is not IdentifierNameSyntax identifier
            || !LocalCounterparts.TryGetValue(identifier.Identifier.ValueText, out var localName))
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.Cancel).ConfigureAwait(false);
        if (model?.GetSymbolInfo(invocation, context.Cancel).Symbol is not IMethodSymbol method
            || !DoNotRedirectToUserControlledUrl.IsMvcRedirectMethod(method))
        {
            return;
        }

        context.RegisterCodeFix(
            Title,
            c =>
            {
                var newIdentifier = SyntaxFactory.IdentifierName(localName).WithTriviaFrom(identifier);
                var newRoot = root.ReplaceNode(identifier, newIdentifier);
                return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
            },
            context.Diagnostics);
    }
}
