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

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForNullableDateOnly() =>
        builder.AddSnippet(
            """
            public class Contract
            {
                public System.DateOnly? ExpirationDateUtc { get; set; } // Noncompliant {{Rename 'ExpirationDateUtc' - a date without a time component should not have 'Utc' in its name.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("DateOnlyPropertyShouldNotBeNamedUtc.cs")
            .WithCodeFix<CS.DateOnlyPropertyShouldNotBeNamedUtcCodeFix>()
            .WithCodeFixedPaths("DateOnlyPropertyShouldNotBeNamedUtc.Fixed.cs")
            .VerifyCodeFix();

#endif

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_FieldCodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("DateOnlyPropertyShouldNotBeNamedUtc_Field.cs")
            .WithCodeFix<CS.DateOnlyPropertyShouldNotBeNamedUtcCodeFix>()
            .WithCodeFixedPaths("DateOnlyPropertyShouldNotBeNamedUtc_Field.Fixed.cs")
            .VerifyCodeFix();

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
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForNullableJunoLocalDate() =>
        builder.AddSnippet(
            """
            namespace GP.Juno.Dates
            {
                public struct LocalDate { }
            }

            public class Contract
            {
                public GP.Juno.Dates.LocalDate? ExpirationDateUtc { get; set; } // Noncompliant {{Rename 'ExpirationDateUtc' - a date without a time component should not have 'Utc' in its name.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForNullableNodaLocalDate() =>
        builder.AddSnippet(
            """
            namespace NodaTime
            {
                public struct LocalDate { }
            }

            public class Contract
            {
                public NodaTime.LocalDate? ExpirationDateUtc { get; set; } // Noncompliant {{Rename 'ExpirationDateUtc' - a date without a time component should not have 'Utc' in its name.}}
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

#if NET

    // A positional record parameter declares the member under this name, so it misleads a reader exactly as a property would.
    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForRecordParameter() =>
        builder.WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            """
            using System;

            public sealed record BirthdayDto(DateOnly BirthDateUtc); // Noncompliant@-0 {{Rename 'BirthDateUtc' - a date without a time component should not have 'Utc' in its name.}}
            """)
            .Verify();

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_CompliantForRecordParameterWithoutUtc() =>
        builder.WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            """
            using System;

            public sealed record BirthdayDto(DateOnly BirthDate);
            """)
            .VerifyNoIssues();

    // A record struct is just as much a positional record as a record class, and must be checked the same way.
    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_NoncompliantForRecordStructParameter() =>
        builder.WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            """
            using System;

            public readonly record struct BirthdayDto(DateOnly BirthDateUtc); // Noncompliant@-0 {{Rename 'BirthDateUtc' - a date without a time component should not have 'Utc' in its name.}}
            """)
            .Verify();

    [TestMethod]
    public void DateOnlyPropertyShouldNotBeNamedUtc_CompliantForRecordStructParameterWithoutUtc() =>
        builder.WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            """
            using System;

            public readonly record struct BirthdayDto(DateOnly BirthDate);
            """)
            .VerifyNoIssues();

#endif
}
