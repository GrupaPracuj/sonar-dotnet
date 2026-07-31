/*
 * SonarAnalyzer for .NET
 * Copyright (C) SonarSource Sàrl
 * mailto:info AT sonarsource DOT com
 *
 * You can redistribute and/or modify this program under the terms of
 * the Sonar Source-Available License Version 1, as published by SonarSource Sàrl.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 * See the Sonar Source-Available License for more details.
 *
 * You should have received a copy of the Sonar Source-Available License
 * along with this program; if not, see https://sonarsource.com/license/ssal/
 */

using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ClaimsAuthorizationShouldNotUseIdentityClaimsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ClaimsAuthorizationShouldNotUseIdentityClaims>();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_NegatedHasClaim() =>
        builder.AddSnippet(
            """
            public class User
            {
                public bool HasClaim(string type) => true;
            }

            public class Access
            {
                public bool HasAccess(User user)
                {
                    return !user.HasClaim("filestore_access"); // Noncompliant {{Do not base access decisions on a negated HasClaim check.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_IdentityClaimInHasClaim() =>
        builder.AddSnippet(
            """
            public static class ClaimTypes
            {
                public const string NameIdentifier = "sub";
            }

            public class User
            {
                public bool HasClaim(string type) => true;
            }

            public class Access
            {
                public bool HasAccess(User user) =>
                    user.HasClaim("sub"); // Noncompliant {{Do not base access control on identity claim 'sub'.}}

                public bool HasAccess2(User user) =>
                    user.HasClaim(ClaimTypes.NameIdentifier); // Noncompliant {{Do not base access control on identity claim 'NameIdentifier'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_IdentityClaimInAuthorizePolicy() =>
        builder.AddSnippet(
            """
            using System;

            public class AuthorizeAttribute : Attribute
            {
                public string Policy { get; set; }
            }

            [Authorize(Policy = "sub")] // Noncompliant {{Do not base access control on identity claim 'sub'.}}
            public class Endpoint { }
            """)
            .Verify();

    [TestMethod]
    public void ClaimsAuthorizationShouldNotUseIdentityClaims_Compliant() =>
        builder.AddSnippet(
            """
            using System;

            public class User
            {
                public bool HasClaim(string type) => true;
            }

            public class AuthorizeAttribute : Attribute
            {
                public string Policy { get; set; }
            }

            [Authorize(Policy = "filestore_access")]
            public class Endpoint
            {
                public bool HasAccess(User user) => user.HasClaim("filestore_access");
            }
            """)
            .VerifyNoIssues();
}
