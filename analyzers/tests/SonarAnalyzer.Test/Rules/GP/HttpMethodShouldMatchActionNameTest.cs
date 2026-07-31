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
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
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
