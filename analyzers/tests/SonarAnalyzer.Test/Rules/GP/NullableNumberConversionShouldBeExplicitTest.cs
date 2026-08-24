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
public class NullableNumberConversionShouldBeExplicitTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.NullableNumberConversionShouldBeExplicit>();

    [TestMethod]
    public void NullableNumberConversionShouldBeExplicit_Noncompliant() =>
        builder.AddSnippet(
            """
            public static class Mapping
            {
                public static int ToId(int? value) =>
                    System.Convert.ToInt32(value); // Noncompliant {{Handle null explicitly before converting this nullable number; Convert.ToInt32 silently treats null as zero.}}

                public static long ToLongId(long? value) =>
                    System.Convert.ToInt64(value); // Noncompliant

                public static int ToShortId(short? value) =>
                    System.Convert.ToInt32(value); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void NullableNumberConversionShouldBeExplicit_CompliantForExplicitNullPolicy() =>
        builder.AddSnippet(
            """
            public static class Mapping
            {
                public static int UseZero(int? value) =>
                    System.Convert.ToInt32(value ?? 0);

                public static int RequireValue(int? value) =>
                    System.Convert.ToInt32(value.Value);

                public static int PreserveNull(int? value) =>
                    value is null ? -1 : System.Convert.ToInt32(value);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void NullableNumberConversionShouldBeExplicit_CompliantForOtherInputsAndMethods() =>
        builder.AddSnippet(
            """
            public static class Mapping
            {
                public static int NonNullable(int value) =>
                    System.Convert.ToInt32(value);

                public static int FloatingPoint(double? value) =>
                    System.Convert.ToInt32(value);

                public static short OtherTarget(int? value) =>
                    System.Convert.ToInt16(value);

                public static int Lookalike(int? value) =>
                    Converter.ToInt32(value);
            }

            public static class Converter
            {
                public static int ToInt32(object value) => 0;
            }
            """)
            .VerifyNoIssues();
}
