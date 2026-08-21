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
public class ActionShouldDeclareAccessPolicyTest
{
    // The snippets declare ASP.NET Core stubs, which the concurrency wrapper would move to another namespace.
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ActionShouldDeclareAccessPolicy>()
        .WithConcurrentAnalysis(false);

    private const string ControllerStubs =
        """
        namespace Microsoft.AspNetCore.Authorization
        {
            public class AuthorizeAttribute : System.Attribute { }
            public class AllowAnonymousAttribute : System.Attribute { }
            public class AuthorizationPolicy { }
            public class AuthorizationPolicyBuilder
            {
                public AuthorizationPolicyBuilder RequireAuthenticatedUser() => this;
                public AuthorizationPolicy Build() => new AuthorizationPolicy();
            }
            public class AuthorizationOptions
            {
                public AuthorizationPolicy FallbackPolicy { get; set; }
                public void AddPolicy(string name, System.Action<AuthorizationPolicyBuilder> configure) =>
                    configure(new AuthorizationPolicyBuilder());
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public class ApiControllerAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
            }
            public class MvcOptions
            {
                public Microsoft.AspNetCore.Mvc.Filters.FilterCollection Filters { get; } =
                    new Microsoft.AspNetCore.Mvc.Filters.FilterCollection();
            }
        }

        namespace Microsoft.AspNetCore.Mvc.Authorization
        {
            public class AuthorizeFilter { }
        }

        namespace Microsoft.AspNetCore.Mvc.Filters
        {
            public class FilterCollection : System.Collections.ObjectModel.Collection<object> { }
        }

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public class ControllerActionEndpointConventionBuilder { }

            public static class ControllerEndpointRouteBuilderExtensions
            {
                public static ControllerActionEndpointConventionBuilder MapControllers(
                    this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) => new ControllerActionEndpointConventionBuilder();
            }

            public static class AuthorizationEndpointConventionBuilderExtensions
            {
                public static TBuilder RequireAuthorization<TBuilder>(this TBuilder builder) => builder;
            }
        }
        """;

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForMissingDeclaration() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() // Noncompliant {{Method 'GetSettings' has neither [Authorize] nor [AllowAnonymous]; explicitly declare its access policy.}}
                {
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenExplicitlyAnonymous() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.AllowAnonymous]
                public Microsoft.AspNetCore.Mvc.IActionResult GetStatus() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForClassicMvcWhenNoActionUsesAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class PublicController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetHealth() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetVersion() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenClassLevelAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Authorization.Authorize]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok();
            }
            """)
            .VerifyNoIssues();

    // ASP.NET Core honours [Authorize] inherited from a shared base controller, so the derived controller has nothing
    // left to declare.
    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenBaseControllerDeclaresAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Authorization.Authorize]
            public abstract class SecuredControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            public class UsersController : SecuredControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenBaseControllerDeclaresAllowAnonymous() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Authorization.AllowAnonymous]
            public abstract class PublicControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            public class UsersController : PublicControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetStatus() => Ok();
            }
            """)
            .VerifyNoIssues();

    // A base class that declares no policy changes nothing: the per-action convention still applies.
    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantWhenBaseControllerDeclaresNothing() =>
        builder.AddSnippet(
            ControllerStubs + """

            public abstract class PlainControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            public class UsersController : PlainControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_ReportsOnceForSameFilePartialController() =>
        builder.AddSnippet(
            ControllerStubs + """

            public partial class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
            }

            public partial class UsersController
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForBareApiControllerAction() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForApiControllerDeclarations() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Mvc.ApiController]
            [Microsoft.AspNetCore.Authorization.Authorize]
            public class SecuredController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class MixedController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Authorization.AllowAnonymous]
                public Microsoft.AspNetCore.Mvc.IActionResult GetStatus() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForApiControllerWithAuthorizedBase() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Mvc.ApiController]
            [Microsoft.AspNetCore.Authorization.Authorize]
            public abstract class SecuredControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            public class UsersController : SecuredControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForDirectFallbackPolicy() =>
        builder.AddSnippet(
            ControllerStubs + """

            public static class AuthorizationConfiguration
            {
                public static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationOptions options) =>
                    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build();
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForNamedPolicyOnly() =>
        builder.AddSnippet(
            ControllerStubs + """

            public static class AuthorizationConfiguration
            {
                public static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationOptions options) =>
                    options.AddPolicy("secured", policy => policy.RequireAuthenticatedUser());
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForGlobalAuthorizeFilter() =>
        builder.AddSnippet(
            ControllerStubs + """

            public static class MvcConfiguration
            {
                public static void Configure(Microsoft.AspNetCore.Mvc.MvcOptions options) =>
                    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter());
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForGlobalAuthorizeFilterStoredInLocal() =>
        builder.AddSnippet(
            ControllerStubs + """

            public static class MvcConfiguration
            {
                public static void Configure(Microsoft.AspNetCore.Mvc.MvcOptions options)
                {
                    var filter = new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter();
                    options.Filters.Add(filter);
                }
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForReassignedAuthorizeFilterLocal() =>
        builder.AddSnippet(
            ControllerStubs + """

            public static class MvcConfiguration
            {
                public static void Configure(Microsoft.AspNetCore.Mvc.MvcOptions options)
                {
                    var filter = new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter();
                    filter = null;
                    options.Filters.Add(filter);
                }
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForMapControllersRequireAuthorization() =>
        builder.AddSnippet(
            ControllerStubs + """

            namespace TestApp
            {
                using Microsoft.AspNetCore.Builder;

                public static class EndpointConfiguration
                {
                    public static void Configure(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) =>
                        endpoints.MapControllers().RequireAuthorization();
                }

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();
                }
            }
            """)
            .VerifyNoIssues();

#if NET
    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantForRealGlobalAuthorizeFilterApi() =>
        new VerifierBuilder<CS.ActionShouldDeclareAccessPolicy>()
            .AddReferences(AspNetCoreMetadataReference.BasicReferences)
            .AddSnippet(
                """
                using Microsoft.AspNetCore.Mvc;
                using Microsoft.AspNetCore.Mvc.Authorization;

                public static class MvcConfiguration
                {
                    public static void Configure(MvcOptions options) =>
                        options.Filters.Add(new AuthorizeFilter());
                }

                [ApiController]
                public class UsersController : ControllerBase
                {
                    [HttpGet]
                    public IActionResult GetProfile() => Ok();
                }
                """)
            .VerifyNoIssues();
#endif

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForUnrelatedLookalikes() =>
        builder.AddSnippet(
            ControllerStubs + """

            namespace Lookalikes
            {
                public class AuthorizeAttribute : System.Attribute { }
                public class AuthorizeFilter { }
                public class FilterCollection
                {
                    public void Add(object filter) { }
                }
                public class MvcOptions
                {
                    public FilterCollection Filters { get; } = new FilterCollection();
                }
            }

            public static class MvcConfiguration
            {
                public static void Configure(Lookalikes.MvcOptions options) =>
                    options.Filters.Add(new Lookalikes.AuthorizeFilter());
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Lookalikes.Authorize]
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForAssemblyApiController() =>
        builder.AddSnippet(
            "[assembly: Microsoft.AspNetCore.Mvc.ApiController]\n\n" + ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok(); // Noncompliant
            }
            """)
            .Verify();
}
