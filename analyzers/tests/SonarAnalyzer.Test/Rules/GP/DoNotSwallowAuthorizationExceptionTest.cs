using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotSwallowAuthorizationExceptionTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotSwallowAuthorizationException>();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_NoncompliantForIsInRole() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool IsInRole(string role) => true;
            }

            public class Service
            {
                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch (System.InvalidOperationException) // Noncompliant {{Do not silently swallow an exception around an access check - at least log the failure.}}
                    {
                    }

                    return false;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_NoncompliantForHasClaim() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool HasClaim(string type, string value) => true;
            }

            public class Service
            {
                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.HasClaim("permission", "filestore_access");
                    }
                    catch (System.NullReferenceException) // Noncompliant {{Do not silently swallow an exception around an access check - at least log the failure.}}
                    {
                    }

                    return false;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_NoncompliantForFilteredGenericCatch() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool IsInRole(string role) => true;
            }

            public class Service
            {
                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch (System.Exception ex) when (ex.Message.Length > 0) // Noncompliant {{Do not silently swallow an exception around an access check - at least log the failure.}}
                    {
                    }

                    return false;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_NoncompliantForUnrecognizedOutputCall() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool IsInRole(string role) => true;
            }

            public class Service
            {
                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch (System.InvalidOperationException ex) // Noncompliant {{Do not silently swallow an exception around an access check - at least log the failure.}}
                    {
                        System.Console.WriteLine(ex);
                    }

                    return false;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_CompliantWhenRecognizedLoggerLogsException() =>
        builder.AddSnippet(
            """
            using Microsoft.Extensions.Logging;

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }

                public static class LoggerExtensions
                {
                    public static void LogError(this ILogger logger, System.Exception exception, string message) { }
                }
            }

            public class User
            {
                public bool IsInRole(string role) => true;
            }

            public class Service
            {
                private readonly Microsoft.Extensions.Logging.ILogger logger;

                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch (System.InvalidOperationException ex)
                    {
                        logger.LogError(ex, "Authorization failed");
                        return false;
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_NoncompliantWhenCatchReturnsFallback() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool IsInRole(string role) => true;
            }

            public class Service
            {
                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch (System.InvalidOperationException) // Noncompliant {{Do not silently swallow an exception around an access check - at least log the failure.}}
                    {
                        return false;
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_CompliantWhenCatchRethrows() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool IsInRole(string role) => true;
            }

            public class Service
            {
                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch (System.InvalidOperationException)
                    {
                        throw;
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // An empty "catch" or "catch (Exception)" without a filter is left to S2486, which reports exactly that shape -
    // reporting it here too would put two issues on the same line.
    [TestMethod]
    public void DoNotSwallowAuthorizationException_CompliantForCatchAllCoveredByS2486() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool IsInRole(string role) => true;
            }

            public class Service
            {
                public bool HasAccess(User user)
                {
                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch
                    {
                    }

                    try
                    {
                        return user.IsInRole("Admin");
                    }
                    catch (System.Exception)
                    {
                    }

                    return false;
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotSwallowAuthorizationException_CompliantWhenTryHasNoAccessCheck() =>
        builder.AddSnippet(
            """
            public class Service
            {
                public int Compute(string input)
                {
                    try
                    {
                        return int.Parse(input);
                    }
                    catch (System.FormatException)
                    {
                    }

                    return 0;
                }
            }
            """)
            .VerifyNoIssues();
}
