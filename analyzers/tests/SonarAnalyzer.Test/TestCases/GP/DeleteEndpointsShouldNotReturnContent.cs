namespace Microsoft.AspNetCore.Mvc
{
    public class HttpDeleteAttribute : System.Attribute { }
    public class HttpGetAttribute : System.Attribute { }
    public interface IActionResult { }
    public abstract class ControllerBase
    {
        protected IActionResult Ok() => null;
        protected IActionResult Ok(object value) => null;
        protected IActionResult NoContent() => null;
    }
}

namespace Tests.Diagnostics
{
    public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpDelete]
        public Microsoft.AspNetCore.Mvc.IActionResult DeleteOrder(int id)
        {
            var deleted = new { Id = id };
            return Ok(deleted); // Noncompliant {{DELETE endpoints should return 204 (NoContent) instead of 200 with a response body.}}
        }

        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult GetOrder(int id) => Ok(new { Id = id });
    }
}
