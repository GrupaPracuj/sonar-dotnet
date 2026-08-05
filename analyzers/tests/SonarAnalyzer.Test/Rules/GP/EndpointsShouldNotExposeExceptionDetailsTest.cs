using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class EndpointsShouldNotExposeExceptionDetailsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.EndpointsShouldNotExposeExceptionDetails>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
                protected IActionResult StatusCode(int code, object value) => null;
                protected IActionResult Problem(string title, int statusCode) => null;
            }
        }
        """;

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForMessageInResponse() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception ex)
                    {
                        return StatusCode(500, ex.Message); // Noncompliant {{Do not put 'Exception.Message' in a response - return a ProblemDetails without internal details.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForStackTraceReturned() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public string Get()
                {
                    try
                    {
                        return "order";
                    }
                    catch (System.InvalidOperationException ex)
                    {
                        return ex.StackTrace; // Noncompliant {{Do not put 'InvalidOperationException.StackTrace' in a response - return a ProblemDetails without internal details.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_NoncompliantForToString() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception ex)
                    {
                        return Ok(ex.ToString()); // Noncompliant {{Do not put 'Exception.ToString' in a response - return a ProblemDetails without internal details.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_CompliantForProblemDetails() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception)
                    {
                        return Problem("The order could not be read.", 500);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // The log is where exception detail belongs, so logging it is not reported.
    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_CompliantForLogging() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    try
                    {
                        return Ok("order");
                    }
                    catch (System.Exception ex)
                    {
                        System.Console.WriteLine(ex.Message);
                        return Problem("The order could not be read.", 500);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotExposeExceptionDetails_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                public string Describe()
                {
                    try
                    {
                        return "order";
                    }
                    catch (System.Exception ex)
                    {
                        return ex.Message;
                    }
                }
            }
            """)
            .VerifyNoIssues();
}
