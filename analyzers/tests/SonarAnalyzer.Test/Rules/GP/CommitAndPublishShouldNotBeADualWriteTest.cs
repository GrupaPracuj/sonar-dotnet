using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class CommitAndPublishShouldNotBeADualWriteTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CommitAndPublishShouldNotBeADualWrite>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default) => null;
                public int SaveChanges() => 0;
            }
        }

        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event, System.Threading.CancellationToken cancellationToken = default) where T : class;
            }
        }

        public interface IOutbox
        {
            System.Threading.Tasks.Task Publish<T>(T @event, System.Threading.CancellationToken cancellationToken = default) where T : class;
        }

        public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext { }

        public class OrderAccepted { }
        """;

    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_NoncompliantForPublishAfterCommit() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly ShopDbContext _context;
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public async System.Threading.Tasks.Task Accept(System.Threading.CancellationToken cancellationToken)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    await _publisher.Publish(new OrderAccepted(), cancellationToken); // Noncompliant {{This publish follows a database commit with no outbox - if it fails, the data has changed and nobody was told.}}
                }
            }
            """)
            .Verify();

    // A publish before the commit is GP0008's case, so the two rules never report the same statement.
    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_CompliantForPublishBeforeCommit() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly ShopDbContext _context;
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public async System.Threading.Tasks.Task Accept(System.Threading.CancellationToken cancellationToken)
                {
                    await _publisher.Publish(new OrderAccepted(), cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_NoncompliantAcrossControlFlow() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly ShopDbContext _context;
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public async System.Threading.Tasks.Task Accept(bool notify, System.Threading.CancellationToken cancellationToken)
                {
                    await _context.SaveChangesAsync(cancellationToken);

                    if (notify)
                    {
                        await _publisher.Publish(new OrderAccepted(), cancellationToken); // Noncompliant {{This publish follows a database commit with no outbox - if it fails, the data has changed and nobody was told.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_CompliantForDisjointBranches() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly ShopDbContext _context;
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public async System.Threading.Tasks.Task Accept(bool save, System.Threading.CancellationToken cancellationToken)
                {
                    if (save)
                    {
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        await _publisher.Publish(new OrderAccepted(), cancellationToken);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_CompliantForUninvokedNestedFunctions() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly ShopDbContext _context;
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public async System.Threading.Tasks.Task Accept(System.Threading.CancellationToken cancellationToken)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    System.Func<System.Threading.Tasks.Task> publishLater =
                        () => _publisher.Publish(new OrderAccepted(), cancellationToken);

                    async System.Threading.Tasks.Task PublishLater()
                    {
                        await _publisher.Publish(new OrderAccepted(), cancellationToken);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_CompliantForCommitOnly() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly ShopDbContext _context;

                public System.Threading.Tasks.Task<int> Accept(System.Threading.CancellationToken cancellationToken) =>
                    _context.SaveChangesAsync(cancellationToken);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_CompliantForPublishOnly() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Threading.CancellationToken cancellationToken) =>
                    _publisher.Publish(new OrderAccepted(), cancellationToken);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CommitAndPublishShouldNotBeADualWrite_CompliantInsideConfiguredOutbox() =>
        CreateBuilderWithOutbox("IOutbox")
            .AddSnippet(
            Stubs + """

            public class OutboxPublisher : IOutbox
            {
                private readonly ShopDbContext _context;
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public async System.Threading.Tasks.Task Publish<T>(T @event, System.Threading.CancellationToken cancellationToken = default) where T : class
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    await _publisher.Publish(@event, cancellationToken);
                }
            }
            """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilderWithOutbox(string outboxTypes) =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.CommitAndPublishShouldNotBeADualWrite { OutboxTypes = outboxTypes })
            .WithOptions(LanguageOptions.CSharpLatest);
}
