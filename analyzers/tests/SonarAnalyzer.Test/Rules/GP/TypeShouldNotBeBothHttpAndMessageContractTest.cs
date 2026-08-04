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
