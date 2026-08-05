namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldEnableNullableReferenceTypes : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0063";

    private const string MessageFormat =
        "'{0}' is declared without nullable reference types, so its members do not say which values are optional.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeTypeDeclaration, SyntaxKind.ClassDeclaration, SyntaxKindEx.RecordDeclaration, SyntaxKind.StructDeclaration);

    private static void AnalyzeTypeDeclaration(SonarSyntaxNodeReportingContext context)
    {
        if (context.Node is not TypeDeclarationSyntax { Identifier: var identifier } declaration
            || !GpMessageContracts.HasContractName(identifier.ValueText)
            || AnnotationsEnabled(context.Model, declaration))
        {
            return;
        }

        context.ReportIssue(Rule, identifier, identifier.ValueText);
    }

    // Asks the semantic model for the context at the declaration, which accounts for the project-level setting and a
    // per-file "#nullable enable" alike. Only annotations matter - warnings may stay off.
    private static bool AnnotationsEnabled(SemanticModel model, TypeDeclarationSyntax declaration) =>
        model.GetNullableContext(declaration.SpanStart).HasFlag(NullableContext.AnnotationsEnabled);
}
