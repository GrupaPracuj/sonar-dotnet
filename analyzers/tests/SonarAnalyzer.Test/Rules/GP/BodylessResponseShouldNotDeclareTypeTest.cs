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
public class BodylessResponseShouldNotDeclareTypeTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.BodylessResponseShouldNotDeclareType>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true)]
            public class ProducesResponseTypeAttribute : System.Attribute
            {
                public ProducesResponseTypeAttribute(int statusCode) { }
                public ProducesResponseTypeAttribute(System.Type type, int statusCode) { }
            }
            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class ProducesResponseTypeAttribute<T> : ProducesResponseTypeAttribute
            {
                public ProducesResponseTypeAttribute(int statusCode) : base(statusCode) { }
            }
        }

        public sealed class Response { }
        """;

    [TestMethod]
    public void BodylessResponseShouldNotDeclareType_NoncompliantForBodylessStatuses() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController
            {
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(Response), 204)] // Noncompliant {{Remove the response body type from status 204; this status cannot contain a body.}}
                [Microsoft.AspNetCore.Mvc.ProducesResponseType<Response>(205)] // Noncompliant {{Remove the response body type from status 205; this status cannot contain a body.}}
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(Response), 304)] // Noncompliant {{Remove the response body type from status 304; this status cannot contain a body.}}
                public void Get() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void BodylessResponseShouldNotDeclareType_CompliantWithoutTypeOrForBodyStatus() =>
        builder.AddSnippet(
            Stubs + """

            [Microsoft.AspNetCore.Mvc.ProducesResponseType(204)]
            public class OrdersController
            {
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(void), 205)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(Response), 200)]
                [Microsoft.AspNetCore.Mvc.ProducesResponseType<Response>(400)]
                public void Get() { }
            }
            """)
            .VerifyNoIssues();
}
