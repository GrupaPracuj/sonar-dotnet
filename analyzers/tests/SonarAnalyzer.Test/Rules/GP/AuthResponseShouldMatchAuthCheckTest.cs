using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class AuthResponseShouldMatchAuthCheckTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AuthResponseShouldMatchAuthCheck>();

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

            public interface IPrincipal
            {
                bool IsInRole(string role);
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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

            public interface IPrincipal
            {
                bool HasClaim(string type, string value);
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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

            public interface IIdentity
            {
                bool IsAuthenticated { get; }
            }

            public interface IPrincipal
            {
                IIdentity Identity { get; }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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

            public interface IIdentity
            {
                bool IsAuthenticated { get; }
            }

            public interface IPrincipal
            {
                IIdentity Identity { get; }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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

            public interface IPrincipal
            {
                bool IsInRole(string role);
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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

            public interface IIdentity
            {
                bool IsAuthenticated { get; }
            }

            public interface IPrincipal
            {
                IIdentity Identity { get; }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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

            public interface IIdentity
            {
                bool IsAuthenticated { get; }
            }

            public interface IPrincipal
            {
                IIdentity Identity { get; }
                bool IsInRole(string role);
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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

            public interface IIdentity
            {
                bool IsAuthenticated { get; }
            }

            public interface IPrincipal
            {
                IIdentity Identity { get; }
                bool IsInRole(string role);
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public IPrincipal User { get; }

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
}
