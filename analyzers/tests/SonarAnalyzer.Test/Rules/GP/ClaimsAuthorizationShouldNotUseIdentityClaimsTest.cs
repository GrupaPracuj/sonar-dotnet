using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ClaimsAuthorizationShouldNotUseIdentityClaimsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ClaimsAuthorizationShouldNotUseIdentityClaims>();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_NegatedHasClaim() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool HasClaim(string type) => true;
            }

            public class Access
            {
                public bool HasAccess(User user)
                {
                    return !user.HasClaim("filestore_access"); // Noncompliant {{Do not base access decisions on a negated HasClaim check.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_IdentityClaimInHasClaim() =>
        builder.AddSnippet(
            """
            public static class ClaimTypes
            {
                public const string NameIdentifier = "sub";
            }

            public class User
            {
                public bool HasClaim(string type) => true;
            }

            public class Access
            {
                public bool HasAccess(User user) =>
                    user.HasClaim("sub"); // Noncompliant {{Do not base access control on identity claim 'sub'.}}

                public bool HasAccess2(User user) =>
                    user.HasClaim(ClaimTypes.NameIdentifier); // Noncompliant {{Do not base access control on identity claim 'NameIdentifier'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_IdentityClaimInAuthorizePolicy() =>
        builder.AddSnippet(
            """
            using System;

            public class AuthorizeAttribute : Attribute
            {
                public string Policy { get; set; }
            }

            [Authorize(Policy = "sub")] // Noncompliant {{Do not base access control on identity claim 'sub'.}}
            public class Endpoint { }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_JunoOrCalledByApi_HasClaimPredicate() =>
        builder.AddSnippet(
            """
            using System;
            using GP.Juno.Hosting.AspNetCore.Security.UserActivities.DependencyInjection;

            public class ClaimsPrincipal
            {
                public bool HasClaim(Func<Claim, bool> predicate) => true;
            }

            public class Claim
            {
                public string Type { get; set; }
            }

            public interface IServiceCollection { }

            namespace GP.Juno.Hosting.AspNetCore.Security.UserActivities.DependencyInjection
            {
                public static class AlternativePermissionExtensions
                {
                    public static IServiceCollection OrCalledByApi<TResource>(this IServiceCollection services, Func<ClaimsPrincipal, bool> userPredicate = null) => services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.OrCalledByApi<string>(user => user.HasClaim(c => c.Type == "sub")); // Noncompliant {{Do not base access control on identity claim 'sub'.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_JunoAddUserActivitiesAlternative_FindFirstClaimTypes() =>
        builder.AddSnippet(
            """
            using System;
            using GP.Juno.Hosting.AspNetCore.Security.UserActivities.DependencyInjection;

            public static class ClaimTypes
            {
                public const string NameIdentifier = "sub";
            }

            public class Claim
            {
                public string Type { get; set; }
            }

            public class ClaimsPrincipal
            {
                public Claim FindFirst(string type) => null;
                public Claim FindFirst(object type) => null;
            }

            public interface IServiceCollection { }

            namespace GP.Juno.Hosting.AspNetCore.Security.UserActivities.DependencyInjection
            {
                public static class AlternativePermissionExtensions
                {
                    public static IServiceCollection AddUserActivitiesAlternative(this IServiceCollection services, Func<ClaimsPrincipal, bool> alternativePermission) => services;
                }
            }

            public class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddUserActivitiesAlternative(user => user.FindFirst(ClaimTypes.NameIdentifier) != null); // Noncompliant {{Do not base access control on identity claim 'NameIdentifier'.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_JunoHasUserClaim() =>
        builder.AddSnippet(
            """
            namespace GP.Juno.Security
            {
                public class ClaimsPrincipal
                {
                    public bool HasUserClaim() => true;
                }
            }

            public class Access
            {
                public bool HasAccess(GP.Juno.Security.ClaimsPrincipal user) =>
                    user.HasUserClaim(); // Noncompliant {{Do not base access control on identity claim 'sub'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_JunoNegatedHasApplicationClaim() =>
        builder.AddSnippet(
            """
            namespace GP.Juno.Security
            {
                public class ClaimsPrincipal
                {
                    public bool HasApplicationClaim() => true;
                }
            }

            public class Access
            {
                public bool HasAccess(GP.Juno.Security.ClaimsPrincipal user) =>
                    !user.HasApplicationClaim(); // Noncompliant {{Do not base access decisions on a negated HasClaim check.}}
                                                  // Noncompliant@-1 {{Do not base access control on identity claim 'app'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_JunoFindUserGroupClaimHasValue() =>
        builder.AddSnippet(
            """
            public class Claim { }

            public class Option<T> { public bool HasValue => true; }

            namespace GP.Juno.Security.UserContexts
            {
                public class ClaimsPrincipal
                {
                    public Option<Claim> FindUserGroupClaim() => null;
                }
            }

            public class Access
            {
                public bool HasAccess(GP.Juno.Security.UserContexts.ClaimsPrincipal user) =>
                    user.FindUserGroupClaim().HasValue; // Noncompliant {{Do not base access control on identity claim 'userGroup'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_JunoHasCompanyClaim() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Claim { }

            namespace GP.Juno.Security
            {
                public static class ClaimsExtractionExtensions
                {
                    public static bool HasCompanyClaim(this IEnumerable<Claim> claims) => true;
                }
            }

            public class Access
            {
                public bool HasAccess(IEnumerable<Claim> claims) =>
                    GP.Juno.Security.ClaimsExtractionExtensions.HasCompanyClaim(claims); // Noncompliant {{Do not base access control on identity claim 'company'.}}
            }
            """)
            .Verify();

    // The name alone does not imply a fixed claim type - only the GP.Juno helpers do.
    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_CompliantForUnrelatedMethodWithJunoName() =>
        builder.AddSnippet(
            """
            public class ClaimsPrincipal
            {
                public bool HasCompanyClaim() => true;
                public bool HasUserClaim() => true;
            }

            public class Access
            {
                public bool HasAccess(ClaimsPrincipal user) =>
                    user.HasCompanyClaim() && user.HasUserClaim();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_NoncompliantForQualifiedClaimTypes() =>
        builder.AddSnippet(
            """
            namespace System.Security.Claims
            {
                public static class ClaimTypes
                {
                    public const string Email = "email";
                }
            }

            public class User
            {
                public bool HasClaim(string type) => true;
            }

            public class Access
            {
                public bool HasAccess(User user) =>
                    user.HasClaim(System.Security.Claims.ClaimTypes.Email); // Noncompliant {{Do not base access control on identity claim 'Email'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_Compliant() =>
        builder.AddSnippet(
            """
            using System;

            public class User
            {
                public bool HasClaim(string type) => true;
            }

            public class AuthorizeAttribute : Attribute
            {
                public string Policy { get; set; }
            }

            [Authorize(Policy = "filestore_access")]
            public class Endpoint
            {
                public bool HasAccess(User user) => user.HasClaim("filestore_access");
            }
            """)
            .VerifyNoIssues();
}
