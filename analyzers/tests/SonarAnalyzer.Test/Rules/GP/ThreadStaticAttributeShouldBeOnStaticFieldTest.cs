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
public class ThreadStaticAttributeShouldBeOnStaticFieldTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ThreadStaticAttributeShouldBeOnStaticField>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ThreadStaticAttributeShouldBeOnStaticField_NoncompliantForInstanceField() =>
        builder.AddSnippet(
            """
            using System;

            public class Foo
            {
                [ThreadStatic]
                private int _value; // Noncompliant {{'_value' has 'System.ThreadStaticAttribute' but is not static - the attribute has no effect on instance fields.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ThreadStaticAttributeShouldBeOnStaticField_CompliantForStaticField() =>
        builder.AddSnippet(
            """
            using System;

            public class Foo
            {
                [ThreadStatic]
                private static int _staticValue;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ThreadStaticAttributeShouldBeOnStaticField_CompliantForPlainField() =>
        builder.AddSnippet(
            """
            public class Foo
            {
                private int _plain;
            }
            """)
            .VerifyNoIssues();

    // Every variable in the declaration is its own instance field and is reported.
    [TestMethod]
    public void ThreadStaticAttributeShouldBeOnStaticField_NoncompliantForEachVariableInDeclaration() =>
        builder.AddSnippet(
            """
            using System;

            public class Foo
            {
                [ThreadStatic]
                private int _a, _b; // Noncompliant
                                     // Noncompliant@-1
            }
            """)
            .Verify();
}
