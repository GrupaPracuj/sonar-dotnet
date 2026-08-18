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
public class CollectionInitializerShouldNotHaveDuplicateKeysTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.CollectionInitializerShouldNotHaveDuplicateKeys>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_NoncompliantForDuplicateDictionaryKey() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int> { { "a", 1 }, { "a", 2 } }; // Noncompliant {{Duplicate key 'a' in dictionary initializer - the second 'Add' call throws ArgumentException at runtime.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_CompliantForDifferentDictionaryKeys() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_CompliantWhenSecondKeyIsNotConstant() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int> { { "a", 1 }, { GetKey(), 2 } };
                }

                private string GetKey() => "a";
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_NoncompliantForDuplicateSetValue() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var s = new HashSet<string> { "x", "x" }; // Noncompliant {{Duplicate value 'x' in this collection initializer is redundant - 'Add' silently ignores it (or throws, for a type that disallows duplicates).}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_CompliantForDifferentSetValues() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var s = new HashSet<string> { "x", "y" };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_CompliantForListDuplicates() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var values = new List<string> { "x", "x" };
                }
            }
            """)
            .VerifyNoIssues();

    // Both keys resolve to the same constant value "a" even though the second is written as a reference to a const.
    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_NoncompliantWhenSecondKeyIsAConstReference() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                private const string K = "a";

                public void M()
                {
                    var d = new Dictionary<string, int> { { "a", 1 }, { K, 2 } }; // Noncompliant {{Duplicate key 'a' in dictionary initializer - the second 'Add' call throws ArgumentException at runtime.}}
                }
            }
            """)
            .Verify();

    // A two-argument Add-style initializer on a type that is not a dictionary must not be flagged as a dictionary
    // key duplicate: the interface guard on the "{key, value}" shape exists precisely to avoid this misfire.
    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_CompliantForNonDictionaryTwoElementInitializer() =>
        builder.AddSnippet(
            """
            using System.Collections;
            using System.Collections.Generic;

            public class Pair : IEnumerable
            {
                public void Add(string a, int b) { }

                public IEnumerator GetEnumerator() => throw new System.NotImplementedException();
            }

            public class C
            {
                public void M()
                {
                    var p = new Pair { { "a", 1 }, { "a", 2 } };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_CompliantForCustomAddOnDictionarySubtype() =>
        builder.AddSnippet(
            """
            using System;
            using System.Collections.Generic;

            public class StatusMap : Dictionary<(Type, string), int>
            {
                public void Add(int value, params (Type, string)[] keys)
                {
                    foreach (var key in keys)
                    {
                        Add(key, value);
                    }
                }
            }

            public class C
            {
                private readonly StatusMap map = new()
                {
                    { 5, (typeof(string), "WaitingForAuthorization") },
                    { 5, (typeof(int), "WaitingForVerification") },
                };
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_NoncompliantForInheritedDictionaryAdd() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class StatusMap : Dictionary<string, int>
            {
            }

            public class C
            {
                private readonly StatusMap map = new()
                {
                    { "a", 1 },
                    { "a", 2 }, // Noncompliant
                };
            }
            """)
            .Verify();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_NoncompliantForThreeDuplicates() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int>
                    {
                        { "a", 1 }, // Fine, the first occurrence
                        { "a", 2 }, // Noncompliant
                        { "a", 3 }, // Noncompliant
                    };
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_NoncompliantForOrdinalIgnoreCaseComparer() =>
        builder.AddSnippet(
            """
            using System;
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "Header", 1 },
                        { "header", 2 }, // Noncompliant {{Duplicate key 'header' in dictionary initializer - the second 'Add' call throws ArgumentException at runtime.}}
                    };
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CollectionInitializerShouldNotHaveDuplicateKeys_CompliantForOrdinalComparerWithDifferentCase() =>
        builder.AddSnippet(
            """
            using System;
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        { "Header", 1 },
                        { "header", 2 },
                    };
                }
            }
            """)
            .VerifyNoIssues();
}
