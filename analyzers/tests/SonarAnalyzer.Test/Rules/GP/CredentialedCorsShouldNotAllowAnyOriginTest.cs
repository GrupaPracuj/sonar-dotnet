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
public class CredentialedCorsShouldNotAllowAnyOriginTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CredentialedCorsShouldNotAllowAnyOrigin>();

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Cors.Infrastructure
        {
            public class CorsPolicyBuilder
            {
                public CorsPolicyBuilder SetIsOriginAllowed(System.Func<string, bool> predicate) => this;
                public CorsPolicyBuilder AllowCredentials() => this;
                public CorsPolicyBuilder AllowAnyHeader() => this;
            }
        }
        """;

    [TestMethod]
    public void CredentialedCorsShouldNotAllowAnyOrigin_Noncompliant() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                public void Configure(Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder policy)
                {
                    policy
                        .SetIsOriginAllowed(_ => true) // Noncompliant {{Restrict credentialed CORS requests to explicit trusted origins.}}
                        .AllowCredentials();

                    policy
                        .AllowCredentials()
                        .AllowAnyHeader()
                        .SetIsOriginAllowed(origin => 1 + 1 == 2); // Noncompliant

                    policy
                        .SetIsOriginAllowed(delegate(string origin) { return true; }) // Noncompliant
                        .AllowCredentials();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CredentialedCorsShouldNotAllowAnyOrigin_CompliantForRestrictedOrUncredentialedPolicies() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                public void Configure(
                    Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder policy,
                    string trustedOrigin)
                {
                    policy
                        .SetIsOriginAllowed(origin => origin == trustedOrigin)
                        .AllowCredentials();

                    policy.SetIsOriginAllowed(_ => true);

                    // FN: Separate statements are intentionally not correlated because they can be on disjoint control-flow paths.
                    policy.SetIsOriginAllowed(_ => true);
                    policy.AllowCredentials();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CredentialedCorsShouldNotAllowAnyOrigin_CompliantForLookalikeApi() =>
        builder.AddSnippet(
            """
            namespace Custom
            {
                public class CorsPolicyBuilder
                {
                    public CorsPolicyBuilder SetIsOriginAllowed(System.Func<string, bool> predicate) => this;
                    public CorsPolicyBuilder AllowCredentials() => this;
                }
            }

            public class Startup
            {
                public void Configure(Custom.CorsPolicyBuilder policy) =>
                    policy.SetIsOriginAllowed(_ => true).AllowCredentials();
            }
            """)
            .VerifyNoIssues();
}
