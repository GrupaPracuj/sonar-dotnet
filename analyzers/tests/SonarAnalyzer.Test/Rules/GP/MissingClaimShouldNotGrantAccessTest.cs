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
public class MissingClaimShouldNotGrantAccessTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.MissingClaimShouldNotGrantAccess>()
#if NET
        .AddReferences(new[] { CoreMetadataReference.SystemSecurityClaims })
#endif
        ;

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_MissingClaimReturnsStatusCode200() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult StatusCode(int statusCode) => null;
                    protected IActionResult Forbid() => null;
                }
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(User user)
                {
                    if (/*!*/!user.HasClaim("filestore_access")/*Noncompliant*/)
                    {
                        return StatusCode(200);
                    }

                    return Forbid();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_MissingClaimReturnsSuccessfulMvcResponse() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Ok() => null;
                    protected IActionResult Forbid() => null;
                }
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(User user)
                {
                    if (/*!*/!user.HasClaim("filestore_access")/*Noncompliant*/)
                    {
                        return Ok();
                    }

                    return Forbid();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_MissingClaimInElseGrantsAccess() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Ok() => null;
                    protected IActionResult Forbid() => null;
                }
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(User user)
                {
                    if (/*!*/user.HasClaim("filestore_access")/*Noncompliant*/)
                    {
                        return Forbid();
                    }
                    else
                    {
                        return Ok();
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_MissingClaimIsDenied() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Ok() => null;
                    protected IActionResult Forbid() => null;
                }
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(User user)
                {
                    if (!user.HasClaim("filestore_access"))
                    {
                        return Forbid();
                    }

                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_NegationWithoutAccessDecision() =>
        builder.AddSnippet(
            """
            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class Claims
            {
                public bool IsMissing(User user) =>
                    !user.HasClaim("filestore_access");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_MissingClaimReturnsMinimalApiSuccess() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Http
            {
                public interface IResult { }

                public static class TypedResults
                {
                    public static IResult Ok() => null;
                    public static IResult Forbid() => null;
                }
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class Endpoint
            {
                public Microsoft.AspNetCore.Http.IResult Get(User user)
                {
                    if (/*!*/!user.HasClaim("filestore_access")/*Noncompliant*/)
                    {
                        return Microsoft.AspNetCore.Http.TypedResults.Ok();
                    }

                    return Microsoft.AspNetCore.Http.TypedResults.Forbid();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_LookalikeSuccessMethodIsNotAccessGrant() =>
        builder.AddSnippet(
            """
            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class ResultFactory
            {
                public object Ok() => null;

                public object Build(User user)
                {
                    if (!user.HasClaim("filestore_access"))
                    {
                        return Ok();
                    }

                    return null;
                }
            }
            """)
            .VerifyNoIssues();

    // HasClaim(string) is an existence check by construction - there is no value to compare, so it is never
    // flagged regardless of claim name.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForHasClaimExistenceCheck() =>
        builder.AddSnippet(
            """
            public static class ClaimTypes
            {
                public const string NameIdentifier = "sub";
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(string type) => true;
            }

            public class Access
            {
                public bool HasAccess(User user) =>
                    user.HasClaim("sub");

                public bool HasAccess2(User user) =>
                    user.HasClaim(ClaimTypes.NameIdentifier);
            }
            """)
            .VerifyNoIssues();

    // HasClaim(predicate) is only flagged when the predicate also compares the claim's Value - matching only on
    // Type is still an existence check, just expressed differently.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForHasClaimPredicateExistenceCheck() =>
        builder.AddSnippet(
            """
            using System;

            public class Claim
            {
                public string Type { get; set; }
                public string Value { get; set; }
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(Func<Claim, bool> predicate) => true;
            }

            public class Access
            {
                public bool HasAccess(User user) =>
                    user.HasClaim(c => c.Type == "sub");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantWhenValueBelongsToAnotherObject() =>
        builder.AddSnippet(
            """
            using System;

            public class Claim
            {
                public string Type { get; set; }
                public string Value { get; set; }
            }

            public class Other
            {
                public string Value { get; set; }
            }

            public class User : System.Security.Claims.ClaimsPrincipal
            {
                public bool HasClaim(Func<Claim, bool> predicate) => true;
            }

            public class Access
            {
                public bool HasAccess(User user, Other other) =>
                    user.HasClaim(c => c.Type == "sub" && other.Value == "12345");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForIdentityReadOutsideAuthorizationDecision() =>
        builder.AddSnippet(
            """
            public class Claim
            {
                public string Value { get; set; }
            }

            namespace GP.Juno.Security
            {
                public class ClaimsPrincipal
                {
                    public Claim FindUserClaim() => null;
                }
            }

            public class Audit
            {
                public string GetAuditSubject(GP.Juno.Security.ClaimsPrincipal user) =>
                    user.FindUserClaim().Value;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForLookalikeHasClaimGuard() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Ok() => null;
                    protected IActionResult Forbid() => null;
                }
            }

            public sealed class Shipment
            {
                public bool HasClaim(string claim) => false;
            }

            public class ShipmentsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(Shipment shipment)
                {
                    if (!shipment.HasClaim("damage"))
                    {
                        return Ok();
                    }

                    return Forbid();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForJunoOrCalledByApi_HasClaimPredicateExistenceCheck() =>
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
                public string Value { get; set; }
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
                    services.OrCalledByApi<string>(user => user.HasClaim(c => c.Type == "sub"));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForJunoAddUserActivitiesAlternative_FindFirstExistenceCheck() =>
        builder.AddSnippet(
            """
            using System;
            using System.Security.Claims;
            using GP.Juno.Hosting.AspNetCore.Security.UserActivities.DependencyInjection;

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
                    services.AddUserActivitiesAlternative(user => user.FindFirst(ClaimTypes.NameIdentifier) != null);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForJunoHasUserClaim() =>
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
                    user.HasUserClaim();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_JunoMissingApplicationClaimGrantsAccess() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Ok() => null;
                    protected IActionResult Forbid() => null;
                }
            }

            namespace GP.Juno.Security
            {
                public class ClaimsPrincipal
                {
                    public bool HasApplicationClaim() => true;
                }
            }

            public class FilesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(GP.Juno.Security.ClaimsPrincipal user)
                {
                    if (/*!*/!user.HasApplicationClaim()/*Noncompliant*/)
                    {
                        return Ok();
                    }

                    return Forbid();
                }
            }
            """)
            .Verify();

    // Option<Claim>.HasValue checks whether the claim was found at all - not its Value - so this stays compliant.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForJunoFindUserGroupClaimHasValue() =>
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
                    user.FindUserGroupClaim().HasValue;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_JunoHasCompanyClaim() =>
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
                    GP.Juno.Security.ClaimsExtractionExtensions.HasCompanyClaim(claims);
            }
            """)
            .VerifyNoIssues();

    // The name alone does not imply a fixed claim type - only the GP.Juno helpers do.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForUnrelatedMethodWithJunoName() =>
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

    // HasClaim(ClaimTypes.Email) is still just an existence check - same treatment as HasClaim("sub").
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForQualifiedClaimTypesExistenceCheck() =>
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
                    user.HasClaim(System.Security.Claims.ClaimTypes.Email);
            }
            """)
            .VerifyNoIssues();

    // The shape the rule description uses as its canonical example: a boolean access check that compares the value of
    // an identity claim read through ClaimsPrincipal.FindFirst.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForFindFirstPresenceCheckInBooleanAccessCheck() =>
        builder.AddSnippet(
            """
            using System.Security.Claims;

            public class Access
            {
                public bool HasAccess(ClaimsPrincipal user) =>
                    user.FindFirst("sub") != null;
            }
            """)
            .VerifyNoIssues();

    // A non-identity claim is exactly what the rule recommends deciding on, so comparing its value is compliant.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForNonIdentityClaimValueComparison() =>
        builder.AddSnippet(
            """
            using System.Security.Claims;

            public class Access
            {
                public bool HasAccess(ClaimsPrincipal user) =>
                    user.FindFirst("filestore_scope").Value == "delete";
            }
            """)
            .VerifyNoIssues();

    // FindFirst on an unrelated type is not a claim lookup, even inside a boolean access check.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForLookalikeFindFirstOnUnrelatedType() =>
        builder.AddSnippet(
            """
            public class Subscription
            {
                public string Value { get; set; }
            }

            public class SubscriptionRepository
            {
                public Subscription FindFirst(string name) => null;
            }

            public class Access
            {
                public bool HasAccess(SubscriptionRepository repository) =>
                    repository.FindFirst("sub").Value == "a3f1c9d2";
            }
            """)
            .VerifyNoIssues();

    // Reading the same claim for a non-authorization purpose is not an access-control decision.
    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_CompliantForFindFirstValueOutsideAuthorizationDecision() =>
        builder.AddSnippet(
            """
            using System.Security.Claims;

            public class Audit
            {
                public string CurrentSubject(ClaimsPrincipal user) =>
                    user.FindFirst("sub").Value;

                public bool IsFirstLogin(ClaimsPrincipal user) =>
                    user.FindFirst("sub").Value == "a3f1c9d2";
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MissingClaimShouldNotGrantAccess_Compliant() =>
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
