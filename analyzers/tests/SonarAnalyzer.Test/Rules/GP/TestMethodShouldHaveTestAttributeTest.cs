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
public class TestMethodShouldHaveTestAttributeTest
{
    // AddTestReference turns the compilation into a test project, which is what a rule scoped to test code needs.
    private readonly VerifierBuilder msTest = new VerifierBuilder<CS.TestMethodShouldHaveTestAttribute>()
        .WithOptions(LanguageOptions.CSharpLatest)
        .AddTestReference()
        .AddReferences(NuGetMetadataReference.MSTestTestFrameworkV3);

    private readonly VerifierBuilder xUnit = new VerifierBuilder<CS.TestMethodShouldHaveTestAttribute>()
        .WithOptions(LanguageOptions.CSharpLatest)
        .AddTestReference()
        .AddReferences(NuGetMetadataReference.XunitFrameworkV3());

    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_NoncompliantForUnannotatedMethod() =>
        msTest.AddSnippet(
            """
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class TokenValidatorTest
            {
                [TestMethod]
                public void Accepts_A_Valid_Token() { }

                public void Rejects_An_Expired_Token() { } // Noncompliant {{Add a test attribute to 'Rejects_An_Expired_Token' or make it private - as it stands it never runs.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_NoncompliantForTaskReturningMethodInXunitClass() =>
        xUnit.AddSnippet(
            """
            using System.Threading.Tasks;
            using Xunit;

            public class TokenValidatorTest
            {
                [Fact]
                public Task Accepts_A_Valid_Token() => Task.CompletedTask;

                public Task Rejects_An_Expired_Token() => Task.CompletedTask; // Noncompliant {{Add a test attribute to 'Rejects_An_Expired_Token' or make it private - as it stands it never runs.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CompliantForPrivateHelper() =>
        msTest.AddSnippet(
            """
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class TokenValidatorTest
            {
                [TestMethod]
                public void Accepts_A_Valid_Token() => Arrange();

                private void Arrange() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CompliantForInvokedPublicHelper() =>
        msTest.AddSnippet(
            """
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class RecentlySearchedUsersTest
            {
                [TestMethod]
                public void Adds_First_User() => AddRecentlySearchesUserData();

                [TestMethod]
                public void Adds_Another_User()
                {
                    this.AddRecentlySearchesUserData();
                }

                public void AddRecentlySearchesUserData() { }

                public void Missing_Test_Attribute() { } // Noncompliant {{Add a test attribute to 'Missing_Test_Attribute' or make it private - as it stands it never runs.}}

                public void Recursive_Method() => Recursive_Method(); // Noncompliant
            }
            """)
            .Verify();

    // Any attribute at all means the author declared an intent, so lifecycle hooks are left alone. Dispose has no
    // attribute to look for - it is how xUnit expresses teardown - so it is excluded by name.
    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CompliantForLifecycleHooks() =>
        msTest.AddSnippet(
            """
            using System;
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class TokenValidatorTest : IDisposable
            {
                [TestInitialize]
                public void Setup() { }

                [TestMethod]
                public void Accepts_A_Valid_Token() { }

                public void Dispose() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CompliantForMethodWithParameters() =>
        msTest.AddSnippet(
            """
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class TokenValidatorTest
            {
                [TestMethod]
                public void Accepts_A_Valid_Token() => Check("token");

                public void Check(string token) { }
            }
            """)
            .VerifyNoIssues();

    // A class with no recognized test at all is not a test class and is never reported.
    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CompliantForNonTestClass() =>
        msTest.AddSnippet(
            """
            public class TokenValidator
            {
                public void Validate() { }
                public void Reject() { }
            }
            """)
            .VerifyNoIssues();

    // xUnit carries async setup and teardown through IAsyncLifetime rather than an attribute, so neither of its
    // members is an unannotated test - InitializeAsync in particular has no name-based exclusion to fall back on.
    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CompliantForAsyncLifetimeImplementation() =>
        xUnit.AddSnippet(
            """
            using System.Threading.Tasks;
            using Xunit;

            public interface IAsyncLifetime
            {
                Task InitializeAsync();
                Task DisposeAsync();
            }

            public class TokenValidatorTest : IAsyncLifetime
            {
                public Task InitializeAsync() => Task.CompletedTask;

                public Task DisposeAsync() => Task.CompletedTask;

                [Fact]
                public void Accepts_A_Valid_Token() { }
            }
            """)
            .VerifyNoIssues();

    // The same for a project's own fixture interface: the interface, not an attribute, is what makes it run.
    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CompliantForOwnInterfaceImplementation() =>
        msTest.AddSnippet(
            """
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            public interface INeedsDatabase
            {
                void SeedDatabase();
            }

            [TestClass]
            public class TokenValidatorTest : INeedsDatabase
            {
                public void SeedDatabase() { }

                [TestMethod]
                public void Accepts_A_Valid_Token() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void TestMethodShouldHaveTestAttribute_CodeFix() =>
        msTest.WithBasePath("GP")
            .AddPaths("TestMethodShouldHaveTestAttribute.cs")
            .WithCodeFix<CS.TestMethodShouldHaveTestAttributeCodeFix>()
            .WithCodeFixedPaths("TestMethodShouldHaveTestAttribute.Fixed.cs")
            .VerifyCodeFix();
}
