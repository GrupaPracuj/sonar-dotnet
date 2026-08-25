/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.CSharp.Rules;

// Shared "is this really the authorization API?" tests for the rules that key on IsInRole/HasClaim/IsAuthenticated.
// Those names are ordinary English, so a domain type can easily carry a same-named member - a shipment's
// HasClaim("damage"), a device's IsAuthenticated, a subscription's IsInRole. The declaring type decides, never the name.
internal static class GpPrincipalApi
{
    private const string PrincipalInterface = "System.Security.Principal.IPrincipal";
    private const string IdentityInterface = "System.Security.Principal.IIdentity";
    private const string ClaimsPrincipalType = "System.Security.Claims.ClaimsPrincipal";
    private const string ClaimsIdentityType = "System.Security.Claims.ClaimsIdentity";

    internal static bool IsClaimsApiType(ITypeSymbol type) =>
        GpJunoTypes.DerivesFrom(type, ClaimsPrincipalType) || GpJunoTypes.DerivesFrom(type, ClaimsIdentityType);

    internal static bool IsIdentityType(ITypeSymbol type) =>
        GpJunoTypes.Implements(type, IdentityInterface);

    private const string AuthorizationServiceInterface = "Microsoft.AspNetCore.Authorization.IAuthorizationService";

    private static readonly HashSet<string> ClaimReadNames = new(StringComparer.Ordinal)
    {
        "HasClaim",
        "FindFirst",
        "FindFirstValue",
        "FindAll",
    };

    // Wider than IsAccessCheck, which asks whether a single call is the check being branched on. This asks the looser
    // question "was authorization being done here at all", for rules that carry the weight of the finding elsewhere -
    // GP0021 also requires the exception to be swallowed. Claim helpers are accepted by name only inside GP.Juno, so
    // an unrelated method that happens to share a name is not mistaken for one.
    internal static bool IsAuthorizationWork(SemanticModel model, InvocationExpressionSyntax invocation) =>
        IsAccessCheck(model, invocation)
        || (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType: { } containingType } method
            && (IsAuthorizationServiceCall(containingType, method)
                || (ClaimReadNames.Contains(method.Name) && IsClaimsApiType(containingType))
                || (IsJunoClaimHelperName(method.Name) && IsJunoNamespace(method))));

    private static bool IsAuthorizationServiceCall(ITypeSymbol containingType, IMethodSymbol method) =>
        method.Name is "AuthorizeAsync" or "Authorize"
        && GpJunoTypes.Implements(containingType, AuthorizationServiceInterface);

    private static bool IsJunoClaimHelperName(string name) =>
        (name.StartsWith("Has", StringComparison.Ordinal) || name.StartsWith("Find", StringComparison.Ordinal))
        && name.EndsWith("Claim", StringComparison.Ordinal);

    private static bool IsJunoNamespace(IMethodSymbol method) =>
        Displays(method.ContainingNamespace) || Displays(method.ContainingType?.ContainingNamespace);

    private static bool Displays(INamespaceSymbol namespaceSymbol) =>
        namespaceSymbol?.ToDisplayString() is { } name
        && (name == "GP.Juno" || name.StartsWith("GP.Juno.", StringComparison.Ordinal));

    // The two ways an access check is spelled: IsInRole(role) on a principal, or HasClaim(...) on the claims API.
    internal static bool IsAccessCheck(SemanticModel model, InvocationExpressionSyntax invocation) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ContainingType: { } containingType } method
        && method.Name switch
        {
            "IsInRole" => GpJunoTypes.Implements(containingType, PrincipalInterface),
            "HasClaim" => IsClaimsApiType(containingType),
            _ => false,
        };
}
