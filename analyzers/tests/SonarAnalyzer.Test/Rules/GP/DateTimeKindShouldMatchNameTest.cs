using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DateTimeKindShouldMatchNameTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DateTimeKindShouldMatchName>();

    [TestMethod]
    public void DateTimeKindShouldMatchName_NoncompliantForAssignmentToUtcNamedProperty() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDateUtc { get; set; }

                public void Init()
                {
                    ExpirationDateUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local); // Noncompliant {{'ExpirationDateUtc' is named as UTC time, but is constructed with DateTimeKind.Local.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DateTimeKindShouldMatchName_NoncompliantForVariableNamedLocal() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public void Compute()
                {
                    var startDateLocal = DateTime.SpecifyKind(DateTime.Parse("2020-01-01"), DateTimeKind.Utc); // Noncompliant {{'startDateLocal' is named as local time, but is constructed with DateTimeKind.Utc.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DateTimeKindShouldMatchName_NoncompliantForPropertyInitializer() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDateUtc { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified); // Noncompliant {{'ExpirationDateUtc' is named as UTC time, but is constructed with DateTimeKind.Unspecified.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateTimeKindShouldMatchName_NoncompliantForObjectInitializer() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDateUtc { get; set; }
            }

            public static class Factory
            {
                public static Contract Create() =>
                    new Contract
                    {
                        ExpirationDateUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local) // Noncompliant {{'ExpirationDateUtc' is named as UTC time, but is constructed with DateTimeKind.Local.}}
                    };
            }
            """)
            .Verify();

    [TestMethod]
    public void DateTimeKindShouldMatchName_NoncompliantForExpressionBodiedMethod() =>
        builder.AddSnippet(
            """
            using System;

            public class Clock
            {
                public DateTime GetExpirationUtc() => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local); // Noncompliant {{'GetExpirationUtc' is named as UTC time, but is constructed with DateTimeKind.Local.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateTimeKindShouldMatchName_NoncompliantForLocalNamedPropertyWithUnspecifiedKind() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime StartDateLocal { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified); // Noncompliant {{'StartDateLocal' is named as local time, but is constructed with DateTimeKind.Unspecified.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateTimeKindShouldMatchName_NoncompliantForTargetTypedNew() =>
        builder.WithOptions(LanguageOptions.CSharpLatest).AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDateUtc { get; set; } = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Local); // Noncompliant {{'ExpirationDateUtc' is named as UTC time, but is constructed with DateTimeKind.Local.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateTimeKindShouldMatchName_CompliantWhenKindMatchesName() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDateUtc { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                public DateTime ExpirationDateLocal { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DateTimeKindShouldMatchName_CompliantWhenNameIsAmbiguous() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDate { get; set; } = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DateTimeKindShouldMatchName_CompliantWhenKindArgumentIsMissing() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDateUtc { get; set; }

                public void Init()
                {
                    ExpirationDateUtc = new DateTime(2020, 1, 1);
                }
            }
            """)
            .VerifyNoIssues();
}
