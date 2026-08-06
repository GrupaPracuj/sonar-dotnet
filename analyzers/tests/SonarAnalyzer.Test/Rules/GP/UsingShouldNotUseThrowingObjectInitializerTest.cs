using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class UsingShouldNotUseThrowingObjectInitializerTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.UsingShouldNotUseThrowingObjectInitializer>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_NoncompliantForUsingDeclaration() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value { get; set; }
                public void Dispose() { }
            }

            public class C
            {
                public void M1()
                {
                    using var conn = new FakeDisposable { Value = Compute() }; // Noncompliant {{This 'using' constructs 'conn' via an object initializer - if a member assignment throws, the instance is never bound and 'Dispose' is never called. Assign the risky members in separate statements after construction.}}
                }

                private static int Compute() => 42;
            }
            """)
            .Verify();

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_CompliantForLiteralOnly() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value { get; set; }
                public void Dispose() { }
            }

            public class C
            {
                public void M2()
                {
                    using var conn = new FakeDisposable { Value = 42 };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_CompliantWhenNoInitializerAtAll() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value { get; set; }
                public void Dispose() { }
            }

            public class C
            {
                public void M3()
                {
                    using var conn = new FakeDisposable();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_CompliantForBareParameterReference() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value { get; set; }
                public void Dispose() { }
            }

            public class C
            {
                public void M4(int input)
                {
                    using var conn = new FakeDisposable { Value = input };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_NoncompliantForUsingStatement() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value { get; set; }
                public void Dispose() { }
            }

            public class C
            {
                public void M5()
                {
                    using (var conn = new FakeDisposable { Value = Compute() }) // Noncompliant
                    {
                    }
                }

                private static int Compute() => 42;
            }
            """)
            .Verify();

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_CompliantForThisQualifiedMemberRead() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value { get; set; }
                public void Dispose() { }
            }

            public class C
            {
                private int field = 42;

                public void M6()
                {
                    using var conn = new FakeDisposable { Value = this.field };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("UsingShouldNotUseThrowingObjectInitializer.cs")
            .WithCodeFix<CS.UsingShouldNotUseThrowingObjectInitializerCodeFix>()
            .WithCodeFixedPaths("UsingShouldNotUseThrowingObjectInitializer.Fixed.cs")
            .VerifyCodeFix();
}
