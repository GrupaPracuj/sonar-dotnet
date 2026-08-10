using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ControllersShouldNotUseDbContextDirectlyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ControllersShouldNotUseDbContextDirectly>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult Ok(object value) => null;
            }
        }

        public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext { }

        public interface IOrderRepository
        {
            object Find(int id);
        }
        """;

    [TestMethod]
    public void ControllersShouldNotUseDbContextDirectly_NoncompliantForFieldAndConstructorParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly ShopDbContext _context; // Noncompliant {{Do not use 'ShopDbContext' in a controller - reach data through a service or repository instead.}}

                public OrdersController(ShopDbContext context) => // Noncompliant {{Do not use 'ShopDbContext' in a controller - reach data through a service or repository instead.}}
                    _context = context;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id) => Ok(_context);
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllersShouldNotUseDbContextDirectly_NoncompliantForLocalVariable() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id)
                {
                    ShopDbContext context = new ShopDbContext(); // Noncompliant {{Do not use 'ShopDbContext' in a controller - reach data through a service or repository instead.}}
                    return Ok(context);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllersShouldNotUseDbContextDirectly_NoncompliantForPrimaryConstructorParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController(ShopDbContext context) : Microsoft.AspNetCore.Mvc.ControllerBase // Noncompliant {{Do not use 'ShopDbContext' in a controller - reach data through a service or repository instead.}}
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id) => Ok(context);
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllersShouldNotUseDbContextDirectly_CompliantForRepository() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly IOrderRepository _orders;

                public OrdersController(IOrderRepository orders) =>
                    _orders = orders;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id) => Ok(_orders.Find(id));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ControllersShouldNotUseDbContextDirectly_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository : IOrderRepository
            {
                private readonly ShopDbContext _context;

                public OrderRepository(ShopDbContext context) =>
                    _context = context;

                public object Find(int id) => _context;
            }
            """)
            .VerifyNoIssues();
}
