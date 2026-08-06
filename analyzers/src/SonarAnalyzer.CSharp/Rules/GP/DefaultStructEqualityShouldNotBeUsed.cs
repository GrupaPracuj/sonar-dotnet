namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultStructEqualityShouldNotBeUsed : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0085";

    private const string OperatorMessageFormat =
        "'{0}' does not override Equals/GetHashCode - '==' falls back to slow, reflection-based comparison that compares reference-type fields by reference, not value.";
    private const string EqualsMessageFormat =
        "'{0}.Equals()' uses the slow, reflection-based default - override Equals/GetHashCode on '{0}' for a real fix, or avoid relying on this comparison.";
    private const string CollectionKeyMessageFormat =
        "'{0}' is used as a Dictionary/HashSet key but does not override Equals/GetHashCode - lookups will use slow, reflection-based comparison.";

    private const string OperatorEqualityName = "op_Equality";

    // One conceptual rule (GP0085) reported through three descriptors, all sharing the same rule id, so each usage
    // site keeps its own precise wording instead of being squeezed into a single generic template.
    private static readonly DiagnosticDescriptor OperatorRule = DescriptorFactory.Create(RuleId, OperatorMessageFormat);
    private static readonly DiagnosticDescriptor EqualsRule = DescriptorFactory.Create(RuleId, EqualsMessageFormat);
    private static readonly DiagnosticDescriptor CollectionKeyRule = DescriptorFactory.Create(RuleId, CollectionKeyMessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(OperatorRule, EqualsRule, CollectionKeyRule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeEqualityOperator, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        context.RegisterNodeAction(AnalyzeEqualsInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    // A plain struct only supports '==' at all when it (or something it implicitly converts to) defines
    // operator==; when it does, that custom operator is what actually runs, not the default path this rule is
    // about, so it must not be flagged - see OperandWithoutCustomOperator.
    private static void AnalyzeEqualityOperator(SonarSyntaxNodeReportingContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;
        if ((OperandWithoutCustomOperator(context.Model, binary.Left) ?? OperandWithoutCustomOperator(context.Model, binary.Right)) is not { } flagged)
        {
            return;
        }

        context.ReportIssue(OperatorRule, binary, flagged.Name);
    }

    private static ITypeSymbol OperandWithoutCustomOperator(SemanticModel model, ExpressionSyntax operand) =>
        model.GetTypeInfo(operand).Type is { } type && GpStructEquality.UsesDefaultEquality(type) && type.GetMembers(OperatorEqualityName).IsEmpty
            ? type
            : null;

    // Calling Equals(object) on a struct always resolves to something - either the struct's own override, or (when
    // there is none) the inherited System.ValueType.Equals. Checking that the resolved method's containing type is
    // literally System.ValueType is what tells the two apart; it also correctly leaves alone a struct that added a
    // faster IEquatable<T>.Equals(T) overload without fixing Equals(object)/GetHashCode, when that faster overload
    // is the one actually selected by overload resolution for a given call.
    private static void AnalyzeEqualsInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Parameters.Length: 1 } method
            || method.Name != nameof(Equals)
            || method.ContainingType.SpecialType != SpecialType.System_ValueType
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || context.Model.GetTypeInfo(memberAccess.Expression).Type is not { TypeKind: TypeKind.Struct } receiverType)
        {
            return;
        }

        context.ReportIssue(EqualsRule, invocation, receiverType.Name);
    }

    private static void AnalyzeObjectCreation(SonarSyntaxNodeReportingContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        if (context.Model.GetTypeInfo(objectCreation).Type is not INamedTypeSymbol { TypeArguments.Length: > 0 } createdType
            || KeyOrElementType(createdType) is not { } keyType
            || !GpStructEquality.UsesDefaultEquality(keyType))
        {
            return;
        }

        context.ReportIssue(CollectionKeyRule, objectCreation.Type, keyType.Name);
    }

    // Dictionary<TKey, TValue>'s first type argument is the key; HashSet<T>'s only type argument is the element,
    // which plays the same role - both are hashed and compared with the same default equality when unspecified.
    private static ITypeSymbol KeyOrElementType(INamedTypeSymbol createdType) =>
        createdType.ConstructedFrom.Is(KnownType.System_Collections_Generic_Dictionary_TKey_TValue) || createdType.ConstructedFrom.Is(KnownType.System_Collections_Generic_HashSet_T)
            ? createdType.TypeArguments[0]
            : null;
}
