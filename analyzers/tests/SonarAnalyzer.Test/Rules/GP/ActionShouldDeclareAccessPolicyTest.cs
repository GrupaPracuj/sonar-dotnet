using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ActionShouldDeclareAccessPolicyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ActionShouldDeclareAccessPolicy>();

    private const string ControllerStubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            public class AllowAnonymousAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
            }
        }
        """;

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_NoncompliantForMissingDeclaration() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() // Noncompliant {{Method 'GetSettings' has neither [Authorize] nor [AllowAnonymous], but other actions in 'UsersController' are explicitly protected with [Authorize].}}
                {
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenExplicitlyAnonymous() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.AllowAnonymous]
                public Microsoft.AspNetCore.Mvc.IActionResult GetStatus() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenNoActionUsesAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            public class PublicController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetHealth() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetVersion() => Ok();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ActionShouldDeclareAccessPolicy_CompliantWhenClassLevelAuthorize() =>
        builder.AddSnippet(
            ControllerStubs + """

            [Microsoft.AspNetCore.Mvc.Authorize]
            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                [Microsoft.AspNetCore.Mvc.Authorize]
                public Microsoft.AspNetCore.Mvc.IActionResult GetProfile() => Ok();

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetSettings() => Ok();
            }
            """)
            .VerifyNoIssues();
}
