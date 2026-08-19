using System.Collections.Generic;

namespace Microsoft.AspNetCore.Mvc
{
    public class HttpGetAttribute : System.Attribute { }
    public interface IActionResult { }
    public abstract class ControllerBase
    {
        protected IActionResult NotFound() => null;
        protected IActionResult Ok(object value) => null;
    }
}

namespace Tests.Diagnostics
{
    public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult GetUsers()
        {
            var users = new List<string>();
            if (users.Count == 0)
            {
                return Ok(System.Array.Empty<object>()); // Fixed
            }

            return Ok(users);
        }
    }
}
