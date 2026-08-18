using Microsoft.AspNetCore.Builder;

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

namespace Microsoft.AspNetCore.Routing
{
    public interface IEndpointRouteBuilder { }
}

namespace Microsoft.AspNetCore.Builder
{
    public static class EndpointRouteBuilderExtensions
    {
        public static void MapDelete<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
    }
}

namespace Microsoft.AspNetCore.Http
{
    public interface IResult { }

    public static class Results
    {
        public static IResult Ok<T>(T value) => null;
        public static IResult NoContent() => null;
    }

    public static class TypedResults
    {
        public static IResult Ok<T>(T value) => null;
        public static IResult NoContent() => null;
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
            return NoContent(); // Fixed
        }

        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult GetOrder(int id) => Ok(new { Id = id });
    }

    public static class Endpoints
    {
        public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
        {
            app.MapDelete("/orders/1", () => Microsoft.AspNetCore.Http.Results.NoContent()); // Fixed
            app.MapDelete("/orders/2", () => Microsoft.AspNetCore.Http.TypedResults.NoContent()); // Fixed
        }
    }
}
