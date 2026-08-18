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
public class DoNotLogPersonalDataTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotLogPersonalData>();

    [TestMethod]
    public void DoNotLogPersonalData_NoncompliantForTemplatePlaceholder() =>
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

            public class UserService
            {
                private readonly ILogger _logger;

                public void Register(string email)
                {
                    _logger.LogInformation("New user registered with {Email}", email); // Noncompliant {{Do not log 'Email' - its name suggests it holds personal data.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotLogPersonalData_NoncompliantForArgumentName() =>
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

            public class UserService
            {
                private readonly ILogger _logger;

                public void Register(string pesel)
                {
                    _logger.LogInformation("Received value: {Value}", pesel); // Noncompliant {{Do not log 'pesel' - its name suggests it holds personal data.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotLogPersonalData_CompliantForUnrelatedValue() =>
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

            public class UserService
            {
                private readonly ILogger _logger;

                public void Register(string userId)
                {
                    _logger.LogInformation("User {UserId} registered", userId);
                }
            }
            """)
            .VerifyNoIssues();
}
