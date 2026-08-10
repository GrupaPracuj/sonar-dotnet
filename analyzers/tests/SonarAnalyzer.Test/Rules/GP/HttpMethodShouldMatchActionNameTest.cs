using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class HttpMethodShouldMatchActionNameTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.HttpMethodShouldMatchActionName>();

    private const string ControllerStubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public class HttpDeleteAttribute : System.Attribute { }
            public class HttpPostAttribute : System.Attribute { }
            public interface IActionResult { }
            public class ViewResult : IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult RedirectToAction(string action) => null;
            }
            public abstract class Controller : ControllerBase
            {
                protected ViewResult View() => null;
                protected ViewResult View(object model) => null;
            }
        }
        """;

    [TestMethod]
    public void HttpMethodShouldMatchActionName_NoncompliantHttpGetWithDeleteName() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser() // Noncompliant {{Method 'DeleteUser' looks like it performs a mutating action but is annotated with [HttpGet].}}
                {
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpMethodShouldMatchActionName_NoncompliantHttpDeleteWithGetName() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUser() // Noncompliant {{Method 'GetUser' looks like it performs a read or creation action but is annotated with [HttpDelete].}}
                {
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpMethodShouldMatchActionName_NoncompliantHttpDeleteWithCreateName() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult CreateUser() // Noncompliant {{Method 'CreateUser' looks like it performs a read or creation action but is annotated with [HttpDelete].}}
                {
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpMethodShouldMatchActionName_CompliantHttpGetWithGetName() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUsers()
                {
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpMethodShouldMatchActionName_CompliantHttpDeleteWithDeleteName() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpDelete]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void HttpMethodShouldMatchActionName_CompliantHttpPostIsOutOfScope() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    // The scaffolded MVC pair: [HttpGet] Edit/Delete render the form, [HttpPost] Edit/Delete apply it.
    [TestMethod]
    public void HttpMethodShouldMatchActionName_CompliantForViewRenderingActions() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.Controller
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ViewResult Edit(int id) => View();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Delete(int id)
                {
                    return View(id);
                }
            }
            """)
            .VerifyNoIssues();

    // Rendering nothing, only mutating and redirecting: the [HttpGet] is the problem, not the name.
    [TestMethod]
    public void HttpMethodShouldMatchActionName_NoncompliantForMutatingActionThatDoesNotRenderAView() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.Controller
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser(int id) // Noncompliant {{Method 'DeleteUser' looks like it performs a mutating action but is annotated with [HttpGet].}}
                {
                    return RedirectToAction("Index");
                }
            }
            """)
            .Verify();

    // The exemption is resolved semantically: an unrelated View() does not turn an action into a view-rendering one.
    [TestMethod]
    public void HttpMethodShouldMatchActionName_NoncompliantForLookalikeViewCall() =>
        builder.AddSnippet(
            ControllerStubs + """

            public sealed class ReportBuilder
            {
                public object View() => null;
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser(ReportBuilder builder) // Noncompliant {{Method 'DeleteUser' looks like it performs a mutating action but is annotated with [HttpGet].}}
                {
                    builder.View();
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void HttpMethodShouldMatchActionName_CompliantForNonControllerType() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersHelper
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
                {
                    return null;
                }
            }
            """)
            .VerifyNoIssues();
}
