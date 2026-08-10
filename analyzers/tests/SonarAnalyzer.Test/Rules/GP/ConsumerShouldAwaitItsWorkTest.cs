using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ConsumerShouldAwaitItsWorkTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ConsumerShouldAwaitItsWork>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace MassTransit
        {
            public interface ConsumeContext<out T>
            {
                T Message { get; }
            }

            public interface IConsumer<T> where T : class
            {
                System.Threading.Tasks.Task Consume(ConsumeContext<T> context);
            }
        }

        public class OrderAccepted { }
        """;

    [TestMethod]
    public void ConsumerShouldAwaitItsWork_NoncompliantForDiscardedTask() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public System.Threading.Tasks.Task Consume(MassTransit.ConsumeContext<OrderAccepted> context)
                {
                    _ = ProcessAsync(context.Message); // Noncompliant {{Await this call - the message is acknowledged when Consume returns, so work nothing awaits is lost without a trace.}}
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                private System.Threading.Tasks.Task ProcessAsync(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .Verify();

    [TestMethod]
    public void ConsumerShouldAwaitItsWork_NoncompliantForBareStatement() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public System.Threading.Tasks.Task Consume(MassTransit.ConsumeContext<OrderAccepted> context)
                {
                    ProcessAsync(context.Message); // Noncompliant {{Await this call - the message is acknowledged when Consume returns, so work nothing awaits is lost without a trace.}}
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                private System.Threading.Tasks.Task ProcessAsync(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .Verify();

    [TestMethod]
    public void ConsumerShouldAwaitItsWork_NoncompliantForTaskRun() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public System.Threading.Tasks.Task Consume(MassTransit.ConsumeContext<OrderAccepted> context)
                {
                    System.Threading.Tasks.Task.Run(() => Notify(context.Message)); // Noncompliant {{Await this call - the message is acknowledged when Consume returns, so work nothing awaits is lost without a trace.}}
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                private void Notify(OrderAccepted message) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void ConsumerShouldAwaitItsWork_CompliantWhenAwaited() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public async System.Threading.Tasks.Task Consume(MassTransit.ConsumeContext<OrderAccepted> context)
                {
                    await ProcessAsync(context.Message);
                }

                private System.Threading.Tasks.Task ProcessAsync(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConsumerShouldAwaitItsWork_CompliantWhenReturned() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public System.Threading.Tasks.Task Consume(MassTransit.ConsumeContext<OrderAccepted> context) =>
                    ProcessAsync(context.Message);

                private System.Threading.Tasks.Task ProcessAsync(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .VerifyNoIssues();

    // A void call is not unobserved work - there is no task to lose.
    [TestMethod]
    public void ConsumerShouldAwaitItsWork_CompliantForVoidCall() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public System.Threading.Tasks.Task Consume(MassTransit.ConsumeContext<OrderAccepted> context)
                {
                    Notify(context.Message);
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                private void Notify(OrderAccepted message) { }
            }
            """)
            .VerifyNoIssues();

    // Outside a consumer there is no broker acknowledgement to lose, so the rule stays out of it.
    [TestMethod]
    public void ConsumerShouldAwaitItsWork_CompliantOutsideConsumer() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                public void Process(OrderAccepted message)
                {
                    _ = ProcessAsync(message);
                }

                private System.Threading.Tasks.Task ProcessAsync(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConsumerShouldAwaitItsWork_CompliantForUnrelatedConsumeOverload() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                public System.Threading.Tasks.Task Consume(MassTransit.ConsumeContext<OrderAccepted> context) =>
                    System.Threading.Tasks.Task.CompletedTask;

                public void Consume(string value)
                {
                    ProcessAsync(value);
                }

                private System.Threading.Tasks.Task ProcessAsync(string value) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ConsumerShouldAwaitItsWork_NoncompliantForExplicitImplementation() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer : MassTransit.IConsumer<OrderAccepted>
            {
                System.Threading.Tasks.Task MassTransit.IConsumer<OrderAccepted>.Consume(MassTransit.ConsumeContext<OrderAccepted> context)
                {
                    ProcessAsync(context.Message); // Noncompliant {{Await this call - the message is acknowledged when Consume returns, so work nothing awaits is lost without a trace.}}
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                private System.Threading.Tasks.Task ProcessAsync(OrderAccepted message) => System.Threading.Tasks.Task.CompletedTask;
            }
            """)
            .Verify();
}
