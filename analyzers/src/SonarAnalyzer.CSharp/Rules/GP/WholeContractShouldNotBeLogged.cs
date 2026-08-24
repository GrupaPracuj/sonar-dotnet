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
        && !GpJunoTypes.DerivesFrom(named, "System.Exception")
        && !IsScalarIdentifierValueObject(named)
            ? named
            : null;

    private static bool IsScalarIdentifierValueObject(INamedTypeSymbol type)
    {
        if (!type.Name.EndsWith("Id", StringComparison.Ordinal))
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { IsGenericType: true, TypeArguments.Length: 1 }
                && IsLoggableScalar(current.TypeArguments[0])
                && current.GetMembers("Value").OfType<IPropertySymbol>().Any(x =>
                    !x.IsStatic
                    && x.DeclaredAccessibility == Accessibility.Public
                    && x.Type.Equals(current.TypeArguments[0])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoggableScalar(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_String
            or SpecialType.System_Byte
            or SpecialType.System_SByte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Decimal
        || type.ToDisplayString() == "System.Guid";
}
