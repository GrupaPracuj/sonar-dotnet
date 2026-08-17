using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class HttpActionShouldHaveOpenApiDescriptionTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpActionShouldHaveOpenApiDescription>()
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
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
            }
            public abstract class Controller : ControllerBase { }
        }

        namespace Swashbuckle.AspNetCore.Annotations
        {
            public sealed class SwaggerOperationAttribute : System.Attribute
            {
                public string Summary { get; set; }
                public string Description { get; set; }
            }
        }

        namespace Microsoft.AspNetCore.Http.Metadata
        {
            public sealed class EndpointSummaryAttribute : System.Attribute
            {
                public EndpointSummaryAttribute(string summary) { }
            }
            public sealed class EndpointDescriptionAttribute : System.Attribute
            {
                public EndpointDescriptionAttribute(string description) { }
            }
        }
        """;

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_NoncompliantWithoutDescription() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(); // Noncompliant {{Describe this HTTP action for OpenAPI consumers.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_CompliantForSwaggerSummaryOrDescription() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Swashbuckle.AspNetCore.Annotations.SwaggerOperation(Summary = "Gets an order")]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Swashbuckle.AspNetCore.Annotations.SwaggerOperation(Description = "Gets all orders")]
                public Microsoft.AspNetCore.Mvc.IActionResult GetAll() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_NoncompliantForEmptySwaggerMetadata() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Swashbuckle.AspNetCore.Annotations.SwaggerOperation(Summary = "")]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_CompliantForXmlSummary() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                /// <summary>Gets an order by its public identifier.</summary>
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_CompliantForXmlRemarks() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                /// <remarks>Returns the order visible to the current user.</remarks>
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_CompliantForInheritedDocumentation() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                /// <inheritdoc/>
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_CompliantForNativeMetadata() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Http.Metadata.EndpointSummary("Gets an order")]
                public Microsoft.AspNetCore.Mvc.IActionResult Get() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_CompliantForIgnoredOrNonActionMethod() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.ApiExplorerSettings(IgnoreApi = true)]
                public Microsoft.AspNetCore.Mvc.IActionResult Internal() => Ok();

                private Microsoft.AspNetCore.Mvc.IActionResult Helper() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpActionShouldHaveOpenApiDescription_IgnoresClassicMvcController() =>
        builder.AddSnippet(
            Stubs + """

            public class HomeController : Microsoft.AspNetCore.Mvc.Controller
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Index() => Ok();
            }
            """)
            .VerifyNoIssues();
}
