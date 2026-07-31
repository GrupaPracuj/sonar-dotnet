using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class GetCollectionEndpointsShouldNotReturnNotFoundTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.GetCollectionEndpointsShouldNotReturnNotFound>()
        .AddReferences(MetadataReferenceFacade.SystemThreadingTasks);

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForNotFound() =>
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
                    protected ActionResult<T> NotFound<T>() => null;
                    protected ActionResult<T> StatusCode<T>(int code) => null;
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> GetUsers()
                {
                    return NotFound<System.Collections.Generic.IReadOnlyList<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForStatusCode404() =>
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
                    protected ActionResult<T> NotFound<T>() => null;
                    protected ActionResult<T> StatusCode<T>(int code) => null;
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IEnumerable<string>> GetUsers()
                {
                    return StatusCode<System.Collections.Generic.IEnumerable<string>>(404); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForOkEmptyCollection() =>
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
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForNonGetMethod() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>> DeleteUsers()
                {
                    return NotFound<System.Collections.Generic.IReadOnlyList<string>>();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForArrayReturnType() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public class ActionResult<T> : IActionResult { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<string[]> GetUsers()
                {
                    return NotFound<string[]>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForConcreteListReturnType() =>
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
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<List<string>> GetUsers()
                {
                    return NotFound<List<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForValueTaskWrappedActionResult() =>
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
                    protected ActionResult<T> NotFound<T>() => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async ValueTask<Microsoft.AspNetCore.Mvc.ActionResult<IEnumerable<string>>> GetUsersAsync()
                {
                    await Task.Yield();
                    return NotFound<IEnumerable<string>>(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                }
            }
            """)
            .WithOptions(LanguageOptions.FromCSharp8)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForPlainIActionResult() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
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
                        return NotFound(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                    }

                    return Ok(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForPlainIActionResultWithExplicitGenericOk() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
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
                        return NotFound(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                    }

                    return Ok<List<string>>(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_NoncompliantForTaskWrappedIActionResult() =>
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
                    protected IActionResult NotFound() => null;
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
                        return NotFound(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
                    }

                    return Ok(new List<string>());
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNotFound_CompliantForPlainIActionResultReturningSingleObject() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult NotFound() => null;
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
                        return NotFound();
                    }

                    return Ok(new User());
                }
            }
            """)
            .VerifyNoIssues();
}
