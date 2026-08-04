using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DateOnlyPropertyShouldNotBeNamedUtcTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DateOnlyPropertyShouldNotBeNamedUtc>();

#if NET

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForDateOnlyProperty() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateOnly ExpirationDateUtc { get; set; } // Noncompliant {{Rename 'ExpirationDateUtc' - a date without a time component should not have 'Utc' in its name.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForLeadingUtcWord() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateOnly UtcStartDate { get; set; } // Noncompliant {{Rename 'UtcStartDate' - a date without a time component should not have 'Utc' in its name.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_CompliantForDateOnlyPropertyWithoutUtc() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateOnly ExpirationDate { get; set; }
            }
            """)
            .VerifyNoIssues();

#endif

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForJunoLocalDateField() =>
        builder.AddSnippet(
            """
            namespace GP.Juno.Dates
            {
                public struct LocalDate { }
            }

            public class Contract
            {
                private GP.Juno.Dates.LocalDate utcExpirationDate; // Noncompliant {{Rename 'utcExpirationDate' - a date without a time component should not have 'Utc' in its name.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_CompliantForDateTimePropertyNamedUtc() =>
        builder.AddSnippet(
            """
            using System;

            public class Contract
            {
                public DateTime ExpirationDateUtc { get; set; }
            }
            """)
            .VerifyNoIssues();
}
