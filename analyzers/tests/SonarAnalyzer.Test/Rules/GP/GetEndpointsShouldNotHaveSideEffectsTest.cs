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

        public class OrderConfirmed { }

        public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext { }
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
}
