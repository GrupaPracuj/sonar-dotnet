namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotUseUnsafeDeserialization : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0029";

    private const string MessageFormat = "'{0}' lets the payload decide which types to instantiate - use a serializer that deserializes into a known type.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    // The five serializers Microsoft documents as performing unrestricted polymorphic deserialization.
    private static readonly HashSet<string> UnsafeSerializers = new(StringComparer.Ordinal)
    {
        "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter",
        "System.Runtime.Serialization.Formatters.Soap.SoapFormatter",
        "System.Runtime.Serialization.NetDataContractSerializer",
        "System.Web.UI.LosFormatter",
        "System.Web.UI.ObjectStateFormatter",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKindEx.ImplicitObjectCreationExpression);
        context.RegisterNodeAction(AnalyzeTypeNameHandling, SyntaxKind.SimpleAssignmentExpression);
    }

    // Target-typed 'new()' is included: 'BinaryFormatter formatter = new();' instantiates the same serializer.
    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        if (ObjectCreationFactory.TryCreate(context.Node, out var creation)
            && creation.TypeSymbol(context.Model) is { } type
            && UnsafeSerializers.Contains(type.ToDisplayString()))
        {
            context.ReportIssue(Rule, creation.Expression, type.Name);
        }
    }

    // Json.NET is only unsafe once TypeNameHandling moves away from None, so the assignment is what gets reported
    // rather than the serializer type itself.
    private static void AnalyzeTypeNameHandling(SonarSyntaxNodeReportingContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (AssignedMemberName(assignment.Left) != "TypeNameHandling"
            || assignment.Right is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var valueName }
            || valueName == "None"
            || context.Model.GetTypeInfo(assignment.Right).Type?.ToDisplayString() != "Newtonsoft.Json.TypeNameHandling")
        {
            return;
        }

        context.ReportIssue(Rule, assignment, $"TypeNameHandling.{valueName}");
    }

    private static string AssignedMemberName(ExpressionSyntax left) =>
        left switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText, // object initializer: new JsonSerializerSettings { TypeNameHandling = ... }
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null,
        };
}
