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
public class DoNotSwallowAuthorizationExceptionTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotSwallowAuthorizationException>()
#if NET
        .AddReferences(new[] { CoreMetadataReference.SystemSecurityClaims })
#endif
        ;

    [TestMethod]
    public void DoNotSwallowAuthorizationException_NoncompliantForIsInRole() =>
        builder.AddSnippet(
            """
            using System.Security.Claims;

            public class Service
            {
                public bool HasAccess(ClaimsPrincipal user)
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
            using System.Security.Claims;

            public class Service
            {
                public bool HasAccess(ClaimsPrincipal user)
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
            using System.Security.Claims;

            public class Service
            {
                public bool HasAccess(ClaimsPrincipal user)
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
            using System.Security.Claims;

            public class Service
            {
                public bool HasAccess(ClaimsPrincipal user)
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
            using System.Security.Claims;
            using Microsoft.Extensions.Logging;

            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }

                public static class LoggerExtensions
                {
                    public static void LogError(this ILogger logger, System.Exception exception, string message) { }
                }
            }

            public class Service
            {
                private readonly Microsoft.Extensions.Logging.ILogger logger;

                public bool HasAccess(ClaimsPrincipal user)
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
            using System.Security.Claims;

            public class Service
            {
                public bool HasAccess(ClaimsPrincipal user)
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
            using System.Security.Claims;

            public class Service
            {
                public bool HasAccess(ClaimsPrincipal user)
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
            using System.Security.Claims;

            public class Service
            {
                public bool HasAccess(ClaimsPrincipal user)
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

    // A domain member that happens to be named IsInRole/HasClaim is not an access check, so the try/catch around it is
    // ordinary error handling.
    [TestMethod]
    public void DoNotSwallowAuthorizationException_CompliantForLookalikeAccessCheck() =>
        builder.AddSnippet(
            """
            public sealed class Shipment
            {
                public bool IsInRole(string role) => true;
                public bool HasClaim(string type, string value) => true;
            }

            public class Service
            {
                public bool Check(Shipment shipment)
                {
                    try
                    {
                        return shipment.IsInRole("carrier") && shipment.HasClaim("damage", "total");
                    }
                    catch (System.InvalidOperationException)
                    {
                    }

                    return false;
                }
            }
            """)
            .VerifyNoIssues();

    // A custom principal implementation is still the authorization API.
    [TestMethod]
    public void DoNotSwallowAuthorizationException_NoncompliantForCustomPrincipalImplementation() =>
        builder.AddSnippet(
            """
            public sealed class TenantPrincipal : System.Security.Principal.IPrincipal
            {
                public System.Security.Principal.IIdentity Identity => null;
                public bool IsInRole(string role) => false;
            }

            public class Service
            {
                public bool HasAccess(TenantPrincipal user)
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
}
