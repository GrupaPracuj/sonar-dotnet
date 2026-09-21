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

    [TestMethod]
    public void DoNotLogSecretLikeValue_CompliantForCancellationToken() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;
            using System.Threading;

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }

                public static class LoggerExtensions
                {
                    public static void LogDebug(this ILogger logger, string message, params object[] args) { }
                }
            }

            public class Worker
            {
                private readonly ILogger _logger;

                public void Stop(CancellationToken cancellationToken) =>
                    _logger.LogDebug("Stopped: {CancellationToken}", cancellationToken);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotLogSecretLikeValue_AssociatesPlaceholderWithItsArgument() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;
            using System.Threading;

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }

                public static class LoggerExtensions
                {
                    public static void LogDebug(this ILogger logger, string message, params object[] args) { }
                }
            }

            public class Worker
            {
                private readonly ILogger _logger;

                public void Stop(string userId, CancellationToken cancellationToken) =>
                    _logger.LogDebug("User {UserId}, token {Token}", userId, cancellationToken);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotLogSecretLikeValue_CompliantForSystemDateAndTimeTypes() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;
            using System;

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

                public void LogExpiration(
                    DateTime accessTokenCreatedAt,
                    DateTimeOffset accessTokenExpiresAtUtc,
                    TimeSpan tokenLifetime,
                    DateTime? refreshTokenCreatedAt,
                    DateTimeOffset? refreshTokenExpiresAtUtc,
                    TimeSpan? refreshTokenLifetime)
                {
                    _logger.LogInformation(
                        "Token dates: {AccessTokenCreatedAt}, {AccessTokenExpiresAtUtc}, {TokenLifetime}",
                        accessTokenCreatedAt,
                        accessTokenExpiresAtUtc,
                        tokenLifetime);
                    _logger.LogInformation(
                        "Token dates: {Value1}, {Value2}, {Value3}",
                        refreshTokenCreatedAt,
                        refreshTokenExpiresAtUtc,
                        refreshTokenLifetime);
                }
            }
            """)
            .VerifyNoIssues();

#if NET

    [TestMethod]
    public void DoNotLogSecretLikeValue_CompliantForDateOnlyAndTimeOnly() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;
            using System;

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

                public void LogExpiration(
                    DateOnly accessTokenExpirationDate,
                    TimeOnly tokenExpirationTime,
                    DateOnly? refreshTokenExpirationDate,
                    TimeOnly? refreshTokenExpirationTime)
                {
                    _logger.LogInformation(
                        "Token dates: {AccessTokenExpirationDate}, {TokenExpirationTime}",
                        accessTokenExpirationDate,
                        tokenExpirationTime);
                    _logger.LogInformation(
                        "Token dates: {Value1}, {Value2}",
                        refreshTokenExpirationDate,
                        refreshTokenExpirationTime);
                }
            }
            """)
            .VerifyNoIssues();

#endif

    [TestMethod]
    public void DoNotLogSecretLikeValue_CompliantForNodaTimeAndJunoDateTypes() =>
        builder.WithConcurrentAnalysis(false)
            .AddSnippet(
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

                namespace NodaTime
                {
                    public struct Instant { }
                    public struct LocalDate { }
                    public struct LocalDateTime { }
                    public struct OffsetDateTime { }
                    public struct ZonedDateTime { }
                }

                namespace GP.Juno.Dates
                {
                    public struct LocalDate { }
                }

                public class AuthService
                {
                    private readonly ILogger _logger;

                    public void LogExpiration(
                        NodaTime.Instant accessTokenIssuedAt,
                        NodaTime.LocalDate refreshTokenExpirationDate,
                        NodaTime.LocalDateTime tokenExpirationLocalTime,
                        NodaTime.OffsetDateTime accessTokenExpiresAt,
                        NodaTime.ZonedDateTime refreshTokenExpiresAt,
                        GP.Juno.Dates.LocalDate tokenExpirationDate,
                        NodaTime.Instant? nullableTokenIssuedAt,
                        NodaTime.LocalDate? nullableRefreshTokenExpirationDate,
                        NodaTime.LocalDateTime? nullableTokenExpirationLocalTime,
                        NodaTime.OffsetDateTime? nullableAccessTokenExpiresAt,
                        NodaTime.ZonedDateTime? nullableRefreshTokenExpiresAt,
                        GP.Juno.Dates.LocalDate? nullableTokenExpirationDate)
                    {
                        _logger.LogInformation(
                            "Token dates: {AccessTokenIssuedAt}, {RefreshTokenExpirationDate}, {TokenExpirationLocalTime}, {AccessTokenExpiresAt}, {RefreshTokenExpiresAt}, {TokenExpirationDate}",
                            accessTokenIssuedAt,
                            refreshTokenExpirationDate,
                            tokenExpirationLocalTime,
                            accessTokenExpiresAt,
                            refreshTokenExpiresAt,
                            tokenExpirationDate);
                        _logger.LogInformation(
                            "Token dates: {Value1}, {Value2}, {Value3}, {Value4}, {Value5}, {Value6}",
                            nullableTokenIssuedAt,
                            nullableRefreshTokenExpirationDate,
                            nullableTokenExpirationLocalTime,
                            nullableAccessTokenExpiresAt,
                            nullableRefreshTokenExpiresAt,
                            nullableTokenExpirationDate);
                    }
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotLogSecretLikeValue_NoncompliantForNumericSecretLikeValue() =>
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

                public void Login(int token) =>
                    _logger.LogInformation("Received {Value}", token); // Noncompliant {{Do not log 'token' - its name suggests it holds a secret.}}
            }
            """)
            .Verify();
}
