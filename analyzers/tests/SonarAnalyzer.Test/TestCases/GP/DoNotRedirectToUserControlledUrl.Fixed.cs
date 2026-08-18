using static Microsoft.AspNetCore.Http.Results;

namespace Microsoft.AspNetCore.Mvc
{
    public class HttpGetAttribute : System.Attribute { }
    public interface IActionResult { }

    public interface IUrlHelper
    {
        bool IsLocalUrl(string url);
    }

    public abstract class ControllerBase
    {
        public IUrlHelper Url { get; set; }
        protected IActionResult Redirect(string url) => null;
        protected IActionResult RedirectPermanent(string url) => null;
        protected IActionResult LocalRedirect(string localUrl) => null;
        protected IActionResult LocalRedirectPermanent(string localUrl) => null;
        protected IActionResult RedirectToAction(string action) => null;
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
        public static void MapGet<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T, Microsoft.AspNetCore.Http.IResult> handler) { }
    }
}

namespace Microsoft.AspNetCore.Http
{
    public interface IResult { }

    public static class Results
    {
        public static IResult Redirect(string url) => null;
    }
}

namespace Tests.Diagnostics
{
    public class AccountController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult LogOn(string returnUrl) =>
            LocalRedirect(returnUrl); // Fixed

        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult LogOnPermanent(string returnUrl) =>
            LocalRedirectPermanent(returnUrl); // Fixed

        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult LogOnGuarded(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index");
        }
    }

    public static class Endpoints
    {
        public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
            Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/login",
                (string returnUrl) => Redirect(returnUrl)); // Fixed
    }
}
