using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotLogSecretLikeValueTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotLogSecretLikeValue>();

    [TestMethod]
    public void DoNotLogSecretLikeValue_NoncompliantForTemplatePlaceholder() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }

                public static class LoggerExtensions
                {
                    public static void LogInformation(this ILogger logger, string message, params object[] args) { }
                }
            }

            public class AuthService
            {
                private readonly ILogger _logger;

                public void Login(string password)
                {
                    _logger.LogInformation("User authenticated with {Password}", password); // Noncompliant {{Do not log 'Password' - its name suggests it holds a secret.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotLogSecretLikeValue_NoncompliantForArgumentName() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }

                public static class LoggerExtensions
                {
                    public static void LogInformation(this ILogger logger, string message, params object[] args) { }
                }
            }

            public class AuthService
            {
                private readonly ILogger _logger;

                public void Login(string password)
                {
                    _logger.LogInformation("Received value: {Value}", password); // Noncompliant {{Do not log 'password' - its name suggests it holds a secret.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotLogSecretLikeValue_NoncompliantForSerilogTemplate() =>
        builder.AddSnippet(
            """
            using Serilog;

            namespace Serilog
            {
                public static class Log
                {
                    public static void Information(string messageTemplate, params object[] propertyValues) { }
                }
            }

            public class AuthService
            {
                public void Reset(string resetToken)
                {
                    Log.Information("Password reset with {Token}", resetToken); // Noncompliant {{Do not log 'Token' - its name suggests it holds a secret.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotLogSecretLikeValue_CompliantForUnrelatedValue() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }

                public static class LoggerExtensions
                {
                    public static void LogInformation(this ILogger logger, string message, params object[] args) { }
                }
            }

            public class AuthService
            {
                private readonly ILogger _logger;

                public void Login(string userId)
                {
                    _logger.LogInformation("User {UserId} logged in", userId);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotLogSecretLikeValue_CompliantForNonLoggingCall() =>
        builder.AddSnippet(
            """
            public class Notes
            {
                public void Write(string password) =>
                    System.Console.WriteLine("Password: {0}", password);
            }
            """)
            .VerifyNoIssues();
}
