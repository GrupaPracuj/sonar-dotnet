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
    }
}
