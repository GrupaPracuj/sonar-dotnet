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
public class HttpActionShouldDocumentResponseStatusCodesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpActionShouldDocumentResponseStatusCodes>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class HttpGetAttribute : System.Attribute { }
            public sealed class HttpPostAttribute : System.Attribute { }
            public sealed class RouteAttribute : System.Attribute
            {
                public RouteAttribute(string template) { }
            }
            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = true)]
            public sealed class ApiControllerAttribute : System.Attribute { }
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
            public sealed class ProblemDetails { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult BadRequest() => null;
                protected IActionResult NotFound() => null;
                protected IActionResult Conflict() => null;
                protected IActionResult NoContent() => null;
                protected IActionResult StatusCode(int statusCode) => null;
                protected IActionResult StatusCode(int statusCode, object value) => null;
                protected IActionResult Problem(string detail = null, int? statusCode = null, string title = null) => null;
            }
            public abstract class Controller : ControllerBase { }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public static class StatusCodes
            {
                public const int Status200OK = 200;
                public const int Status400BadRequest = 400;
                public const int Status404NotFound = 404;
                public const int Status409Conflict = 409;
                public const int Status422UnprocessableEntity = 422;
            }
            public interface IResult { }
            public static class Results
            {
                public static IResult NotFound() => null;
            }
        }
        """;

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_ProblemInCatchIsReported() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(200)]
                public Microsoft.AspNetCore.Mvc.IActionResult Create()
                {
                    try
                    {
                        return Ok();
                    }
                    catch (System.Exception)
                    {
                        return Problem( // Noncompliant {{HTTP status 404 is returned but not declared. Add ProducesResponseType for this status.}}
                            statusCode: Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound,
                            title: "Order not found");
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_GenericProducesResponseTypeDocumentsProblemStatus() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType<Microsoft.AspNetCore.Mvc.ProblemDetails>(404)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    Problem(statusCode: Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_NotFoundIsReported() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    NotFound(); // Noncompliant {{HTTP status 404 is returned but not declared. Add ProducesResponseType for this status.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_ReportsEachMissingStatusOnce() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(200)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int state)
                {
                    if (state < 0)
                    {
                        return BadRequest(); // Noncompliant {{HTTP status 400 is returned but not declared. Add ProducesResponseType for this status.}}
                    }
                    if (state == 0)
                    {
                        return NotFound(); // Noncompliant {{HTTP status 404 is returned but not declared. Add ProducesResponseType for this status.}}
                    }
                    if (state == 1)
                    {
                        return NotFound();
                    }
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_HandlesIfSwitchAndCatch() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult Update(int state)
                {
                    try
                    {
                        if (state < 0)
                        {
                            return BadRequest(); // Noncompliant {{HTTP status 400 is returned but not declared. Add ProducesResponseType for this status.}}
                        }
                        return state switch
                        {
                            0 => Conflict(), // Noncompliant {{HTTP status 409 is returned but not declared. Add ProducesResponseType for this status.}}
                            _ => NoContent(), // Noncompliant {{HTTP status 204 is returned but not declared. Add ProducesResponseType for this status.}}
                        };
                    }
                    catch (System.Exception)
                    {
                        return Problem(statusCode: 404); // Noncompliant {{HTTP status 404 is returned but not declared. Add ProducesResponseType for this status.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_RecognizesAllConstantFormsAndNamedStatusCodeParameter() =>
        builder.AddSnippet(
            Stubs + """

            namespace Api
            {
                using Codes = Microsoft.AspNetCore.Http.StatusCodes;

                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    private const int ConflictStatus = 409;

                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult Literal() =>
                        StatusCode(418); // Noncompliant {{HTTP status 418 is returned but not declared. Add ProducesResponseType for this status.}}

                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult FrameworkConstant() =>
                        StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound); // Noncompliant {{HTTP status 404 is returned but not declared. Add ProducesResponseType for this status.}}

                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult LocalConstant() =>
                        StatusCode(ConflictStatus); // Noncompliant {{HTTP status 409 is returned but not declared. Add ProducesResponseType for this status.}}

                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public Microsoft.AspNetCore.Mvc.IActionResult AliasedAndNamed() =>
                        StatusCode(value: null, statusCode: Codes.Status422UnprocessableEntity); // Noncompliant {{HTTP status 422 is returned but not declared. Add ProducesResponseType for this status.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_DynamicStatusAndNestedFunctionAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult FromProblem(int status)
                {
                    Microsoft.AspNetCore.Mvc.IActionResult Local() => NotFound();
                    return Problem(statusCode: status);
                }

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult FromStatusCode(int status) =>
                    StatusCode(status);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_NonActionsAndLookalikeHelpersAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public static class LookalikeResults
            {
                public static Microsoft.AspNetCore.Mvc.IActionResult NotFound() => null;
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    LookalikeResults.NotFound();

                private Microsoft.AspNetCore.Mvc.IActionResult Internal() =>
                    NotFound();
            }

            public class OrderService
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    LookalikeResults.NotFound();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_ControllerAndBaseClassMetadataAreInherited() =>
        builder.AddSnippet(
            Stubs + """

            [Microsoft.AspNetCore.Mvc.ProducesResponseType(404)]
            public abstract class ApiControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase { }

            [Microsoft.AspNetCore.Mvc.ProducesResponseType(409)]
            public class OrdersController : ApiControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Missing() =>
                    NotFound();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult ConflictResult() =>
                    Conflict();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_ExpressionBodiedActionReportsOk() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.Route("orders")]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    Ok(); // Noncompliant {{HTTP status 200 is returned but not declared. Add ProducesResponseType for this status.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_HandlesMinimalResultReturnedByController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Http.IResult Get() =>
                    Microsoft.AspNetCore.Http.Results.NotFound(); // Noncompliant {{HTTP status 404 is returned but not declared. Add ProducesResponseType for this status.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_ConventionsIgnoredActionsAndClassicMvcAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public class Convention { public static void Get() { } }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ApiConventionMethod(typeof(Convention), "Get")]
                public Microsoft.AspNetCore.Mvc.IActionResult Conventional() => NotFound();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ApiExplorerSettings(IgnoreApi = true)]
                public Microsoft.AspNetCore.Mvc.IActionResult Internal() => BadRequest();
            }

            public class HomeController : Microsoft.AspNetCore.Mvc.Controller
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Index() => NotFound();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_ApiControllerAttributeEnablesMvcController() =>
        builder.AddSnippet(
            Stubs + """

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class OrdersController : Microsoft.AspNetCore.Mvc.Controller
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    NotFound(); // Noncompliant {{HTTP status 404 is returned but not declared. Add ProducesResponseType for this status.}}
            }
            """)
            .Verify();
}
