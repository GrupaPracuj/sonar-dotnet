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
public class FlagsEnumShouldNotBeValidatedWithIsDefinedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.FlagsEnumShouldNotBeValidatedWithIsDefined>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void FlagsEnumShouldNotBeValidatedWithIsDefined_NoncompliantClassicOverload() =>
        builder.AddSnippet(
            """
            [System.Flags]
            public enum Access
            {
                Read = 1,
                Write = 2,
            }

            public class Validator
            {
                public bool IsValid(Access value) =>
                    System.Enum.IsDefined(value: value, enumType: typeof(Access)); // Noncompliant {{Do not use 'Enum.IsDefined' to validate the flags enum 'Access'; combined flag values are valid but may not be named.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void FlagsEnumShouldNotBeValidatedWithIsDefined_NoncompliantGenericOverload() =>
        builder.AddSnippet(
            """
            [System.Flags]
            public enum Access
            {
                Read = 1,
                Write = 2,
            }

            public class Validator
            {
                public bool IsValid(Access value) =>
                    System.Enum.IsDefined<Access>(value); // Noncompliant {{Do not use 'Enum.IsDefined' to validate the flags enum 'Access'; combined flag values are valid but may not be named.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void FlagsEnumShouldNotBeValidatedWithIsDefined_CompliantForSimpleEnumAndOtherMethod() =>
        builder.AddSnippet(
            """
            public enum State
            {
                Unknown,
                Ready,
            }

            public static class CustomEnum
            {
                public static bool IsDefined(System.Type type, object value) => true;
            }

            public class Validator
            {
                public bool IsValid(State value) =>
                    System.Enum.IsDefined(typeof(State), value) && CustomEnum.IsDefined(typeof(State), value);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void FlagsEnumShouldNotBeValidatedWithIsDefined_CompliantForStringName() =>
        builder.AddSnippet(
            """
            [System.Flags]
            public enum Access
            {
                Read = 1,
                Write = 2,
            }

            public class Validator
            {
                public bool IsValid(string value) => System.Enum.IsDefined(typeof(Access), value);
            }
            """)
            .VerifyNoIssues();
}
