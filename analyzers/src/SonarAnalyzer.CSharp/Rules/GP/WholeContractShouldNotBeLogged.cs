/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WholeContractShouldNotBeLogged : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0074";

    private const string MessageFormat = "Do not log the whole contract '{0}' - log the fields the diagnosis needs.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context) =>
        context.RegisterCompilationStartAction(start =>
        {
            var contracts = GpSemanticContractDetector.GetOrCreate(start.Compilation);
            start.RegisterNodeAction(c => AnalyzeInvocation(c, contracts), SyntaxKind.InvocationExpression);
        });

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context, GpSemanticContractDetector contracts)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList is not { } argumentList || !GpLoggingHelper.IsLoggingCall(context.Model, invocation))
        {
            return;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (ContractTypeInLogArgument(context.Model, argument.Expression, contracts) is { } contractType)
            {
                context.ReportIssue(Rule, argument, contractType.Name);
                return; // one finding per logging call is enough
            }
        }
    }

    // The whole object, not one of its fields: "message" matches, "message.OrderId" does not, because the type of a
    // member access is the member's type rather than the contract's.
    private static INamedTypeSymbol ContractTypeInLogArgument(
        SemanticModel model,
        ExpressionSyntax expression,
        GpSemanticContractDetector contracts)
    {
        if (ContractArgumentType(model, expression, contracts) is { } direct)
        {
            return direct;
        }

        return expression.RemoveParentheses() switch
        {
            InterpolatedStringExpressionSyntax interpolated => interpolated.Contents
                .OfType<InterpolationSyntax>()
                .Select(x => ContractArgumentType(model, x.Expression, contracts))
                .FirstOrDefault(x => x is not null),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression)
                && model.GetTypeInfo(binary).ConvertedType?.SpecialType == SpecialType.System_String =>
                ContractTypeInLogArgument(model, binary.Left, contracts) ?? ContractTypeInLogArgument(model, binary.Right, contracts),
            _ => null,
        };
    }

    private static INamedTypeSymbol ContractArgumentType(
        SemanticModel model,
        ExpressionSyntax expression,
        GpSemanticContractDetector contracts) =>
        model.GetTypeInfo(expression).Type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named
        && contracts.IsContract(named)
            ? named
            : null;
}
