using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class CreateEndpointsShouldReturnCreatedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CreateEndpointsShouldReturnCreated>();

    private const string ControllerStubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpPostAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
                protected IActionResult CreatedAtAction(string actionName, object routeValues, object value) => null;
            }
        }
        """;

    [TestMethod]
    public void CreateEndpointsShouldReturnCreated_NoncompliantForOk() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult CreateOrder(object order)
                {
                    return Ok(order); // Noncompliant {{Method 'CreateOrder' looks like it creates a resource - return 201 (Created/CreatedAtAction) instead of 200 (Ok).}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CreateEndpointsShouldReturnCreated_CompliantForCreatedAtAction() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult CreateOrder(object order) =>
                    CreatedAtAction(nameof(CreateOrder), new { id = 1 }, order);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CreateEndpointsShouldReturnCreated_CompliantForNonCreationVerb() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class AuthController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult Login(object credentials) => Ok(new { Token = "abc" });
            }
            """)
            .VerifyNoIssues();

    // "Ok" is resolved to ControllerBase: a same-named helper on the controller itself is not the MVC 200 factory.
    [TestMethod]
    public void CreateEndpointsShouldReturnCreated_CompliantForLookalikeOk() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private static object Ok(object value, bool acknowledged) => null;

                [Microsoft.AspNetCore.Mvc.HttpPost]
                public object CreateOrder(object order) => Ok(order, true);
            }
            """)
            .VerifyNoIssues();
}
