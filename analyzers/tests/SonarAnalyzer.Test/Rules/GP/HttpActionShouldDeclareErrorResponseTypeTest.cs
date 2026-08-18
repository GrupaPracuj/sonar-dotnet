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
public class HttpActionShouldDeclareErrorResponseTypeTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpActionShouldDeclareErrorResponseType>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class HttpGetAttribute : System.Attribute { }
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
                protected IActionResult Ok(object value) => null;
                protected IActionResult BadRequest() => null;
                protected IActionResult BadRequest(object error) => null;
                protected IActionResult NotFound(object value) => null;
                protected IActionResult StatusCode(int statusCode, object value) => null;
            }
            public abstract class Controller : ControllerBase { }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }
            public static class Results
            {
                public static IResult BadRequest<T>(T error) => null;
            }
        }

        public sealed class ErrorResponse { }
        """;

    [TestMethod]
    public void HttpActionShouldDeclareErrorResponseType_NoncompliantForDocumentedStatusWithoutType() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(400)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => // Noncompliant {{Declare the concrete response type for status 400 with ProducesResponseType.}}
                    BadRequest(new ErrorResponse()); // Secondary
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDeclareErrorResponseType_CompliantWithConcreteType() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType<ErrorResponse>(400)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    BadRequest(new ErrorResponse());
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareErrorResponseType_DoesNotDuplicateMissingStatusOrRequireTypeForEmptyBody() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult MissingMetadata() =>
                    BadRequest(new ErrorResponse());

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(400)]
                public Microsoft.AspNetCore.Mvc.IActionResult EmptyBody() =>
                    BadRequest();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareErrorResponseType_HandlesConstantStatusCodeAndMinimalResult() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(500)]
                public Microsoft.AspNetCore.Mvc.IActionResult Explicit() => // Noncompliant
                    StatusCode(500, new ErrorResponse()); // Secondary

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(400)]
                public Microsoft.AspNetCore.Http.IResult Minimal() => // Noncompliant
                    Microsoft.AspNetCore.Http.Results.BadRequest(new ErrorResponse()); // Secondary
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDeclareErrorResponseType_IgnoresConventionIgnoredAndNestedResponses() =>
        builder.AddSnippet(
            Stubs + """

            public class Convention { public static void Get() { } }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ApiConventionMethod(typeof(Convention), "Get")]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(400)]
                public Microsoft.AspNetCore.Mvc.IActionResult Conventional() =>
                    BadRequest(new ErrorResponse());

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ApiExplorerSettings(IgnoreApi = true)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(400)]
                public Microsoft.AspNetCore.Mvc.IActionResult Internal() =>
                    BadRequest(new ErrorResponse());

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(404)]
                public Microsoft.AspNetCore.Mvc.IActionResult Nested()
                {
                    Microsoft.AspNetCore.Mvc.IActionResult Local() => NotFound(new ErrorResponse());
                    return Ok(new object());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareErrorResponseType_IgnoresClassicMvcController() =>
        builder.AddSnippet(
            Stubs + """

            public class HomeController : Microsoft.AspNetCore.Mvc.Controller
            {
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(400)]
                public Microsoft.AspNetCore.Mvc.IActionResult Index() =>
                    BadRequest(new ErrorResponse());
            }
            """)
            .VerifyNoIssues();
}
