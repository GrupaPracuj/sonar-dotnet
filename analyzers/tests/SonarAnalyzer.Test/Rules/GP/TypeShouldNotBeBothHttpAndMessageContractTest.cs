using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class TypeShouldNotBeBothHttpAndMessageContractTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.TypeShouldNotBeBothHttpAndMessageContract>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
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

        public class OrderRequest
        {
            public System.Guid OrderId { get; set; }
        }

        public sealed record OrderAccepted(System.Guid OrderId);
        """;

    [TestMethod]
    public void TypeShouldNotBeBothHttpAndMessageContract_NoncompliantForSharedType() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                [Microsoft.AspNetCore.Mvc.HttpPost]
                public async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> Create(OrderRequest request)
                {
                    await _publisher.Publish(request); // Noncompliant {{'OrderRequest' is also an HTTP contract - declare a separate message contract.}}
                    return Ok();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void TypeShouldNotBeBothHttpAndMessageContract_CompliantForSeparateTypes() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                [Microsoft.AspNetCore.Mvc.HttpPost]
                public async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> Create(OrderRequest request)
                {
                    await _publisher.Publish(new OrderAccepted(request.OrderId));
                    return Ok();
                }
            }
            """)
            .VerifyNoIssues();

    // The shared type is found through the compilation's symbols, so the publish does not have to sit in the
    // controller - which is also what makes the result independent of which file is analyzed first.
    [TestMethod]
    public void TypeShouldNotBeBothHttpAndMessageContract_NoncompliantWhenPublishedElsewhere() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult Create(OrderRequest request) => Ok();
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(OrderRequest request) =>
                    _publisher.Publish(request); // Noncompliant {{'OrderRequest' is also an HTTP contract - declare a separate message contract.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void TypeShouldNotBeBothHttpAndMessageContract_NoncompliantForResponseType() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public System.Threading.Tasks.Task<OrderAccepted> Create() => null;
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(OrderAccepted @event) =>
                    _publisher.Publish(@event); // Noncompliant {{'OrderAccepted' is also an HTTP contract - declare a separate message contract.}}
            }
            """)
            .Verify();

    // An action also takes CancellationToken and Guid; those must not end up in the HTTP contract set.
    [TestMethod]
    public void TypeShouldNotBeBothHttpAndMessageContract_CompliantForFrameworkParameterTypes() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult Create(System.Guid id, System.Threading.CancellationToken cancellationToken) => Ok();
            }

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new OrderAccepted(id));
            }
            """)
            .VerifyNoIssues();

    // Publishing a type no endpoint exchanges is fine, whichever file the publish happens to be in.
    [TestMethod]
    public void TypeShouldNotBeBothHttpAndMessageContract_CompliantWithoutAnyController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(OrderRequest request) =>
                    _publisher.Publish(request);
            }
            """)
            .VerifyNoIssues();
}
