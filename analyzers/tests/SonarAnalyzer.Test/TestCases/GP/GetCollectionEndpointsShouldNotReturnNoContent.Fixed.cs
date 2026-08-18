using System.Collections.Generic;

namespace Microsoft.AspNetCore.Mvc
{
    public class HttpGetAttribute : System.Attribute { }
    public interface IActionResult { }
    public abstract class ControllerBase
    {
        protected IActionResult NoContent() => null;
        protected IActionResult Ok(object value) => null;
    }
}

namespace Tests.Diagnostics
{
    public class UsersController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult GetUsers(bool empty)
        {
            if (empty)
            {
                return Ok(System.Array.Empty<object>()); // Fixed
            }

            return Ok(new List<string>());
        }
    }
}
