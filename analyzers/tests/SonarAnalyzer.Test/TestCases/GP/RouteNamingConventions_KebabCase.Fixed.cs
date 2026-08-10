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
    public class HttpGetAttribute : System.Attribute, Routing.IRouteTemplateProvider
    {
        public HttpGetAttribute(string template) => Template = template;
        public string Template { get; }
        public int? Order { get; set; }
        public string Name { get; set; }
    }
}

public class JobOffersController
{
    [Microsoft.AspNetCore.Mvc.HttpGet("job-offers")] // Fixed
    public void GetAll() { }

    // The offending segment is not the first one: the fix has to rename the segment the issue points at.
    [Microsoft.AspNetCore.Mvc.HttpGet("api/by-recruiter")] // Fixed
    public void GetByRecruiter() { }
}
