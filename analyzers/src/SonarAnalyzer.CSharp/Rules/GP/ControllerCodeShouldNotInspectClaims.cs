/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControllerCodeShouldNotInspectClaims : SonarDiagnosticAnalyzer
{
    internal const string RuleId = "GP0112";

    private const string ControllerBaseType = "Microsoft.AspNetCore.Mvc.ControllerBase";
    private const string ClaimsPrincipalType = "System.Security.Claims.ClaimsPrincipal";
    private const string PrincipalExtensionsType = "System.Security.Claims.PrincipalExtensions";
    private const string MessageFormat = "Move claims access out of controller code.";

    private static readonly DiagnosticDescriptor Rule = DescriptorFactory.Create(RuleId, MessageFormat);
    private static readonly HashSet<string> ClaimsMethods = new(StringComparer.Ordinal)
    {
        "FindFirst",
        "FindAll",
        "HasClaim",
        "IsInRole",
    };
    private static readonly HashSet<string> IdentityValueProperties = new(StringComparer.Ordinal)
    {
        "Name",
        "AuthenticationType",
        "IsAuthenticated",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    protected override void Initialize(SonarAnalysisContext context)
    {
        context.RegisterNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeInvocation(SonarSyntaxNodeReportingContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsInsideController(context, invocation)
            || context.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !IsClaimsInspection(context.Model, invocation, method))
        {
            return;
        }

        context.ReportIssue(Rule, invocation.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Name : invocation.Expression);
    }

    private static void AnalyzeMemberAccess(SonarSyntaxNodeReportingContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (!IsInsideController(context, memberAccess)
            || context.Model.GetSymbolInfo(memberAccess).Symbol is not IPropertySymbol property)
        {
            return;
        }

        if (property.Name == "Claims" && GpPrincipalApi.IsClaimsApiType(property.ContainingType))
        {
            context.ReportIssue(Rule, memberAccess.Name);
        }
        else if (property.Name == "Identity"
                 && GpJunoTypes.DerivesFrom(property.ContainingType, ClaimsPrincipalType)
                 && !IsReceiverOfIdentityValueAccess(context.Model, memberAccess))
        {
            context.ReportIssue(Rule, memberAccess.Name);
        }
        else if (IdentityValueProperties.Contains(property.Name) && IsClaimsIdentityValueAccess(context.Model, memberAccess, property))
        {
            context.ReportIssue(Rule, memberAccess.Name);
        }
    }

    private static bool IsClaimsInspection(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (ClaimsMethods.Contains(method.Name) && GpPrincipalApi.IsClaimsApiType(method.ContainingType))
        {
            return true;
        }

        var original = method.ReducedFrom ?? method;
        return original.Name == "FindFirstValue"
               && original.ContainingType.ToDisplayString() == PrincipalExtensionsType
               && original.Parameters.FirstOrDefault()?.Type is { } principalType
               && GpJunoTypes.DerivesFrom(principalType, ClaimsPrincipalType)
               && ExtensionReceiverType(model, invocation, method) is { } receiverType
               && GpJunoTypes.DerivesFrom(receiverType, ClaimsPrincipalType);
    }

    private static ITypeSymbol ExtensionReceiverType(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.ReducedFrom is not null
            ? method.ReceiverType
            : invocation.ArgumentList.Arguments.FirstOrDefault() is { Expression: { } receiver }
                ? model.GetTypeInfo(receiver).Type
                : null;

    private static bool IsReceiverOfIdentityValueAccess(SemanticModel model, MemberAccessExpressionSyntax identityAccess) =>
        identityAccess.Parent is MemberAccessExpressionSyntax { Expression: var receiver } outer
        && receiver == identityAccess
        && model.GetSymbolInfo(outer).Symbol is IPropertySymbol property
        && IdentityValueProperties.Contains(property.Name);

    private static bool IsClaimsIdentityValueAccess(
        SemanticModel model,
        MemberAccessExpressionSyntax memberAccess,
        IPropertySymbol property) =>
        GpPrincipalApi.IsClaimsApiType(property.ContainingType)
        || memberAccess.Expression is MemberAccessExpressionSyntax identityAccess
           && model.GetSymbolInfo(identityAccess).Symbol is IPropertySymbol { Name: "Identity", ContainingType: { } principalType }
           && GpJunoTypes.DerivesFrom(principalType, ClaimsPrincipalType);

    private static bool IsInsideController(SonarSyntaxNodeReportingContext context, SyntaxNode node) =>
        context.Model.GetEnclosingSymbol(node.SpanStart)?.ContainingType is { } containingType
        && GpJunoTypes.DerivesFrom(containingType, ControllerBaseType);
}
