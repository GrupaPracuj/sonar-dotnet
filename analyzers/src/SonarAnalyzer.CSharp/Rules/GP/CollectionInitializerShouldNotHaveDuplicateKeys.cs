/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

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
            // A dictionary subtype can expose an unrelated Add overload whose first argument is not the key.
            // Collection initializers bind each element to its actual Add method, so verify that signature rather
            // than inferring argument roles from the created type alone.
            if (initializer.Parent is not ExpressionSyntax creation
                || context.Model.GetTypeInfo(creation).Type is not { } createdType
                || !UsesDictionaryAdd(elements, createdType, context.Model))
            {
                return;
            }

            ReportDuplicates(
                context,
                elements.Select(x => (Compare: FirstOperand(x), Report: (SyntaxNode)x)),
                DictionaryKeyMessage,
                UsesOrdinalIgnoreCaseComparer(creation, createdType, context.Model));
        }
        else if (elements.All(x => !IsComplexElement(x)))
        {
            if (initializer.Parent is ExpressionSyntax creation
                && context.Model.GetTypeInfo(creation).Type is { } createdType
                && createdType.DerivesOrImplements(KnownType.System_Collections_Generic_ISet_T))
            {
                ReportDuplicates(context, elements.Select(x => (Compare: x, Report: (SyntaxNode)x)), CollectionValueMessage, false);
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

    private static bool UsesDictionaryAdd(SeparatedSyntaxList<ExpressionSyntax> elements, ITypeSymbol createdType, SemanticModel model)
    {
        var genericDictionary = createdType.AllInterfaces.FirstOrDefault(x =>
            x.ConstructedFrom.Is(KnownType.System_Collections_Generic_IDictionary_TKey_TValue));
        if (genericDictionary is { TypeArguments.Length: 2 })
        {
            return elements.All(x =>
                model.GetCollectionInitializerSymbolInfo(x).Symbol is IMethodSymbol { Parameters.Length: 2 } add
                && add.Parameters[0].Type.Equals(genericDictionary.TypeArguments[0])
                && add.Parameters[1].Type.Equals(genericDictionary.TypeArguments[1]));
        }

        return createdType.AllInterfaces.Any(x => x.ToDisplayString() == NonGenericDictionaryFullName)
               && elements.All(x =>
                   model.GetCollectionInitializerSymbolInfo(x).Symbol is IMethodSymbol { Parameters.Length: 2 } add
                   && add.Parameters.All(p => p.Type.SpecialType == SpecialType.System_Object));
    }

    // Only elements whose value is a compile-time constant are ever compared, so two elements that merely look
    // alike (e.g. two different local variables) can never be misidentified as duplicates. Reports once for every
    // element after the first one that shares its constant value with an earlier element.
    private static void ReportDuplicates(
        SonarSyntaxNodeReportingContext context,
        IEnumerable<(ExpressionSyntax Compare, SyntaxNode Report)> elements,
        string messageFormat,
        bool ordinalIgnoreCase)
    {
        var seenValues = new List<object>();
        foreach (var (compare, report) in elements)
        {
            var constant = context.Model.GetConstantValue(compare);
            if (!constant.HasValue)
            {
                continue;
            }

            if (seenValues.Any(x => ValuesEqual(x, constant.Value, ordinalIgnoreCase)))
            {
                context.ReportIssue(Rule, report, string.Format(messageFormat, constant.Value));
            }

            seenValues.Add(constant.Value);
        }
    }

    private static bool ValuesEqual(object first, object second, bool ordinalIgnoreCase) =>
        ordinalIgnoreCase && first is string firstString && second is string secondString
            ? string.Equals(firstString, secondString, StringComparison.OrdinalIgnoreCase)
            : Equals(first, second);

    private static bool UsesOrdinalIgnoreCaseComparer(ExpressionSyntax creation, ITypeSymbol createdType, SemanticModel model)
    {
        if (createdType is not INamedTypeSymbol { TypeArguments.Length: 2 } namedType
            || namedType.TypeArguments[0].SpecialType != SpecialType.System_String
            || creation is not ObjectCreationExpressionSyntax { ArgumentList: { } argumentList } objectCreation
            || model.GetSymbolInfo(objectCreation).Symbol is not IMethodSymbol constructor)
        {
            return false;
        }

        var lookup = new CSharpMethodParameterLookup(argumentList, constructor);
        return argumentList.Arguments.Any(argument =>
            lookup.TryGetSymbol(argument, out var parameter)
            && parameter.Name == "comparer"
            && model.GetSymbolInfo(argument.Expression).Symbol is IPropertySymbol
            {
                Name: "OrdinalIgnoreCase",
                ContainingType: { } containingType,
            }
            && containingType.ToDisplayString() == "System.StringComparer");
    }
}
