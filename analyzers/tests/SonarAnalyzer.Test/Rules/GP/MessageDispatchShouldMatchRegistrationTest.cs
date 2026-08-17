using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class MessageDispatchShouldMatchRegistrationTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.MessageDispatchShouldMatchRegistration>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace GP.Juno.Configuration
        {
            public class AppConfig { }
        }

        namespace GP.Juno.EventStream.Api
        {
            public static class AppConfigExtensions
            {
                public static GP.Juno.Configuration.AppConfig Sends<T>(this GP.Juno.Configuration.AppConfig config) => config;
                public static GP.Juno.Configuration.AppConfig Publishes<T>(this GP.Juno.Configuration.AppConfig config) => config;
            }
        }

        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T message);
            }
        }

        namespace GP.Juno.Abstractions.Messaging
        {
            public interface IMessageSender
            {
                System.Threading.Tasks.Task Send<T>(T message);
            }
        }

        public sealed record RebuildIndex(string IndexName);
        public sealed record IndexRebuilt(string IndexName);

        """;

    [TestMethod]
    public void MessageDispatchShouldMatchRegistration_NoncompliantForPublishingSentCommand() =>
        builder.AddSnippet(
            Stubs + """
            public class Startup
            {
                public void Configure(GP.Juno.Configuration.AppConfig config) =>
                    GP.Juno.EventStream.Api.AppConfigExtensions.Sends<RebuildIndex>(config);
            }

            public class IndexService
            {
                public System.Threading.Tasks.Task Rebuild(GP.Juno.Abstractions.EventStream.IPublisher publisher) =>
                    publisher.Publish(new RebuildIndex("offers")); // Noncompliant {{'RebuildIndex' is registered with 'Sends' but dispatched with 'Publish'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageDispatchShouldMatchRegistration_NoncompliantForSendingPublishedEvent() =>
        builder.AddSnippet(
            Stubs + """
            public class Startup
            {
                public void Configure(GP.Juno.Configuration.AppConfig config) =>
                    GP.Juno.EventStream.Api.AppConfigExtensions.Publishes<IndexRebuilt>(config);
            }

            public class IndexService
            {
                public System.Threading.Tasks.Task Notify(GP.Juno.Abstractions.Messaging.IMessageSender sender) =>
                    sender.Send(new IndexRebuilt("offers")); // Noncompliant {{'IndexRebuilt' is registered with 'Publishes' but dispatched with 'Send'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageDispatchShouldMatchRegistration_CompliantForMatchingDispatch() =>
        builder.AddSnippet(
            Stubs + """
            public class Startup
            {
                public void Configure(GP.Juno.Configuration.AppConfig config)
                {
                    GP.Juno.EventStream.Api.AppConfigExtensions.Sends<RebuildIndex>(config);
                    GP.Juno.EventStream.Api.AppConfigExtensions.Publishes<IndexRebuilt>(config);
                }
            }

            public class IndexService
            {
                public System.Threading.Tasks.Task Rebuild(
                    GP.Juno.Abstractions.Messaging.IMessageSender sender,
                    RebuildIndex command) =>
                    sender.Send(command);

                public System.Threading.Tasks.Task Notify(
                    GP.Juno.Abstractions.EventStream.IPublisher publisher,
                    IndexRebuilt @event) =>
                    publisher.Publish(@event);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MessageDispatchShouldMatchRegistration_DoesNotGuessWithoutRegistration() =>
        builder.AddSnippet(
            Stubs + """
            public class IndexService
            {
                public System.Threading.Tasks.Task Rebuild(
                    GP.Juno.Abstractions.EventStream.IPublisher publisher,
                    RebuildIndex command) =>
                    publisher.Publish(command);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MessageDispatchShouldMatchRegistration_CompliantWhenBothSemanticsAreExplicitlyRegistered() =>
        builder.AddSnippet(
            Stubs + """
            public class Startup
            {
                public void Configure(GP.Juno.Configuration.AppConfig config)
                {
                    GP.Juno.EventStream.Api.AppConfigExtensions.Sends<RebuildIndex>(config);
                    GP.Juno.EventStream.Api.AppConfigExtensions.Publishes<RebuildIndex>(config);
                }
            }

            public class IndexService
            {
                public System.Threading.Tasks.Task Rebuild(
                    GP.Juno.Abstractions.EventStream.IPublisher publisher,
                    RebuildIndex command) =>
                    publisher.Publish(command);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MessageDispatchShouldMatchRegistration_IgnoresUnrelatedMethods() =>
        builder.AddSnippet(
            Stubs + """
            public class LocalBus
            {
                public void Sends<T>() { }
                public void Publish<T>(T value) { }
            }

            public class IndexService
            {
                public void Rebuild(LocalBus bus)
                {
                    bus.Sends<RebuildIndex>();
                    bus.Publish(new RebuildIndex("offers"));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MessageDispatchShouldMatchRegistration_RecognizesExtensionByJunoReceiverType() =>
        builder.AddSnippet(
            """
            using Company.Configuration;

            namespace GP.Juno.Configuration
            {
                public class AppConfig { }
            }

            namespace Company.Configuration
            {
                public static class MessagingExtensions
                {
                    public static GP.Juno.Configuration.AppConfig Sends<T>(
                        this GP.Juno.Configuration.AppConfig config) => config;
                }
            }

            namespace GP.Juno.Abstractions.EventStream
            {
                public interface IPublisher
                {
                    System.Threading.Tasks.Task Publish<T>(T message);
                }
            }

            public sealed record RebuildIndex(string IndexName);

            public class Startup
            {
                public void Configure(GP.Juno.Configuration.AppConfig config) =>
                    config.Sends<RebuildIndex>();
            }

            public class IndexService
            {
                public System.Threading.Tasks.Task Rebuild(
                    GP.Juno.Abstractions.EventStream.IPublisher publisher) =>
                    publisher.Publish(new RebuildIndex("offers")); // Noncompliant
            }
            """)
            .Verify();
}
