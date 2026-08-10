using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class AuthResponseShouldMatchAuthCheckTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AuthResponseShouldMatchAuthCheck>()
#if NET
        .AddReferences(new[] { CoreMetadataReference.SystemSecurityClaims })
#endif
        ;

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_NoncompliantForIsInRoleReturningUnauthorized() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Unauthorized() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (!User.IsInRole("Admin"))
                    {
                        return Unauthorized(); // Noncompliant {{This looks like a permission check; return 403 (Forbid) instead of 401 (Unauthorized).}}
                    }

                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_NoncompliantForHasClaimReturningStatusCode401() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult StatusCode(int code) => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (!User.HasClaim("permission", "users.delete"))
                    {
                        return StatusCode(401); // Noncompliant {{This looks like a permission check; return 403 (Forbid) instead of 401 (Unauthorized).}}
                    }

                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_NoncompliantForIsAuthenticatedReturningForbid() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Forbid() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (!User.Identity.IsAuthenticated)
                    {
                        return Forbid(); // Noncompliant {{This looks like an authentication check; return 401 (Unauthorized) instead of 403 (Forbid).}}
                    }

                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_NoncompliantForIsAuthenticatedInElseBranchReturningStatusCode403() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult StatusCode(int code) => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (User.Identity.IsAuthenticated)
                    {
                        return Ok();
                    }
                    else
                    {
                        return StatusCode(403); // Noncompliant {{This looks like an authentication check; return 401 (Unauthorized) instead of 403 (Forbid).}}
                    }
                }
            }
            """)
            .Verify();

    // A custom identity implementation still is one: the rule keys on System.Security.Principal.IIdentity, not on a
    // single concrete type.
    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_NoncompliantForCustomIdentityImplementation() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Forbid() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public sealed class TenantIdentity : System.Security.Principal.IIdentity
            {
                public string AuthenticationType => null;
                public bool IsAuthenticated => false;
                public string Name => null;
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public TenantIdentity Identity { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (!Identity.IsAuthenticated)
                    {
                        return Forbid(); // Noncompliant {{This looks like an authentication check; return 401 (Unauthorized) instead of 403 (Forbid).}}
                    }

                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CompliantForIsInRoleReturningForbid() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Forbid() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (!User.IsInRole("Admin"))
                    {
                        return Forbid();
                    }

                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CompliantForIsAuthenticatedReturningUnauthorized() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Unauthorized() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (!User.Identity.IsAuthenticated)
                    {
                        return Unauthorized();
                    }

                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CompliantForMixedAmbiguousCondition() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Unauthorized() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (!User.Identity.IsAuthenticated || !User.IsInRole("Admin"))
                    {
                        return Unauthorized();
                    }

                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CompliantForNestedIfIsAnalyzedIndependently() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Forbid() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (User.Identity.IsAuthenticated)
                    {
                        if (!User.IsInRole("Admin"))
                        {
                            return Forbid();
                        }

                        return Ok();
                    }

                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CompliantForWrongResponseInSuccessfulBranch() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Unauthorized() => null;
                    protected IActionResult Forbid() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }

                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    if (User.IsInRole("Admin"))
                    {
                        return Unauthorized();
                    }

                    return Forbid();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CompliantForLookalikeReturnedResponse() =>
        builder.AddSnippet(
            """
            public sealed class ResultFactory
            {
                public object Unauthorized() => null;
            }

            public class Service
            {
                public object Execute(System.Security.Claims.ClaimsPrincipal user, ResultFactory results)
                {
                    if (!user.IsInRole("Admin"))
                    {
                        return results.Unauthorized();
                    }

                    return null;
                }
            }
            """)
            .VerifyNoIssues();

    // Domain members that happen to be named like the authorization API are not authorization checks, so the
    // 401/403 distinction the rule enforces does not apply to them.
    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CompliantForLookalikeCheckedCondition() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Unauthorized() => null;
                    protected IActionResult Forbid() => null;
                    protected IActionResult Ok() => null;
                }
            }

            public sealed class Shipment
            {
                public bool HasClaim(string type, string value) => false;
                public bool IsInRole(string role) => false;
                public bool IsAuthenticated => false;
            }

            public class ShipmentsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Damage(Shipment shipment)
                {
                    if (!shipment.HasClaim("damage", "total"))
                    {
                        return Unauthorized();
                    }

                    if (!shipment.IsInRole("carrier"))
                    {
                        return Unauthorized();
                    }

                    if (!shipment.IsAuthenticated)
                    {
                        return Forbid();
                    }

                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_NoncompliantForMinimalApiFailedBranch() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }
                public static class Results
                {
                    public static IResult Unauthorized() => null;
                    public static IResult Ok() => null;
                }
            }

            public class Endpoint
            {
                public Microsoft.AspNetCore.Http.IResult Execute(System.Security.Claims.ClaimsPrincipal user)
                {
                    if (!user.HasClaim("permission", "users.delete"))
                    {
                        return Microsoft.AspNetCore.Http.Results.Unauthorized(); // Noncompliant
                    }

                    return Microsoft.AspNetCore.Http.Results.Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void AuthResponseShouldMatchAuthCheck_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("AuthResponseShouldMatchAuthCheck.cs")
            .WithCodeFix<CS.AuthResponseShouldMatchAuthCheckCodeFix>()
            .WithCodeFixedPaths("AuthResponseShouldMatchAuthCheck.Fixed.cs")
            .VerifyCodeFix();
}
