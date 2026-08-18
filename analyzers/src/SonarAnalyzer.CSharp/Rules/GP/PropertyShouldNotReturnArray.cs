/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// Attribute properties are exempt (an attribute constructor argument can only be an array, never a collection, so
// there is no alternative shape to move to), as are byte arrays used for binary payloads and message contracts.
// 'override' is excluded too: the property's shape is dictated by the base member, so the
// finding would be unfixable at that site.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PropertyShouldNotReturnArray : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0099";

    private const string MessageFormat = "'{0}' returns an array - callers can mutate it through this property. Return a read-only collection, or a method that returns a copy.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contracts = GpSemanticContractDetector.GetOrCreate(start.Compilation);
            start.RegisterNodeAction(c => Analyze(c, contracts), SyntaxKind.PropertyDeclaration);
        });

    private static void Analyze(SonarSyntaxNodeReportingContext context, GpSemanticContractDetector contracts)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (declaration.Modifiers.Any(SyntaxKind.OverrideKeyword)
            || context.Model.GetDeclaredSymbol(declaration) is not { } property
            || property.GetMethod?.EffectiveAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
            || property.SetMethod?.EffectiveAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal
            || property.Type is not IArrayTypeSymbol arrayType
            || arrayType is { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte }
            || IsExcludedContainingType(property.ContainingType, contracts))
        {
            return;
        }

        context.ReportIssue(Rule, declaration.Identifier, property.Name);
    }

    private static bool IsExcludedContainingType(INamedTypeSymbol containingType, GpSemanticContractDetector contracts) =>
        containingType.DerivesFrom(KnownType.System_Attribute) || contracts.IsContract(containingType);
}
