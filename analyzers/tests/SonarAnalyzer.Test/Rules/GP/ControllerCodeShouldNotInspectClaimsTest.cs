/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ControllerCodeShouldNotInspectClaimsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ControllerCodeShouldNotInspectClaims>()
#if NET
        .AddReferences(new[] { CoreMetadataReference.SystemSecurityClaims })
#endif
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        global using System.Security.Claims;

        namespace Microsoft.AspNetCore.Http
        {
            public abstract class HttpContext
            {
                public abstract System.Security.Claims.ClaimsPrincipal User { get; }
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public interface IActionResult { }

            public abstract class ControllerBase
            {
                public System.Security.Claims.ClaimsPrincipal User { get; }
                public Microsoft.AspNetCore.Http.HttpContext HttpContext { get; }
                protected IActionResult Ok(object value) => null;
            }
        }

        namespace System.Security.Claims
        {
            public static class PrincipalExtensions
            {
                public static string FindFirstValue(this ClaimsPrincipal principal, string claimType) => null;
            }
        }
        """;

    [TestMethod]
    public void ControllerCodeShouldNotInspectClaims_ReportsLookupAndInspectionApis() =>
        builder.AddSnippet(
            Stubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    var first = User.FindFirst("sub"); // Noncompliant {{Move claims access out of controller code.}}
                    var value = User.FindFirstValue("sub"); // Noncompliant
                    var claims = User.Claims; // Noncompliant
                    var httpClaims = HttpContext.User.Claims; // Noncompliant
                    var all = User.FindAll("scope"); // Noncompliant
                    var hasClaim = User.HasClaim("scope", "write"); // Noncompliant
                    var inRole = User.IsInRole("admin"); // Noncompliant
                    var name = User.Identity.Name; // Noncompliant
                    return Ok(value);
                }

                public Microsoft.AspNetCore.Mvc.IActionResult Inspect(
                    System.Security.Claims.ClaimsPrincipal principal,
                    System.Security.Claims.ClaimsIdentity identity)
                {
                    var principalClaim = principal.FindFirst("sub"); // Noncompliant
                    var identityClaim = identity.FindFirst("sub"); // Noncompliant
                    var identityClaims = identity.Claims; // Noncompliant
                    return Ok(identity.Name); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllerCodeShouldNotInspectClaims_ReportsPrivateHelpersAndInheritedControllers() =>
        builder.AddSnippet(
            Stubs + """

            public abstract class ApplicationControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                protected string CurrentSubject() => User.FindFirstValue("sub"); // Noncompliant
            }

            public class UsersController : ApplicationControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(ReadName());

                private string ReadName() => HttpContext.User.Identity.Name; // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllerCodeShouldNotInspectClaims_AllowsIdentityProvidersFromTokenAndPolicyAuthorization() =>
        builder.AddSnippet(
            Stubs + """

            namespace Microsoft.AspNetCore.Authorization
            {
                public interface IAuthorizationService
                {
                    System.Threading.Tasks.Task<bool> AuthorizeAsync(
                        System.Security.Claims.ClaimsPrincipal user,
                        object resource,
                        string policyName);
                }
            }

            public interface IIdentityProvider
            {
                System.Guid UserId { get; }
            }

            public sealed class FromTokenAttribute : System.Attribute { }

            public sealed class UserContext
            {
                public System.Guid UserId { get; set; }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly IIdentityProvider identity;
                private readonly Microsoft.AspNetCore.Authorization.IAuthorizationService authorization;

                public UsersController(
                    IIdentityProvider identity,
                    Microsoft.AspNetCore.Authorization.IAuthorizationService authorization)
                {
                    this.identity = identity;
                    this.authorization = authorization;
                }

                public Microsoft.AspNetCore.Mvc.IActionResult FromProvider() => Ok(identity.UserId);

                public Microsoft.AspNetCore.Mvc.IActionResult FromToken([FromToken] UserContext user) => Ok(user.UserId);

                public System.Threading.Tasks.Task<bool> Authorize(object resource) =>
                    authorization.AuthorizeAsync(User, resource, "CanRead");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ControllerCodeShouldNotInspectClaims_IgnoresExternalServicesAndLookalikes() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class ClaimStore
            {
                public object FindFirst(string key) => null;
                public object FindAll(string key) => null;
                public bool HasClaim(string type, string value) => false;
                public bool IsInRole(string role) => false;
                public object Claims { get; }
                public FakeIdentity Identity { get; }
            }

            public sealed class FakeIdentity
            {
                public string Name { get; }
            }

            public sealed class ClaimsService
            {
                public string Read(System.Security.Claims.ClaimsPrincipal user) =>
                    user.FindFirstValue("sub");
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(ClaimStore store)
                {
                    store.FindFirst("sub");
                    store.FindAll("scope");
                    store.HasClaim("scope", "write");
                    store.IsInRole("admin");
                    var claims = store.Claims;
                    return Ok(store.Identity.Name);
                }
            }
            """)
            .VerifyNoIssues();
}
