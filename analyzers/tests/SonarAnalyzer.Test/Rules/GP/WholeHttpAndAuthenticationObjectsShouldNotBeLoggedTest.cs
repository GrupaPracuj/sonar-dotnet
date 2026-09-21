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
public class WholeHttpAndAuthenticationObjectsShouldNotBeLoggedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.WholeHttpAndAuthenticationObjectsShouldNotBeLogged>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class LoggerMessageAttribute : System.Attribute
            {
                public int EventId { get; set; }
                public string Message { get; set; }
            }

            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string message, params object[] args) { }
            }
        }

        namespace System.Security.Claims
        {
            public class ClaimsIdentity
            {
                public string Name { get; }
            }

            public class ClaimsPrincipal
            {
                public ClaimsIdentity Identity { get; }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IHeaderDictionary
            {
                string this[string key] { get; }
            }

            public interface IRequestCookieCollection { }
            public interface IQueryCollection { }
            public interface IFormCollection { }

            public struct QueryString
            {
                public override string ToString() => "";
            }

            public abstract class HttpRequest
            {
                public abstract string Method { get; }
                public abstract IHeaderDictionary Headers { get; }
                public abstract IRequestCookieCollection Cookies { get; }
                public abstract IQueryCollection Query { get; }
                public abstract QueryString QueryString { get; }
            }

            public abstract class HttpResponse { }

            public abstract class HttpContext
            {
                public abstract HttpRequest Request { get; }
                public abstract HttpResponse Response { get; }
                public abstract System.Security.Claims.ClaimsPrincipal User { get; }
            }
        }

        namespace Microsoft.AspNetCore.Authentication
        {
            public sealed class AuthenticationProperties { }

            public sealed class AuthenticationTicket
            {
                public AuthenticationTicket(
                    System.Security.Claims.ClaimsPrincipal principal,
                    AuthenticationProperties properties)
                {
                    Principal = principal;
                    Properties = properties;
                }

                public System.Security.Claims.ClaimsPrincipal Principal { get; }
                public AuthenticationProperties Properties { get; }
            }
        }
        """;

    [TestMethod]
    public void WholeHttpAndAuthenticationObjectsShouldNotBeLogged_NoncompliantDirectObjects() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class RequestLogger
            {
                private readonly Microsoft.Extensions.Logging.ILogger logger;

                public void Log(
                    Microsoft.AspNetCore.Http.HttpContext context,
                    Microsoft.AspNetCore.Authentication.AuthenticationProperties properties,
                    Microsoft.AspNetCore.Authentication.AuthenticationTicket ticket)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Context {Context}", context); // Noncompliant {{Do not log the whole 'HttpContext' object - log only explicitly selected, non-sensitive fields.}}
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Request {Request}", context.Request); // Noncompliant {{Do not log the whole 'HttpRequest' object - log only explicitly selected, non-sensitive fields.}}
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Headers {Headers}", context.Request.Headers); // Noncompliant {{Do not log the whole 'IHeaderDictionary' object - log only explicitly selected, non-sensitive fields.}}
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Cookies {Cookies}", context.Request.Cookies); // Noncompliant {{Do not log the whole 'IRequestCookieCollection' object - log only explicitly selected, non-sensitive fields.}}
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Query {Query}", context.Request.Query); // Noncompliant {{Do not log the whole 'IQueryCollection' object - log only explicitly selected, non-sensitive fields.}}
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Principal {Principal}", context.User); // Noncompliant {{Do not log the whole 'ClaimsPrincipal' object - log only explicitly selected, non-sensitive fields.}}
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Properties {Properties}", properties); // Noncompliant {{Do not log the whole 'AuthenticationProperties' object - log only explicitly selected, non-sensitive fields.}}
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Ticket {Ticket}", ticket); // Noncompliant {{Do not log the whole 'AuthenticationTicket' object - log only explicitly selected, non-sensitive fields.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void WholeHttpAndAuthenticationObjectsShouldNotBeLogged_NoncompliantForLoggerMessageMethod() =>
        builder.AddSnippet(
            Stubs + """

            public static class RequestLog
            {
                [Microsoft.Extensions.Logging.LoggerMessage(EventId = 1, Message = "Request {Request}")]
                public static void LogRequest(
                    Microsoft.Extensions.Logging.ILogger logger,
                    Microsoft.AspNetCore.Http.HttpRequest request) { }
            }

            public sealed class RequestHandler
            {
                public void Handle(
                    Microsoft.Extensions.Logging.ILogger logger,
                    Microsoft.AspNetCore.Http.HttpRequest request) =>
                    RequestLog.LogRequest(logger, request); // Noncompliant {{Do not log the whole 'HttpRequest' object - log only explicitly selected, non-sensitive fields.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void WholeHttpAndAuthenticationObjectsShouldNotBeLogged_NoncompliantInterpolationAndConcatenation() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class RequestLogger
            {
                private readonly Microsoft.Extensions.Logging.ILogger logger;

                public void Log(Microsoft.AspNetCore.Http.HttpRequest request)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, $"Request: {request}"); // Noncompliant
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Query: " + request.QueryString); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void WholeHttpAndAuthenticationObjectsShouldNotBeLogged_CompliantSelectedFields() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class RequestLogger
            {
                private readonly Microsoft.Extensions.Logging.ILogger logger;

                public void Log(Microsoft.AspNetCore.Http.HttpContext context)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(
                        logger,
                        "Method {Method}, correlation {CorrelationId}, user {UserName}",
                        context.Request.Method,
                        context.Request.Headers["X-Correlation-Id"],
                        context.User.Identity.Name);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void WholeHttpAndAuthenticationObjectsShouldNotBeLogged_CompliantForNonLoggingCall() =>
        builder.AddSnippet(
            Stubs + """

            public static class RequestProcessor
            {
                public static void Process(Microsoft.AspNetCore.Http.HttpRequest request) =>
                    System.GC.KeepAlive(request);
            }
            """)
            .VerifyNoIssues();
}
