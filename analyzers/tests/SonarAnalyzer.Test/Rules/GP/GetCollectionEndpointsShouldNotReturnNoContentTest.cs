using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class GetCollectionEndpointsShouldNotReturnNoContentTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.GetCollectionEndpointsShouldNotReturnNoContent>()
        .AddReferences(MetadataReferenceFacade.SystemThreadingTasks);

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForNoContent() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NoContent<T>() => null;
                    protected ActionResult<T> StatusCode<T>(int code) => null;
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> GetUsers()
                {
                    return NoContent<System.Collections.Generic.IReadOnlyList<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForStatusCode204() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NoContent<T>() => null;
                    protected ActionResult<T> StatusCode<T>(int code) => null;
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IEnumerable<string>> GetUsers()
                {
                    return StatusCode<System.Collections.Generic.IEnumerable<string>>(204); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForOkEmptyCollection() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> GetUsers()
                {
                    return Ok<System.Collections.Generic.IReadOnlyList<string>>(new List<string>());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForNonGetMethod() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NoContent<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> DeleteUsers()
                {
                    return NoContent<System.Collections.Generic.IReadOnlyList<string>>();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForArrayReturnType() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NoContent<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<string[]> GetUsers()
                {
                    return NoContent<string[]>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForConcreteListReturnType() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NoContent<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<List<string>> GetUsers()
                {
                    return NoContent<List<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForValueTaskWrappedActionResult() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NoContent<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async ValueTask<Microsoft.AspNetCore.Mvc.ActionResult<IEnumerable<string>>> GetUsersAsync()
                {
                    await Task.Yield();
                    return NoContent<IEnumerable<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                }
            }
            """)
            .WithOptions(LanguageOptions.FromCSharp8)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForPlainIActionResult() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NoContent() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUsers(bool empty)
                {
                    if (empty)
                    {
                        return NoContent(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                    }

                    return Ok(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForPlainIActionResultWithExplicitGenericOk() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NoContent() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUsers(bool empty)
                {
                    if (empty)
                    {
                        return NoContent(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                    }

                    return Ok<List<string>>(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForTaskWrappedIActionResult() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NoContent() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task<Microsoft.AspNetCore.Mvc.IActionResult> GetUsersAsync(bool empty)
                {
                    await Task.Yield();
                    if (empty)
                    {
                        return NoContent(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 204.}}
                    }

                    return Ok(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForPlainIActionResultReturningSingleObject() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NoContent() => null;
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class User { }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult GetUser(bool missing)
                {
                    if (missing)
                    {
                        return NoContent();
                    }

                    return Ok(new User());
                }
            }
            """)
            .VerifyNoIssues();
}
