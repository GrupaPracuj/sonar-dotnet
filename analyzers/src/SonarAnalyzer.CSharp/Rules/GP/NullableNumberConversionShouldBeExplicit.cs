/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullableNumberConversionShouldBeExplicit : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0124";

    private const string MessageFormat = "Handle null explicitly before converting this nullable number; Convert.{0} silently treats null as zero.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> ConversionMethods = new(StringComparer.Ordinal)
    {
        "ToInt32",
        "ToInt64",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
            {
                IsStatic: true,
                ContainingType: { } containingType,
                Name: var methodName,
            }
            || containingType.ToDisplayString() != "System.Convert"
            || !ConversionMethods.Contains(methodName)
            || invocation.ArgumentList.Arguments is not { Count: 1 } arguments
            || context.Model.GetTypeInfo(arguments[0].Expression).Type is not INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments.Length: 1,
            }
            nullable
            || !IsIntegral(nullable.TypeArguments[0])
            || HasExplicitNullHandling(context.Model, invocation, arguments[0].Expression))
        {
            return;
        }

        context.ReportIssue(Rule, invocation, methodName);
    }

    private static bool HasExplicitNullHandling(SemanticModel model, InvocationExpressionSyntax invocation, ExpressionSyntax argument)
    {
        if (model.GetSymbolInfo(argument.RemoveParentheses()).Symbol is not { } symbol)
        {
            return false;
        }

        return invocation.Ancestors()
                   .OfType<ConditionalExpressionSyntax>()
                   .Any(x => TestsNullState(model, x.Condition, symbol))
               || invocation.FirstAncestorOrSelf<StatementSyntax>()?.Parent is BlockSyntax block
               && block.Statements
                   .TakeWhile(x => !x.Span.Contains(invocation.Span))
                   .OfType<IfStatementSyntax>()
                   .Any(x => TestsNullState(model, x.Condition, symbol));
    }

    private static bool TestsNullState(SemanticModel model, ExpressionSyntax condition, ISymbol symbol) =>
        condition.DescendantNodesAndSelf().Any(x => x.IsKind(SyntaxKind.NullLiteralExpression))
        && condition.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Any(x => symbol.Equals(model.GetSymbolInfo(x).Symbol))
        || condition.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(x => x.Name.Identifier.ValueText == "HasValue"
                      && symbol.Equals(model.GetSymbolInfo(x.Expression).Symbol));

    private static bool IsIntegral(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Byte
            or SpecialType.System_SByte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64;
}
