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
public class NamespaceShouldNotShareNameWithTypeTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.NamespaceShouldNotShareNameWithType>();

    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_NoncompliantForSingleSegmentNamespace() =>
        builder.AddSnippet(
            """
            namespace Debug // Noncompliant {{Namespace 'Debug' should not share a name with the type 'Debug' declared inside it.}}
            {
                public class Debug
                {
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_CompliantForDifferentTypeName() =>
        builder.AddSnippet(
            """
            namespace Debug
            {
                public class DebugHelper
                {
                }
            }
            """)
            .VerifyNoIssues();

    // Only the last dot-separated segment of the namespace name is compared against the type name.
    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_NoncompliantForLastSegmentOfDottedNamespace() =>
        builder.AddSnippet(
            """
            namespace Fabrikam.Math // Noncompliant {{Namespace 'Math' should not share a name with the type 'Math' declared inside it.}}
            {
                public class Math
                {
                }
            }
            """)
            .Verify();

    // The inner "Foo" is nested inside "Bar", not a direct member of the namespace, so the outer namespace is compliant.
    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_CompliantForNestedTypeNotDirectNamespaceMember() =>
        builder.AddSnippet(
            """
            namespace Foo
            {
                public class Bar
                {
                    public class Foo
                    {
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_NoncompliantForEnum() =>
        builder.AddSnippet(
            """
            namespace Values // Noncompliant {{Namespace 'Values' should not share a name with the type 'Values' declared inside it.}}
            {
                public enum Values
                {
                    A
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_NoncompliantForDelegate() =>
        builder.AddSnippet(
            """
            namespace Handlers // Noncompliant {{Namespace 'Handlers' should not share a name with the type 'Handlers' declared inside it.}}
            {
                public delegate void Handlers();
            }
            """)
            .Verify();

    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_NoncompliantForFileScopedNamespace() =>
        builder.WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            """
            namespace Debug; // Noncompliant {{Namespace 'Debug' should not share a name with the type 'Debug' declared inside it.}}

            public class Debug
            {
            }
            """)
            .Verify();

    [TestMethod]
    public void NamespaceShouldNotShareNameWithType_NoncompliantForRecord() =>
        builder.WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            """
            namespace Order // Noncompliant {{Namespace 'Order' should not share a name with the type 'Order' declared inside it.}}
            {
                public record Order(int Id);
            }
            """)
            .Verify();
}
