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

public interface IPrincipal
{
    bool IsInRole(string role);
}

namespace Tests.Diagnostics
{
    public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public IPrincipal User { get; }

        public Microsoft.AspNetCore.Mvc.IActionResult DeleteUser()
        {
            if (!User.IsInRole("Admin"))
            {
                return Forbid(); // Fixed
            }

            return Ok();
        }
    }
}
