/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PropertySetterShouldNotBeMoreAccessibleThanGetter : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0079";

    private const string MessageFormat = "'{0}' has a setter that is more accessible than its getter - narrow the setter or widen the getter.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration);

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        // A set-only (or get-only) member has no accessor pair to compare, and set-only properties are already
        // covered by S2376 - this rule only looks at properties where both accessors exist.
        if (context.Model.GetDeclaredSymbol(context.Node) is not IPropertySymbol { GetMethod: { } getMethod, SetMethod: { } setMethod } property
            || !IsStrictlyWiderThan(setMethod.DeclaredAccessibility, getMethod.DeclaredAccessibility))
        {
            return;
        }

        var location = context.Node switch
        {
            PropertyDeclarationSyntax p => p.Identifier,
            IndexerDeclarationSyntax i => i.ThisKeyword,
            _ => default,
        };
        context.ReportIssue(Rule, location, property.Name);
    }

    // Accessibility in C# is a lattice, not a total order: Protected and Internal are mutually incomparable (only
    // "protected internal" - ProtectedOrInternal - is unambiguously wider than both, and "private protected" -
    // ProtectedAndInternal - is unambiguously narrower than both). Comparing an incomparable pair must never claim
    // "wider", or the rule would false-positive on the common, deliberate internal/protected split.
    internal static bool IsStrictlyWiderThan(Accessibility wider, Accessibility narrower) =>
        wider switch
        {
            Accessibility.Public => narrower != Accessibility.Public,
            Accessibility.ProtectedOrInternal => narrower is Accessibility.Protected or Accessibility.Internal or Accessibility.ProtectedAndInternal or Accessibility.Private,
            Accessibility.Protected => narrower is Accessibility.ProtectedAndInternal or Accessibility.Private,
            Accessibility.Internal => narrower is Accessibility.ProtectedAndInternal or Accessibility.Private,
            Accessibility.ProtectedAndInternal => narrower == Accessibility.Private,
            _ => false,
        };
}
