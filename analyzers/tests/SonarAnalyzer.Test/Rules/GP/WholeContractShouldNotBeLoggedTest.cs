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
            }
        }

        public sealed record OrderAcceptedContract(System.Guid OrderId, string Status);
        """;

    [TestMethod]
    public void WholeContractShouldNotBeLogged_NoncompliantForContractArgument() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(OrderAcceptedContract message) =>
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Received {Message}", message); // Noncompliant {{Do not log the whole contract 'OrderAcceptedContract' - log the fields the diagnosis needs.}}
            }
            """)
            .Verify();

    // Individual fields are what the rule steers towards.
    [TestMethod]
    public void WholeContractShouldNotBeLogged_CompliantForIndividualFields() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderConsumer
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;

                public void Handle(OrderAcceptedContract message) =>
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
                public void Handle(OrderAcceptedContract message) => System.Console.WriteLine(message);
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

                public void Handle(OrderAcceptedContract message)
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

                public void Handle(OrderAcceptedContract message)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, $"Received {message.OrderId}");
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Received " + message.Status);
                }
            }
            """)
            .VerifyNoIssues();
}
