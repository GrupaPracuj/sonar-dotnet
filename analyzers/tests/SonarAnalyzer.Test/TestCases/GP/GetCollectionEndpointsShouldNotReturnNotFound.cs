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
        public Microsoft.AspNetCore.Mvc.IActionResult GetUsers(bool empty)
        {
            if (empty)
            {
                return NotFound(); // Noncompliant {{GET endpoints returning collections should return 200 with an empty collection instead of 404.}}
            }

            return Ok(new List<string>());
        }
    }
}
