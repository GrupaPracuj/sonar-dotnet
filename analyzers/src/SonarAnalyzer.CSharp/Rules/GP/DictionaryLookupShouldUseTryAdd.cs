namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DictionaryLookupShouldUseTryAdd : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0082";

    private const string MessageFormat = "Use 'TryAdd' instead of checking 'ContainsKey' before 'Add' - it does the lookup once instead of twice.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.IfStatement);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        if (TryGetTryAddParts(ifStatement, context.Model, out _, out _, out _))
        {
            context.ReportIssue(Rule, ifStatement);
        }
    }

    // Shared with the code fix: recognizes "if (!X.ContainsKey(K)) X.Add(K, V);" with no "else", where X and K are
    // syntactically identical in both calls and X's type implements IDictionary<TKey, TValue>. Returns false, with
    // all out parameters null, for anything else (including the same shape on a non-dictionary type, e.g. List<T>).
    internal static bool TryGetTryAddParts(IfStatementSyntax ifStatement, SemanticModel model, out ExpressionSyntax dictionary, out ExpressionSyntax key, out ExpressionSyntax value)
    {
        dictionary = null;
        key = null;
        value = null;

        if (ifStatement.Else is not null
            || ifStatement.Condition.RemoveParentheses() is not PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } negation
            || !TryGetInvocation(negation.Operand, "ContainsKey", 1, out var containsKeyTarget, out var containsKeyArguments)
            || SingleStatement(ifStatement.Statement) is not ExpressionStatementSyntax { Expression: { } addExpression }
            || !TryGetInvocation(addExpression, "Add", 2, out var addTarget, out var addArguments)
            || !IsDictionaryMethod(model, negation.Operand)
            || !IsDictionaryMethod(model, addExpression)
            || !SyntaxFactory.AreEquivalent(containsKeyTarget, addTarget)
            || !SyntaxFactory.AreEquivalent(containsKeyArguments[0].Expression, addArguments[0].Expression)
            || model.GetTypeInfo(containsKeyTarget).Type is not { } dictionaryType
            || !dictionaryType.DerivesOrImplements(KnownType.System_Collections_Generic_IDictionary_TKey_TValue)
            || !HasApplicableTryAdd(model, ifStatement.SpanStart, dictionaryType, containsKeyArguments[0].Expression, addArguments[1].Expression))
        {
            return false;
        }

        dictionary = containsKeyTarget;
        key = containsKeyArguments[0].Expression;
        value = addArguments[1].Expression;
        return true;
    }

    private static bool HasApplicableTryAdd(SemanticModel model, int position, ITypeSymbol dictionaryType, ExpressionSyntax key, ExpressionSyntax value) =>
        model.LookupSymbols(position, dictionaryType, "TryAdd")
            .OfType<IMethodSymbol>()
            .Any(x =>
                !x.IsStatic
                && x.ContainingType.ConstructedFrom.Is(KnownType.System_Collections_Generic_Dictionary_TKey_TValue)
                && x.Parameters.Length == 2
                && x.ReturnType.SpecialType == SpecialType.System_Boolean
                && model.ClassifyConversion(key, x.Parameters[0].Type).IsImplicit
                && model.ClassifyConversion(value, x.Parameters[1].Type).IsImplicit);

    private static bool IsDictionaryMethod(SemanticModel model, ExpressionSyntax expression) =>
        expression.RemoveParentheses() is InvocationExpressionSyntax invocation
        && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
        && method.ContainingType.ConstructedFrom.Is(KnownType.System_Collections_Generic_Dictionary_TKey_TValue);

    // The "if" body can be a single statement or a block containing exactly one statement; either way, that is the
    // one statement whose shape we need to check.
    private static StatementSyntax SingleStatement(StatementSyntax statement) =>
        statement is BlockSyntax { Statements: { Count: 1 } statements } ? statements[0] : statement;

    private static bool TryGetInvocation(ExpressionSyntax expression, string methodName, int argumentCount, out ExpressionSyntax target, out SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        target = null;
        arguments = default;

        if (expression.RemoveParentheses() is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } memberAccess,
                ArgumentList.Arguments: { } invocationArguments,
            }
            && name == methodName
            && invocationArguments.Count == argumentCount)
        {
            target = memberAccess.Expression;
            arguments = invocationArguments;
            return true;
        }

        return false;
    }
}
