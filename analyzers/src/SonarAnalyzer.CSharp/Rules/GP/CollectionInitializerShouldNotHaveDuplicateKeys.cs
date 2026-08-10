namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CollectionInitializerShouldNotHaveDuplicateKeys : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0083";

    // The message text itself carries the variable part (dictionary key vs. plain collection value), so the
    // descriptor's own format string is just a pass-through for it.
    private const string MessageFormat = "{0}";

    private const string DictionaryKeyMessage = "Duplicate key '{0}' in dictionary initializer - the second 'Add' call throws ArgumentException at runtime.";
    private const string CollectionValueMessage = "Duplicate value '{0}' in this collection initializer is redundant - 'Add' silently ignores it (or throws, for a type that disallows duplicates).";

    private const string NonGenericDictionaryFullName = "System.Collections.IDictionary";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(Analyze, SyntaxKind.CollectionInitializerExpression);

    private static void Analyze(SonarSyntaxNodeReportingContext context)
    {
        var initializer = (InitializerExpressionSyntax)context.Node;
        var elements = initializer.Expressions;
        if (elements.Count < 2)
        {
            return;
        }

        if (elements.All(IsComplexElement))
        {
            // The "{key, value}" shape: only report when the created type is actually a dictionary, so an
            // unrelated type with its own two-argument Add(a, b) overload is left alone.
            if (initializer.Parent is not ExpressionSyntax creation
                || context.Model.GetTypeInfo(creation).Type is not { } createdType
                || !ImplementsDictionary(createdType))
            {
                return;
            }

            ReportDuplicates(context, elements.Select(x => (Compare: FirstOperand(x), Report: (SyntaxNode)x)), DictionaryKeyMessage);
        }
        else if (elements.All(x => !IsComplexElement(x)))
        {
            if (initializer.Parent is ExpressionSyntax creation
                && context.Model.GetTypeInfo(creation).Type is { } createdType
                && createdType.DerivesOrImplements(KnownType.System_Collections_Generic_ISet_T))
            {
                ReportDuplicates(context, elements.Select(x => (Compare: x, Report: (SyntaxNode)x)), CollectionValueMessage);
            }
        }

        // Anything else (a mix of both shapes) is not valid C# for a single initializer under normal overload
        // resolution, so it is left unhandled rather than guessed at.
    }

    private static bool IsComplexElement(ExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.ComplexElementInitializerExpression);

    // A "{key, value}" element is itself an InitializerExpressionSyntax of kind ComplexElementInitializerExpression,
    // whose own Expressions are the key and the value.
    private static ExpressionSyntax FirstOperand(ExpressionSyntax complexElement) =>
        ((InitializerExpressionSyntax)complexElement).Expressions[0];

    private static bool ImplementsDictionary(ITypeSymbol type) =>
        type.DerivesOrImplements(KnownType.System_Collections_Generic_IDictionary_TKey_TValue)
        || type.AllInterfaces.Any(x => x.ToDisplayString() == NonGenericDictionaryFullName);

    // Only elements whose value is a compile-time constant are ever compared, so two elements that merely look
    // alike (e.g. two different local variables) can never be misidentified as duplicates. Reports once for every
    // element after the first one that shares its constant value with an earlier element.
    private static void ReportDuplicates(SonarSyntaxNodeReportingContext context, IEnumerable<(ExpressionSyntax Compare, SyntaxNode Report)> elements, string messageFormat)
    {
        var seenValues = new List<object>();
        foreach (var (compare, report) in elements)
        {
            var constant = context.Model.GetConstantValue(compare);
            if (!constant.HasValue)
            {
                continue;
            }

            if (seenValues.Any(x => Equals(x, constant.Value)))
            {
                context.ReportIssue(Rule, report, string.Format(messageFormat, constant.Value));
            }

            seenValues.Add(constant.Value);
        }
    }
}
