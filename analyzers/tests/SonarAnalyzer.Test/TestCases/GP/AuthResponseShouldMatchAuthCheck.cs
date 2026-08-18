namespace Microsoft.AspNetCore.Mvc
{
    public interface IActionResult { }
    public abstract class ControllerBase
    {
        protected IActionResult Unauthorized() => null;
        protected IActionResult Forbid() => null;
        protected IActionResult Ok() => null;
    }
}

namespace Microsoft.AspNetCore.Http
{
    public interface IResult { }

    public static class Results
    {
        public static IResult Unauthorized() => null;
        public static IResult Forbid() => null;
        public static IResult Ok() => null;
    }

    public static class TypedResults
    {
        public static HttpResults.Ok Ok() => null;
        public static HttpResults.ForbidHttpResult Forbid() => null;
        public static HttpResults.UnauthorizedHttpResult Unauthorized() => null;
    }
}

namespace Microsoft.AspNetCore.Http.HttpResults
{
    public sealed class Ok { }
    public sealed class ForbidHttpResult { }
    public sealed class UnauthorizedHttpResult { }

    public sealed class Results<T1, T2>
    {
        public static implicit operator Results<T1, T2>(T1 value) => null;
        public static implicit operator Results<T1, T2>(T2 value) => null;
    }
}

namespace Tests.Diagnostics
{
    public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public System.Security.Claims.ClaimsPrincipal User { get; }

        public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
        {
            if (!User.IsInRole("Admin"))
            {
                return Unauthorized(); // Noncompliant {{This looks like a permission check; return 403 (Forbid) instead of 401 (Unauthorized).}}
            }

            return Ok();
        }
    }

    // The Minimal API factory has no bare "Forbid()" counterpart, so the receiver has to survive the fix.
    public class Endpoint
    {
        public Microsoft.AspNetCore.Http.IResult DeleteUser(System.Security.Claims.ClaimsPrincipal user)
        {
            if (!user.Identity.IsAuthenticated)
            {
                return Microsoft.AspNetCore.Http.Results.Forbid(); // Noncompliant {{This looks like an authentication check; return 401 (Unauthorized) instead of 403 (Forbid).}}
            }

            return Microsoft.AspNetCore.Http.Results.Ok();
        }

        public Microsoft.AspNetCore.Http.HttpResults.Results<
            Microsoft.AspNetCore.Http.HttpResults.Ok,
            Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult> NamedHandler(System.Security.Claims.ClaimsPrincipal user)
        {
            if (!user.Identity.IsAuthenticated)
            {
                return Microsoft.AspNetCore.Http.TypedResults.Forbid(); // Noncompliant
            }

            return Microsoft.AspNetCore.Http.TypedResults.Ok();
        }

        // Minimal API handlers are usually async, so the union has to be recognized through the Task that wraps it.
        public async System.Threading.Tasks.Task<Microsoft.AspNetCore.Http.HttpResults.Results<
            Microsoft.AspNetCore.Http.HttpResults.Ok,
            Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>> AsyncNamedHandler(System.Security.Claims.ClaimsPrincipal user)
        {
            await System.Threading.Tasks.Task.CompletedTask;
            if (!user.Identity.IsAuthenticated)
            {
                return Microsoft.AspNetCore.Http.TypedResults.Forbid(); // Noncompliant
            }

            return Microsoft.AspNetCore.Http.TypedResults.Ok();
        }
    }
}
