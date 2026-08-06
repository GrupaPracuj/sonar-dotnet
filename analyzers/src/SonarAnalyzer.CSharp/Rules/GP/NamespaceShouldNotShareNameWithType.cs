namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceShouldNotShareNameWithType : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0076";

    private const string MessageFormat = "Namespace '{0}' should not share a name with the type '{0}' declared inside it.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.NamespaceDeclaration, SyntaxKindEx.FileScopedNamespaceDeclaration);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var namespaceDeclaration = (BaseNamespaceDeclarationSyntaxWrapper)context.Node;
        if (LastSegment(namespaceDeclaration.Name) is not { } lastSegment)
        {
            return;
        }

        var namespaceName = lastSegment.Identifier.ValueText;
        // Only direct members of the namespace block are considered: a same-named type nested inside another
        // type declared in this namespace is not a direct member, and several compilers only struggle with the
        // direct case, so it is deliberately not reported.
        if (namespaceDeclaration.Members.Any(x => MemberIdentifier(x) is { } identifier && identifier.ValueText == namespaceName))
        {
            context.ReportIssue(Rule, lastSegment, namespaceName);
        }
    }

    // For "Fabrikam.Debug" the Name is a QualifiedNameSyntax whose Right is the "Debug" SimpleNameSyntax; for a
    // single-segment namespace like "Debug" the Name is already the SimpleNameSyntax itself.
    private static SimpleNameSyntax LastSegment(NameSyntax name) =>
        name switch
        {
            QualifiedNameSyntax qualified => qualified.Right,
            SimpleNameSyntax simple => simple,
            _ => null
        };

    private static SyntaxToken? MemberIdentifier(MemberDeclarationSyntax member) =>
        member switch
        {
            BaseTypeDeclarationSyntax baseType => baseType.Identifier, // class, struct, interface, enum
            DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Identifier,
            _ when RecordDeclarationSyntaxWrapper.IsInstance(member) => ((RecordDeclarationSyntaxWrapper)member).Identifier,
            _ => null
        };
}
