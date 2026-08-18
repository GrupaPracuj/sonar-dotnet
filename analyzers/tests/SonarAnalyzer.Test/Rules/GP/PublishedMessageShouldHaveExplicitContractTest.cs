/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublishedMessageShouldHaveExplicitContractTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PublishedMessageShouldHaveExplicitContract>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
                System.Threading.Tasks.Task Publish(object @event);
            }
        }

        namespace GP.Juno.Abstractions
        {
            public class AppConfig { }
        }

        namespace GP.Juno.EventStream
        {
            public static class AppConfigExtensions
            {
                public static GP.Juno.Abstractions.AppConfig Publishes<T>(this GP.Juno.Abstractions.AppConfig config) => config;
            }
        }

        public sealed record OrderAccepted(System.Guid OrderId);
        """;

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_NoncompliantForAnonymousType() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new { OrderId = id }); // Noncompliant {{Publish a declared contract type instead of an anonymous type.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_NoncompliantForDictionary() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new System.Collections.Generic.Dictionary<string, object> { ["orderId"] = id }); // Noncompliant {{Publish a declared contract type instead of a loose dictionary.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_NoncompliantForObject() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(object payload) =>
                    _publisher.Publish(payload); // Noncompliant {{Publish a declared contract type instead of 'object'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_NoncompliantForShapelessPublishDeclaration() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                public void Configure(GP.Juno.Abstractions.AppConfig config) =>
                    GP.Juno.EventStream.AppConfigExtensions.Publishes<object>(config); // Noncompliant {{Publish a declared contract type instead of 'object'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_CompliantForDeclaredContract() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public System.Threading.Tasks.Task Accept(System.Guid id) =>
                    _publisher.Publish(new OrderAccepted(id));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_CompliantForMassTransitRuntimeType() =>
        builder.AddSnippet(
            """
            using MassTransit;

            namespace MassTransit
            {
                public interface IPublishEndpoint { }

                public static class PublishEndpointExtensions
                {
                    public static System.Threading.Tasks.Task Publish(
                        this IPublishEndpoint endpoint,
                        object message,
                        System.Type messageType,
                        System.Threading.CancellationToken cancellationToken = default) => null;
                }
            }

            public sealed record OrderAccepted(System.Guid OrderId);

            public class OrderService
            {
                private readonly MassTransit.IPublishEndpoint publisher;

                public System.Threading.Tasks.Task Accept(object payload) =>
                    publisher.Publish(payload, typeof(OrderAccepted));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessageShouldHaveExplicitContract_CompliantForNonMessagingCall() =>
        builder.AddSnippet(
            Stubs + """

            public class Recorder
            {
                public void Publish(object payload) { }

                public void Record(System.Guid id) => Publish(new { OrderId = id });
            }
            """)
            .VerifyNoIssues();
}
