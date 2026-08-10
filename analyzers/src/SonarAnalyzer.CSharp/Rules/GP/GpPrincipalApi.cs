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
