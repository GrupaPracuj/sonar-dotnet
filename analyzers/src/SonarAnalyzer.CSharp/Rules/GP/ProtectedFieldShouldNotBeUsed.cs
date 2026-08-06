namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProtectedFieldShouldNotBeUsed : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0081";

    private const string MessageFormat = "'{0}' should not have protected accessibility - use a protected property instead.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);

    private static void AnalyzeField(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (FieldDeclarationSyntax)context.Node;
        // Only the plain "protected" modifier is in scope - "protected internal" (wider) and "private protected"
        // (narrower) are each a different accessibility this guideline does not warn about. Static fields, and
        // fields marked readonly or const, share S1104's exact exception list for the public case.
        if (!IsPlainProtected(declaration.Modifiers)
            || declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
            || declaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
            || declaration.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return;
        }

        foreach (var variable in declaration.Declaration.Variables)
        {
            if (context.Model.GetDeclaredSymbol(variable) is IFieldSymbol field && !IsExempted(field))
            {
                context.ReportIssue(Rule, variable.Identifier, variable.Identifier.ValueText);
            }
        }
    }

    private static bool IsPlainProtected(SyntaxTokenList modifiers) =>
        modifiers.Any(SyntaxKind.ProtectedKeyword) && !modifiers.Any(SyntaxKind.InternalKeyword) && !modifiers.Any(SyntaxKind.PrivateKeyword);

    // Mirrors S1104's exception list exactly, since the same false-positive concerns apply identically to
    // protected fields: a field inside a [StructLayout] type, and a field inside a [Serializable] type unless the
    // field itself is also marked [NonSerialized].
    private static bool IsExempted(IFieldSymbol field) =>
        field.ContainingType is { } containingType
        && (containingType.HasAttribute(KnownType.System_Runtime_InteropServices_StructLayoutAttribute) || Serializable(field, containingType));

    private static bool Serializable(IFieldSymbol field, INamedTypeSymbol containingType) =>
        containingType.HasAttribute(KnownType.System_SerializableAttribute) && !field.HasAttribute(KnownType.System_NonSerializedAttribute);
}
