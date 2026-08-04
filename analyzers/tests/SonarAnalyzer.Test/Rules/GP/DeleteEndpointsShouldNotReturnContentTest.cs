using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DeleteEndpointsShouldNotReturnContentTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DeleteEndpointsShouldNotReturnContent>();

    private const string ControllerStubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpDeleteAttribute : System.Attribute { }
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult Ok(object value) => null;
                protected IActionResult NoContent() => null;
            }
        }
        """;

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_NoncompliantForOkWithBody() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteOrder(int id)
                {
                    var deleted = new { Id = id };
                    return Ok(deleted); // Noncompliant {{DELETE endpoints should return 204 (NoContent) instead of 200 with a response body.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CompliantForNoContent() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteOrder(int id) => NoContent();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CompliantForOkWithoutBody() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteOrder(int id) => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteEndpointsShouldNotReturnContent_CompliantForNonDeleteMethod() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetOrder(int id) => Ok(new { Id = id });
            }
            """)
            .VerifyNoIssues();
}
