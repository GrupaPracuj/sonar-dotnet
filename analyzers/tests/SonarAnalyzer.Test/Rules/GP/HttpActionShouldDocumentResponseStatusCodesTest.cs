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
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult BadRequest() => null;
                protected IActionResult NotFound() => null;
                protected IActionResult Conflict() => null;
                protected IActionResult NoContent() => null;
                protected IActionResult StatusCode(int statusCode) => null;
            }
            public abstract class Controller : ControllerBase { }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }
            public static class Results
            {
                public static IResult NotFound() => null;
            }
        }
        """;

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_NoncompliantForMissingStatus() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(bool found) // Noncompliant {{Document the non-200 response 404 with ProducesResponseType.}}
                {
                    return found ? Ok() : NotFound();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_ReportsAllMissingStatusesOnce() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int state) // Noncompliant {{Document the non-200 response 400, 404 with ProducesResponseType.}}
                {
                    if (state < 0)
                    {
                        return BadRequest();
                    }
                    return state == 0 ? NotFound() : Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_CompliantWhenDocumented() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(404)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(bool found) =>
                    found ? Ok() : NotFound();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_NoncompliantForIResultStatus() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Http.IResult Get() => // Noncompliant {{Document the non-200 response 404 with ProducesResponseType.}}
                    Microsoft.AspNetCore.Http.Results.NotFound();
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_CompliantForControllerLevelMetadata() =>
        builder.AddSnippet(
            Stubs + """

            [Microsoft.AspNetCore.Mvc.ProducesResponseType(409)]
            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(bool valid) =>
                    valid ? Ok() : Conflict();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_CompliantForApiConventionOrIgnoredAction() =>
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
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_CompliantForUnknownDynamicStatusAndNestedFunction() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int status)
                {
                    Microsoft.AspNetCore.Mvc.IActionResult Local() => NotFound();
                    return StatusCode(status);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_IgnoresClassicMvcController() =>
        builder.AddSnippet(
            Stubs + """

            public class HomeController : Microsoft.AspNetCore.Mvc.Controller
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Index() =>
                    NotFound();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDocumentResponseStatusCodes_AnalyzesMvcControllerMarkedAsApi() =>
        builder.AddSnippet(
            Stubs + """

            [Microsoft.AspNetCore.Mvc.ApiController]
            public class OrdersController : Microsoft.AspNetCore.Mvc.Controller
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => // Noncompliant
                    NotFound();
            }
            """)
            .Verify();
}
