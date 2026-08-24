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
public class DeclaredResponseStatusShouldBeReturnedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DeclaredResponseStatusShouldBeReturned>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class HttpGetAttribute : System.Attribute { }
            public sealed class HttpPostAttribute : System.Attribute { }
            public sealed class ApiExplorerSettingsAttribute : System.Attribute
            {
                public bool IgnoreApi { get; set; }
            }
            public sealed class ApiConventionMethodAttribute : System.Attribute
            {
                public ApiConventionMethodAttribute(System.Type type, string name) { }
            }
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
            public class ProducesResponseTypeAttribute : System.Attribute
            {
                public ProducesResponseTypeAttribute(int statusCode) { }
                public ProducesResponseTypeAttribute(System.Type type, int statusCode) { }
            }
            public sealed class ProducesResponseTypeAttribute<T> : ProducesResponseTypeAttribute
            {
                public ProducesResponseTypeAttribute(int statusCode) : base(statusCode) { }
            }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult Created(string uri, object value) => null;
                protected IActionResult Accepted() => null;
                protected IActionResult NoContent() => null;
                protected IActionResult NotFound() => null;
                protected IActionResult Conflict() => null;
                protected IActionResult UnprocessableEntity() => null;
                protected IActionResult RedirectPermanent(string url) => null;
                protected IActionResult BadRequest() => null;
                protected IActionResult Unauthorized() => null;
                protected IActionResult Forbid() => null;
                protected IActionResult StatusCode(int statusCode) => null;
            }
            public abstract class Controller : ControllerBase { }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public static class StatusCodes
            {
                public const int Status200OK = 200;
                public const int Status201Created = 201;
                public const int Status202Accepted = 202;
                public const int Status204NoContent = 204;
                public const int Status400BadRequest = 400;
                public const int Status404NotFound = 404;
                public const int Status409Conflict = 409;
                public const int Status422UnprocessableEntity = 422;
                public const int Status500InternalServerError = 500;
            }
        }

        public sealed class OrderResponse { }
        """;

    [TestMethod]
    public void DeclaredResponseStatusShouldBeReturned_ReportsEachUnreturnedStatusOnce() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(201)] // Noncompliant {{HTTP status 201 is declared but no action path returns it.}}
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(OrderResponse), 201)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(204)] // Noncompliant {{HTTP status 204 is declared but no action path returns it.}}
                [Microsoft.AspNetCore.Mvc.ProducesResponseType<OrderResponse>(404)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(200)]
                public Microsoft.AspNetCore.Mvc.IActionResult Create() => Ok();
            }
            """)
            .Verify();

    [TestMethod]
    public void DeclaredResponseStatusShouldBeReturned_AcceptsAllDeclaredExplicitPaths() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(200)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(201)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(202)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(204)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(404)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(409)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(422)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(301)]
                public Microsoft.AspNetCore.Mvc.IActionResult Create(int state) =>
                    state switch
                    {
                        0 => Ok(),
                        1 => Created("orders/1", new OrderResponse()),
                        2 => Accepted(),
                        3 => NoContent(),
                        4 => NotFound(),
                        5 => Conflict(),
                        6 => UnprocessableEntity(),
                        _ => RedirectPermanent("orders"),
                    };
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeclaredResponseStatusShouldBeReturned_RecognizesConstantStatusCodeAndConditionalPaths() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(Microsoft.AspNetCore.Http.StatusCodes.Status201Created)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(bool exists) =>
                    exists ? StatusCode(201) : StatusCode(404);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeclaredResponseStatusShouldBeReturned_UnknownHelperSuppressesTheAction() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(201)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(bool ready)
                {
                    if (ready)
                    {
                        return Ok();
                    }
                    return BuildResponse();
                }

                private Microsoft.AspNetCore.Mvc.IActionResult BuildResponse() => Created("orders/1", new OrderResponse());
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeclaredResponseStatusShouldBeReturned_ErrorStatusesAreNotRequiredInTheAction() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(400)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(401)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(403)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(404)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(405)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(406)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(409)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(415)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(422)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(429)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(500)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeclaredResponseStatusShouldBeReturned_ControllerMetadataIsNotRequiredFromEveryAction() =>
        builder.AddSnippet(
            Stubs + """

            [Microsoft.AspNetCore.Mvc.ProducesResponseType(404)]
            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeclaredResponseStatusShouldBeReturned_ConventionsIgnoredActionsAndUnknownReturnsAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public class Convention { public static void Get() { } }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ApiConventionMethod(typeof(Convention), "Get")]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(201)]
                public Microsoft.AspNetCore.Mvc.IActionResult Conventional() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ApiExplorerSettings(IgnoreApi = true)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(201)]
                public Microsoft.AspNetCore.Mvc.IActionResult Internal() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(201)]
                public Microsoft.AspNetCore.Mvc.IActionResult Dynamic(int status) => StatusCode(status);
            }
            """)
            .VerifyNoIssues();
}
