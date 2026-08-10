using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class GetEndpointsShouldNotHaveSideEffectsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.GetEndpointsShouldNotHaveSideEffects>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public int SaveChanges() => 0;
                public System.Threading.Tasks.Task<int> SaveChangesAsync() => null;
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public class HttpPostAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
            }
        }

        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
            }
        }

        namespace MassTransit
        {
            public interface IPublishEndpoint
            {
                System.Threading.Tasks.Task Publish<T>(T message) where T : class;
            }
        }

        public class OrderConfirmed { }

        public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext { }
        """;

    private const string MinimalApiStubs =
        """
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static void MapGet<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
                public static void MapPost<T>(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern, System.Func<T> handler) { }
            }
        }
        """;

    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_NoncompliantForSaveChanges() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly ShopDbContext _context;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Confirm(int id)
                {
                    _context.SaveChanges(); // Noncompliant {{A GET endpoint should not change state - 'SaveChanges' makes this endpoint unsafe to retry, prefetch or cache.}}
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_NoncompliantForPublish() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Confirm(int id)
                {
                    _publisher.Publish(new OrderConfirmed()); // Noncompliant {{A GET endpoint should not change state - 'Publish' makes this endpoint unsafe to retry, prefetch or cache.}}
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_CompliantForPostEndpoint() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly ShopDbContext _context;

                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult Confirm(int id)
                {
                    _context.SaveChanges();
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_CompliantForReadOnlyGet() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id) => Ok();
            }
            """)
            .VerifyNoIssues();

    // "Add" only counts on an EF target, not on any list that happens to have the method.
    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_CompliantForListAdd() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id)
                {
                    var names = new System.Collections.Generic.List<string>();
                    names.Add("order");
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_CompliantForEntityFrameworkTypeNameLookalikes() =>
        builder.AddSnippet(
            Stubs + """

            namespace Shop
            {
                public class DbSet
                {
                    public void Add(object value) { }
                }

                public static class EntityFrameworkQueryableExtensions
                {
                    public static void ExecuteDelete() { }
                }

                public static class RelationalDatabaseFacadeExtensions
                {
                    public static void ExecuteUpdate() { }
                }
            }

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly Shop.DbSet values;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get()
                {
                    values.Add(new object());
                    Shop.EntityFrameworkQueryableExtensions.ExecuteDelete();
                    Shop.RelationalDatabaseFacadeExtensions.ExecuteUpdate();
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_MinimalApiNoncompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            public static class Endpoints
            {
                public static void Map(
                    Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app,
                    ShopDbContext context,
                    MassTransit.IPublishEndpoint publisher)
                {
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/save", () =>
                    {
                        context.SaveChanges(); // Noncompliant
                        return "saved";
                    });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/publish", () =>
                    {
                        publisher.Publish(new OrderConfirmed()); // Noncompliant
                        return "published";
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void GetEndpointsShouldNotHaveSideEffects_MinimalApiBoundariesAreCompliant() =>
        builder.AddSnippet(
            Stubs + MinimalApiStubs + """

            namespace Custom
            {
                public static class Endpoints
                {
                    public static void MapGet<T>(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string pattern, System.Func<T> handler) { }
                }
            }

            public static class Endpoints
            {
                public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, ShopDbContext context)
                {
                    context.SaveChanges();
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(app, "/save", () =>
                    {
                        context.SaveChanges();
                        return "saved";
                    });
                    Custom.Endpoints.MapGet(app, "/custom", () =>
                    {
                        context.SaveChanges();
                        return "saved";
                    });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/nested", () =>
                    {
                        System.Action nested = () => context.SaveChanges();
                        return "read";
                    });
                    Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/local", () =>
                    {
                        void Save() => context.SaveChanges();
                        return "read";
                    });
                }
            }
            """)
            .VerifyNoIssues();
}
