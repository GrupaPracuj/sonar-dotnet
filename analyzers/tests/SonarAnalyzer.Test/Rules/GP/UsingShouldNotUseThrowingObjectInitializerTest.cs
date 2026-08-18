/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

using System.IO;
using Microsoft.CodeAnalysis.CSharp;
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
    public void UsingShouldNotUseThrowingObjectInitializer_NoncompliantForThrowingSetterWithLiteralValue() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value
                {
                    get => 0;
                    set => throw new InvalidOperationException();
                }

                public void Dispose() { }
            }

            public class C
            {
                public void M()
                {
                    using var resource = new FakeDisposable { Value = 42 }; // Noncompliant {{This 'using' constructs 'resource' via an object initializer - if a member assignment throws, the instance is never bound and 'Dispose' is never called. Assign the risky members in separate statements after construction.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_CompliantForFieldWithLiteralValue() =>
        builder.AddSnippet(
            """
            using System;

            public class FakeDisposable : IDisposable
            {
                public int Value;
                public void Dispose() { }
            }

            public class C
            {
                public void M()
                {
                    using var resource = new FakeDisposable { Value = 42 };
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

#if NET

    // Emitted to an image on purpose: Compilation.ToMetadataReference() hands back a CompilationReference, whose
    // symbols still carry their declaration syntax, so it would not exercise the metadata path at all. The referenced
    // library needs the attributes that back 'required', which .NET Framework does not carry, hence #if NET.
    [TestMethod]
    public void UsingShouldNotUseThrowingObjectInitializer_CodeFixForMembersFromMetadata() =>
        builder
            .AddReferences([EmitToImage(
                """
                namespace Library
                {
                    public class InitOnlyDisposable : System.IDisposable
                    {
                        public int Value { get; init; }
                        public void Dispose() { }
                    }

                    public class RequiredDisposable : System.IDisposable
                    {
                        public required int Value { get; set; }
                        public void Dispose() { }
                    }

                    // A required field reaches a different branch than a required property: only the property has an
                    // IsRequired shim, so the field has to be recognized from its RequiredMemberAttribute.
                    public class RequiredFieldDisposable : System.IDisposable
                    {
                        public required int Value;
                        public void Dispose() { }
                    }

                    public class PlainDisposable : System.IDisposable
                    {
                        public int Value { get; set; }
                        public void Dispose() { }
                    }
                }
                """)])
            .WithBasePath("GP")
            .AddPaths("UsingShouldNotUseThrowingObjectInitializer_Metadata.cs")
            .WithCodeFix<CS.UsingShouldNotUseThrowingObjectInitializerCodeFix>()
            .WithCodeFixedPaths("UsingShouldNotUseThrowingObjectInitializer_Metadata.Fixed.cs")
            .VerifyCodeFix();

    private static MetadataReference EmitToImage(string code)
    {
        var compilation = new SnippetCompiler(code, false, AnalyzerLanguage.CSharp, parseOptions: new CSharpParseOptions(LanguageVersion.Latest)).Compilation;
        var image = new MemoryStream();
        compilation.Emit(image).Success.Should().BeTrue("the referenced library has to compile");
        image.Position = 0;
        return MetadataReference.CreateFromStream(image);
    }

#endif
}
