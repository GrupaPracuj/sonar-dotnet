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
public class WholeContractShouldNotBeLoggedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.WholeContractShouldNotBeLogged>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }

            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string message, params object[] args) { }
                public static void LogError(this ILogger logger, System.Exception exception, string message) { }
            }
        }

        namespace Contracts
        {
            public sealed record OrderAccepted(System.Guid OrderId, string Status);

            public sealed class OfferException : System.Exception { }

            public abstract class StronglyTypedValue<T>
            {
                protected StronglyTypedValue(T value) => Value = value;
                public T Value { get; }
            }

            public sealed class OrderId : StronglyTypedValue<System.Guid>
            {
                public OrderId(System.Guid value) : base(value) { }
            }
        }
        """;

    [TestMethod]
    public void WholeContractShouldNotBeLogged_NoncompliantForContractArgument() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(Contracts.OrderAccepted message) =>
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Received {Message}", message); // Noncompliant {{Do not log the whole contract 'OrderAccepted' - log the fields the diagnosis needs.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void WholeContractShouldNotBeLogged_CompliantForExceptionAndScalarIdentifier() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(Contracts.OfferException exception, Contracts.OrderId orderId)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogError(_logger, exception, "Offer failed");
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Order {OrderId}", orderId);
                }
            }
            """)
            .VerifyNoIssues();

    // Individual fields are what the rule steers towards.
    [TestMethod]
    public void WholeContractShouldNotBeLogged_CompliantForIndividualFields() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(Contracts.OrderAccepted message) =>
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(
                        _logger, "Received order {OrderId} with status {Status}", message.OrderId, message.Status);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void WholeContractShouldNotBeLogged_CompliantForNonContractArgument() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderProjection
            {
                public System.Guid OrderId { get; set; }
            }

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(OrderProjection projection) =>
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Read {Projection}", projection);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void WholeContractShouldNotBeLogged_CompliantForNonLoggingCall() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                public void Handle(Contracts.OrderAccepted message) => System.Console.WriteLine(message);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void WholeContractShouldNotBeLogged_NoncompliantInInterpolationAndConcatenation() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(Contracts.OrderAccepted message)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, $"Received {message}"); // Noncompliant
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Received " + message); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void WholeContractShouldNotBeLogged_CompliantForMemberInInterpolationAndConcatenation() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(Contracts.OrderAccepted message)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, $"Received {message.OrderId}");
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Received " + message.Status);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void WholeContractShouldNotBeLogged_CompliantForContractSuffixAlone() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record CustomerResponse(System.Guid CustomerId);

            public class CustomerService
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(CustomerResponse response) =>
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Received {Response}", response);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void WholeContractShouldNotBeLogged_NoncompliantForSentTypeOutsideContractsNamespace() =>
        builder.AddSnippet(
            Stubs + """

            namespace MassTransit
            {
                public interface ISendEndpoint
                {
                    System.Threading.Tasks.Task Send<T>(T message) where T : class;
                }
            }

            public sealed record OrderAccepted(System.Guid OrderId);

            public class OrderService
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;
                private readonly MassTransit.ISendEndpoint sender;

                public System.Threading.Tasks.Task Send(OrderAccepted message)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Sending {Message}", message); // Noncompliant {{Do not log the whole contract 'OrderAccepted' - log the fields the diagnosis needs.}}
                    return sender.Send(message);
                }
            }
            """)
            .Verify();
}
