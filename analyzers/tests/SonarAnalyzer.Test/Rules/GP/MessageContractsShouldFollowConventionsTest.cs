using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class MessageContractsShouldFollowConventionsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.MessageContractsShouldFollowConventions>();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_EventSuffix() =>
        builder.AddSnippet(
            """
            public class PaymentReceivedEvent { }

            public class AppConfig
            {
                public AppConfig Publishes<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig)
                {
                    appConfig.Publishes<PaymentReceivedEvent>(); // Noncompliant {{Rename event 'PaymentReceivedEvent' to remove the 'Event' suffix.}}
                    return appConfig;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_CommandSuffix() =>
        builder.AddSnippet(
            """
            public class AcceptOrderCommand { }

            public class AppConfig
            {
                public AppConfig Sends<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig)
                {
                    appConfig.Sends<AcceptOrderCommand>(); // Noncompliant {{Rename command 'AcceptOrderCommand' to remove the 'Command' suffix.}}
                    return appConfig;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_Behaviorful() =>
        builder.AddSnippet(
            """
            public class NotifyUser
            {
                public string Email { get; set; }

                public void SendNow() { } // Noncompliant {{Message contract 'NotifyUser' should not contain business behavior.}}
            }

            public class AppConfig
            {
                public AppConfig Sends<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig)
                {
                    appConfig.Sends<NotifyUser>();
                    return appConfig;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_RecordWithValueSemantics_Compliant() =>
        builder.WithOptions(LanguageOptions.CSharpLatest).AddSnippet(
            """
            public record PaymentReceived(string PaymentId)
            {
                public override string ToString() => PaymentId;
            }

            public class AppConfig
            {
                public AppConfig Publishes<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig) =>
                    appConfig.Publishes<PaymentReceived>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_StaticFactory_Compliant() =>
        builder.AddSnippet(
            """
            public class NotifyUser
            {
                public string Email { get; set; }

                public static NotifyUser For(string email) => new NotifyUser { Email = email };
            }

            public class AppConfig
            {
                public AppConfig Sends<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig) =>
                    appConfig.Sends<NotifyUser>();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_StaticMethodReturningSomethingElse_Noncompliant() =>
        builder.AddSnippet(
            """
            public class NotifyUser
            {
                public string Email { get; set; }

                public static bool IsValid(string email) => email.Length > 0; // Noncompliant {{Message contract 'NotifyUser' should not contain business behavior.}}
            }

            public class AppConfig
            {
                public AppConfig Sends<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig) =>
                    appConfig.Sends<NotifyUser>();
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_ReportsEachBehaviorMethodOnce() =>
        builder.AddSnippet(
            """
            public class NotifyUser
            {
                public void SendNow() { } // Noncompliant {{Message contract 'NotifyUser' should not contain business behavior.}}
                public bool CanSend() => true; // Noncompliant {{Message contract 'NotifyUser' should not contain business behavior.}}
            }

            public class AppConfig
            {
                public AppConfig Sends<T>() => this;
            }

            public static class Startup
            {
                public static void RegisterMessages(AppConfig first, AppConfig second)
                {
                    first.Sends<NotifyUser>();
                    second.Sends<NotifyUser>();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_MutableButBehaviorFree_Compliant() =>
        builder.AddSnippet(
            """
            public class NotifyUser
            {
                // Mutable properties are allowed - only business behavior (methods) is flagged.
                public string Email { get; set; }
            }

            public class AppConfig
            {
                public AppConfig Sends<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig)
                {
                    appConfig.Sends<NotifyUser>();
                    return appConfig;
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_JunoPublishEventSuffix() =>
        builder.AddSnippet(
            """
            using System.Threading;
            using System.Threading.Tasks;

            namespace GP.Juno.Abstractions.EventStream
            {
                public interface IPublisher
                {
                    Task Publish<T>(T @event, CancellationToken cancellationToken = default(CancellationToken)) where T : class;
                }
            }

            public class PaymentReceivedEvent { }

            public class Service
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public Service(GP.Juno.Abstractions.EventStream.IPublisher publisher) => _publisher = publisher;

                public Task Send() => _publisher.Publish(new PaymentReceivedEvent()); // Noncompliant {{Rename event 'PaymentReceivedEvent' to remove the 'Event' suffix.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_MassTransitSendCommandSuffix() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            namespace MassTransit
            {
                public interface ISendEndpoint
                {
                    Task Send<T>(T message) where T : class;
                }
            }

            public class AcceptOrderCommand { }

            public class Service
            {
                private readonly MassTransit.ISendEndpoint _endpoint;

                public Service(MassTransit.ISendEndpoint endpoint) => _endpoint = endpoint;

                public Task Send() => _endpoint.Send(new AcceptOrderCommand()); // Noncompliant {{Rename command 'AcceptOrderCommand' to remove the 'Command' suffix.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void MessageContractsShouldFollowConventions_Compliant() =>
        builder.AddSnippet(
            """
            public class PaymentReceived
            {
                public string PaymentId { get; }

                public PaymentReceived(string paymentId) => PaymentId = paymentId;
            }

            public class NotifyUser
            {
                public string UserId { get; }

                public NotifyUser(string userId) => UserId = userId;
            }

            public class AppConfig
            {
                public AppConfig Publishes<T>() => this;
                public AppConfig Sends<T>() => this;
            }

            public static class Startup
            {
                public static AppConfig RegisterMessages(this AppConfig appConfig)
                {
                    appConfig.Publishes<PaymentReceived>();
                    appConfig.Sends<NotifyUser>();
                    return appConfig;
                }
            }
            """)
            .VerifyNoIssues();
}
