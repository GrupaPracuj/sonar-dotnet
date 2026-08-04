namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractShouldNotExposeNonSerializableMembers : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0025";

    private const string MessageFormat = "'{0}' has type '{1}', which does not serialize to JSON meaningfully - remove it from this contract.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // Only types whose own name ends with one of these are treated as wire contracts - this rule does not try to
    // trace which classes are actually reachable from a controller action or a message declaration.
    private static readonly string[] ContractNameSuffixes = { "Dto", "Request", "Response", "Contract" };

    private static readonly HashSet<string> BannedTypes = new(StringComparer.Ordinal)
    {
        "System.IO.Stream",
        "System.Threading.Tasks.Task",
        "System.IntPtr",
        "System.UIntPtr",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
    }

    private static void AnalyzeProperty(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (IsContractMember(declaration)
            && BannedType(context.Model, declaration.Type) is { } typeName)
        {
            context.ReportIssue(Rule, declaration, declaration.Identifier.ValueText, typeName);
        }
    }

    private static void AnalyzeField(SonarSyntaxNodeReportingContext context)
    {
        var declaration = (FieldDeclarationSyntax)context.Node;
        if (!IsContractMember(declaration)
            || declaration.Modifiers.All(x => !x.IsKind(SyntaxKind.PublicKeyword))
            // A static or const field is not part of the serialized instance, so it is not part of the contract.
            || declaration.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword) || x.IsKind(SyntaxKind.ConstKeyword))
            || BannedType(context.Model, declaration.Declaration.Type) is not { } typeName)
        {
            return;
        }

        foreach (var variable in declaration.Declaration.Variables)
        {
            context.ReportIssue(Rule, variable, variable.Identifier.ValueText, typeName);
        }
    }

    private static bool IsContractMember(MemberDeclarationSyntax member) =>
        member.Parent is TypeDeclarationSyntax { Identifier.ValueText: var typeName }
        && Array.Exists(ContractNameSuffixes, x => typeName.EndsWith(x, StringComparison.Ordinal));

    private static string BannedType(SemanticModel model, TypeSyntax typeSyntax) =>
        model.GetTypeInfo(typeSyntax).Type is { } type && IsBannedType(type)
            ? type.ToDisplayString()
            : null;

    private static bool IsBannedType(ITypeSymbol type) =>
        BannedTypes.Contains(type.ToDisplayString())
        || (type is INamedTypeSymbol { IsGenericType: true, Name: "Task" } named && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        || (type.TypeKind == TypeKind.Delegate && type.ContainingNamespace?.ToDisplayString() == "System");
}
