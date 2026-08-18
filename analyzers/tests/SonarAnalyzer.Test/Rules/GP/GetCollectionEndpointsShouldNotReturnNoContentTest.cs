/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class GetCollectionEndpointsShouldNotReturnNoContentTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.GetCollectionEndpointsShouldNotReturnNoContent>()
        .AddReferences(MetadataReferenceFacade.SystemThreadingTasks);

    private const string MinimalApiStubs =
        """
        global using Microsoft.AspNetCore.Builder;

        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public sealed class RouteHandlerBuilder { }

            public static class EndpointRouteBuilderExtensions
            {
                public static RouteHandlerBuilder MapGet<T>(
                    this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints,
                    string pattern,
                    System.Func<T> handler) => null;
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }

            public static class Results
            {
                public static IResult NoContent() => null;
                public static IResult StatusCode(int statusCode) => null;
                public static IResult Ok<T>(T value) => null;
            }

            public static class TypedResults
            {
                public static IResult NoContent() => null;
                public static IResult Ok<T>(T value) => null;
            }
        }
        """;

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
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForScalarString() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public class ActionResult<T> { }
                public abstract class ControllerBase
                {
                    protected ActionResult<T> NoContent<T>() => null;
                    protected ActionResult<T> Ok<T>(T value) => null;
                }
            }

            public class SettingsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<string> GetSetting(bool exists) =>
                    exists ? Ok("value") : NoContent<string>();
            }
            """)
            .VerifyNoIssues();

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

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForMinimalApiResultsBlockLambda() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool empty) =>
                    app.MapGet("/customers/{customerId}/users", () =>
                    {
                        if (empty)
                        {
                            return Microsoft.AspNetCore.Http.Results.NoContent(); // Noncompliant
                        }

                        return Microsoft.AspNetCore.Http.Results.Ok(new System.Collections.Generic.List<string>());
                    });
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForMinimalApiTypedResultsExpressionLambda() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool empty) =>
                    app.MapGet("/users", () => empty
                        ? Microsoft.AspNetCore.Http.TypedResults.NoContent() // Noncompliant
                        : Microsoft.AspNetCore.Http.TypedResults.Ok(new string[0]));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_NoncompliantForMinimalApiStatusCode204() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool empty) =>
                    app.MapGet("/users", () => empty
                        ? Microsoft.AspNetCore.Http.Results.StatusCode(204) // Noncompliant
                        : Microsoft.AspNetCore.Http.Results.Ok(new string[0]));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .Verify();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForMinimalApiReturningSingleObject() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public sealed class User { }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, bool empty) =>
                    app.MapGet("/users/{id}", () => empty
                        ? Microsoft.AspNetCore.Http.Results.NoContent()
                        : Microsoft.AspNetCore.Http.Results.Ok(new User()));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForLookalikeMapGet() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public sealed class CustomApp
            {
                public void MapGet<T>(string pattern, System.Func<T> handler) { }
            }

            public static class Endpoints
            {
                public static void Map(CustomApp app, bool empty) =>
                    app.MapGet("/users", () => empty
                        ? Microsoft.AspNetCore.Http.Results.NoContent()
                        : Microsoft.AspNetCore.Http.Results.Ok(new string[0]));
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForNamedMethodHandler() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                    app.MapGet("/users", Handle);

                private static Microsoft.AspNetCore.Http.IResult Handle()
                {
                    if (System.DateTime.Now.Ticks == 0)
                    {
                        return Microsoft.AspNetCore.Http.Results.NoContent();
                    }

                    return Microsoft.AspNetCore.Http.Results.Ok(new string[0]);
                }
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForNestedFunctions() =>
        builder.AddSnippet(
            MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                    app.MapGet("/users", () =>
                    {
                        Microsoft.AspNetCore.Http.IResult Local() => Microsoft.AspNetCore.Http.Results.NoContent();
                        System.Func<Microsoft.AspNetCore.Http.IResult> nested =
                            () => Microsoft.AspNetCore.Http.TypedResults.NoContent();
                        return Microsoft.AspNetCore.Http.Results.Ok(new string[0]);
                    });
            }
            """)
            .WithOptions(LanguageOptions.CSharpLatest)
            .VerifyNoIssues();

    // "NoContent" is resolved to ControllerBase: a same-named helper on the controller itself is not the MVC 204 factory.
    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CompliantForLookalikeNoContent() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase
                {
                    protected IActionResult Ok<T>(T value) => null;
                }
            }

            public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private static IEnumerable<string> NoContent() => new string[0];

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public IEnumerable<string> GetUsers() => NoContent();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetCollectionEndpointsShouldNotReturnNoContent_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("GetCollectionEndpointsShouldNotReturnNoContent.cs")
            .WithCodeFix<CS.GetCollectionEndpointsShouldNotReturnNoContentCodeFix>()
            .WithCodeFixedPaths("GetCollectionEndpointsShouldNotReturnNoContent.Fixed.cs")
            .VerifyCodeFix();
}
