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
}
