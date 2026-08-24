/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */
#if NET

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class AuthorizedActionShouldDeclareAuthResponsesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AuthorizedActionShouldDeclareAuthResponses>()
        .WithOptions(LanguageOptions.CSharpLatest)
        .AddReferences([
            AspNetCoreMetadataReference.MicrosoftAspNetCoreHttpAbstractions,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcAbstractions,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcCore,
            AspNetCoreMetadataReference.MicrosoftAspNetCoreMvcViewFeatures,
            GpMetadataReferences.MicrosoftAspNetCoreAuthorization,
            GpMetadataReferences.MicrosoftAspNetCoreMetadata,
        ]);

    [TestMethod]
    public void AuthorizedActionShouldDeclareAuthResponses_MethodLevelAuthorize() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public class UsersController : ControllerBase
            {
                [HttpGet("a")]
                [Authorize]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult BareAuthorize() => Ok(); // Noncompliant {{Declare the 401 response this authorized action can return.}}

                [HttpGet("b")]
                [Authorize(Policy = "UserAccess")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult PolicyAuthorize() => Ok(); // Noncompliant {{Declare the 401 and 403 responses this authorized action can return.}}

                [HttpGet("c")]
                [Authorize(Roles = "admin")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                [ProducesResponseType(StatusCodes.Status401Unauthorized)]
                public IActionResult RolesMissingForbidden() => Ok(); // Noncompliant {{Declare the 403 response this authorized action can return.}}

                [HttpGet("d")]
                [Authorize("UserAccess")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                [ProducesResponseType(StatusCodes.Status401Unauthorized)]
                [ProducesResponseType(StatusCodes.Status403Forbidden)]
                public IActionResult PositionalPolicyDeclared() => Ok();

                [HttpGet("e")]
                [Authorize]
                [ProducesResponseType(StatusCodes.Status200OK)]
                [ProducesResponseType(StatusCodes.Status401Unauthorized)]
                public IActionResult BareAuthorizeDeclared() => Ok();

                [HttpGet("f")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult NotAuthorized() => Ok();
            }
            """).Verify();

    [TestMethod]
    public void AuthorizedActionShouldDeclareAuthResponses_ControllerLevelAuthorize() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [Authorize(Policy = "ApplicationContext")]
            public class PermissionsController : ControllerBase
            {
                [HttpGet("a")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult Inherited() => Ok(); // Noncompliant {{Declare the 401 and 403 responses this authorized action can return.}}

                [HttpGet("b")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                [ProducesResponseType(StatusCodes.Status401Unauthorized)]
                [ProducesResponseType(StatusCodes.Status403Forbidden)]
                public IActionResult Declared() => Ok();

                [HttpGet("c")]
                [AllowAnonymous]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult Anonymous() => Ok();
            }

            [ApiController]
            [Authorize(Policy = "ApplicationContext")]
            [ProducesResponseType(StatusCodes.Status401Unauthorized)]
            [ProducesResponseType(StatusCodes.Status403Forbidden)]
            public class DeclaredOnControllerController : ControllerBase
            {
                [HttpGet("d")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult Ok200() => Ok();
            }

            [ApiController]
            [AllowAnonymous]
            public class PublicController : ControllerBase
            {
                [HttpGet("e")]
                [Authorize(Policy = "Ignored")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult AnonymousWins() => Ok();
            }
            """).Verify();

    [TestMethod]
    public void AuthorizedActionShouldDeclareAuthResponses_ControllerLevelConstantPolicy() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            internal static class PolicyNames
            {
                internal const string ReaderAccess = nameof(ReaderAccess);
            }

            [ApiController]
            [Authorize(PolicyNames.ReaderAccess)]
            public class BookLoanController : ControllerBase
            {
                [HttpPost("book-loans")]
                [ProducesResponseType(StatusCodes.Status201Created)]
                [ProducesResponseType(StatusCodes.Status401Unauthorized)]
                public IActionResult BorrowBook() => Ok(); // Noncompliant {{Declare the 403 response this authorized action can return.}}
            }
            """).Verify();

    [TestMethod]
    public void AuthorizedActionShouldDeclareAuthResponses_GlobalAuthorizeFilter() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Mvc.Authorization;

            public static class MvcSetup
            {
                public static void Configure(MvcOptions options) =>
                    options.Filters.Add(new AuthorizeFilter("BasicAccess"));
            }

            [ApiController]
            public class CatalogController : ControllerBase
            {
                [HttpGet("books")]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult Books() => Ok(); // Noncompliant {{Declare the 401 and 403 responses this authorized action can return.}}

                [HttpGet("public")]
                [AllowAnonymous]
                [ProducesResponseType(StatusCodes.Status200OK)]
                public IActionResult Public() => Ok();
            }
            """).Verify();

    [TestMethod]
    public void AuthorizedActionShouldDeclareAuthResponses_TypedAndSwaggerAttributes() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public class TypedController : ControllerBase
            {
                [HttpGet("a")]
                [Authorize(Policy = "UserAccess")]
                [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
                [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
                public IActionResult Generic() => Ok();

                [HttpGet("b")]
                [Authorize]
                [ProducesResponseType(typeof(ProblemDetails), 401)]
                public IActionResult WithType() => Ok();
            }
            """).VerifyNoIssues();

    [TestMethod]
    public void AuthorizedActionShouldDeclareAuthResponses_NotAnAction() =>
        builder.AddSnippet(
            """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public class HelperController : ControllerBase
            {
                [Authorize(Policy = "UserAccess")]
                private IActionResult NotAnAction() => Ok();

                [NonAction]
                [Authorize(Policy = "UserAccess")]
                public IActionResult ExplicitNonAction() => Ok();
            }

            [Authorize(Policy = "UserAccess")]
            public class PlainService
            {
                public string Get() => "not a controller";
            }
            """).VerifyNoIssues();
}

#endif
