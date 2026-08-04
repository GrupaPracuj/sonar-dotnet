using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class EndpointsShouldNotReturnEntitiesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.EndpointsShouldNotReturnEntities>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.ComponentModel.DataAnnotations
        {
            public class KeyAttribute : System.Attribute { }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public class ActionResult<T> { }
            public abstract class ControllerBase { }
        }

        public class Order
        {
            [System.ComponentModel.DataAnnotations.Key]
            public int Id { get; set; }
        }

        public sealed class OrderResponse
        {
            public System.Guid Id { get; set; }
        }
        """;

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Order Get(int id) => null; // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForTaskOfEntity() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Threading.Tasks.Task<Order> Get(int id) => null; // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForCollectionOfEntities() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Collections.Generic.IReadOnlyList<Order> GetAll() => null; // Noncompliant {{'Order' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_NoncompliantForQueryable() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Linq.IQueryable<OrderResponse> GetAll() => null; // Noncompliant {{'IQueryable' is a database entity - return a response contract instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_CompliantForResponseContract() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Threading.Tasks.Task<OrderResponse> Get(int id) => null;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public System.Collections.Generic.IReadOnlyList<OrderResponse> GetAll() => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EndpointsShouldNotReturnEntities_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                public Order Get(int id) => null;
            }
            """)
            .VerifyNoIssues();
}
