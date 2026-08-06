namespace Microsoft.AspNetCore.Mvc.Routing
{
    public interface IRouteTemplateProvider
    {
        string Template { get; }
        int? Order { get; }
        string Name { get; }
    }
}

namespace Microsoft.AspNetCore.Mvc
{
    public class RouteAttribute : System.Attribute, Routing.IRouteTemplateProvider
    {
        public RouteAttribute(string template) => Template = template;
        public string Template { get; }
        public int? Order { get; set; }
        public string Name { get; set; }
    }
}

public class UsersController
{
    [Microsoft.AspNetCore.Mvc.Route("api/users")] // Fixed
    public void GetAll() { }
}
