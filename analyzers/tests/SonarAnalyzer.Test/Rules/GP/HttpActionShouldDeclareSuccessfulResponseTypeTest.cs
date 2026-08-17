using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class HttpActionShouldDeclareSuccessfulResponseTypeTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpActionShouldDeclareSuccessfulResponseType>()
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
            public interface IActionResult { }
            public abstract class ActionResult : IActionResult { }
            public sealed class ObjectResult : ActionResult { }
            public sealed class ActionResult<T>
            {
                public static implicit operator ActionResult<T>(ActionResult result) => null;
                public static implicit operator ActionResult<T>(T value) => null;
            }
            public abstract class ControllerBase
            {
                protected ActionResult Ok() => null;
                protected ActionResult Ok(object value) => null;
                protected IActionResult Accepted(string uri) => null;
                protected IActionResult Accepted(string uri, object value) => null;
                protected IActionResult BadRequest() => null;
            }
            public abstract class Controller : ControllerBase { }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }
            public static class Results
            {
                public static IResult Ok<T>(T value) => null;
            }
        }

        public sealed class OrderResponse { }
        """;

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_NoncompliantForAsyncActionWithoutApiController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> Get() // Noncompliant {{Declare the concrete successful response type with ProducesResponseType.}}
                {
                    await System.Threading.Tasks.Task.Delay(1);
                    return Ok(new OrderResponse()); // Secondary
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_CompliantWithConcreteMetadata() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(OrderResponse), 200)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(new OrderResponse());
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_ErrorMetadataDoesNotDocumentSuccess() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(string), 400)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => // Noncompliant
                    Ok(new OrderResponse()); // Secondary
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_DifferentSuccessStatusDoesNotDocumentOk() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(OrderResponse), 201)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => // Noncompliant
                    Ok(new OrderResponse()); // Secondary
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_CompliantForTypedActionResult() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<OrderResponse> Get() =>
                    Ok(new OrderResponse());
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_CompliantForBodylessSuccess() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Start() => Accepted("jobs/1");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_NoncompliantForAcceptedPayload() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Start() => // Noncompliant
                    Accepted("jobs/1", new OrderResponse()); // Secondary
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_NoncompliantForIResultPayload() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Http.IResult Get() => // Noncompliant
                    Microsoft.AspNetCore.Http.Results.Ok(new OrderResponse()); // Secondary
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_IgnoresUnusedSuccessFactory() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    var unused = Ok(new OrderResponse());
                    return BadRequest();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_IgnoresClassicMvcController() =>
        builder.AddSnippet(
            Stubs + """

            public class HomeController : Microsoft.AspNetCore.Mvc.Controller
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Index() =>
                    Ok(new OrderResponse());
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldDeclareSuccessfulResponseType_CompliantWithDerivedResponseAttribute() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class ProducesResponseTypesAttribute : Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute
            {
                public ProducesResponseTypesAttribute(System.Type type, int statusCode) : base(type, statusCode) { }
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [ProducesResponseTypes(typeof(OrderResponse), 200)]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() =>
                    Ok(new OrderResponse());
            }
            """)
            .VerifyNoIssues();
}
