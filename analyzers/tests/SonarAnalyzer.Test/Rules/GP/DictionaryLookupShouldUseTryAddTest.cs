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
public class DictionaryLookupShouldUseTryAddTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DictionaryLookupShouldUseTryAdd>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_NoncompliantForSingleStatementBody() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Cache
            {
                public void Add(Dictionary<string, int> dict, string key, int value)
                {
                    if (!dict.ContainsKey(key)) dict.Add(key, value); // Noncompliant {{Use 'TryAdd' instead of checking 'ContainsKey' before 'Add' - it does the lookup once instead of twice.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_NoncompliantForBlockBody() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Cache
            {
                public void Add(Dictionary<string, int> dict, string key, int value)
                {
                    if (!dict.ContainsKey(key)) // Noncompliant
                    {
                        dict.Add(key, value);
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_CompliantWhenNotNegated() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Cache
            {
                public void Add(Dictionary<string, int> dict, string key, int value)
                {
                    if (dict.ContainsKey(key)) dict.Add(key, value);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_CompliantWhenElseIsPresent() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Cache
            {
                public void Add(Dictionary<string, int> dict, string key, int value)
                {
                    if (!dict.ContainsKey(key))
                    {
                        dict.Add(key, value);
                    }
                    else
                    {
                        DoSomethingElse();
                    }
                }

                private void DoSomethingElse() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_CompliantWhenKeysDiffer() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Cache
            {
                public void Add(Dictionary<string, int> dict, string key, string otherKey, int value)
                {
                    if (!dict.ContainsKey(key)) dict.Add(otherKey, value);
                }
            }
            """)
            .VerifyNoIssues();

    // List<T>.Contains/Add is a totally different, valid pattern - List<T> has no TryAdd to fall back to.
    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_CompliantForList() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Cache
            {
                public void Add(List<string> list, string item)
                {
                    if (!list.Contains(item)) list.Add(item);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_CompliantForIDictionaryWithoutTryAddApi() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class Cache
            {
                public void Add(IDictionary<string, int> dict, string key, int value)
                {
                    if (!dict.ContainsKey(key)) dict.Add(key, value);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DictionaryLookupShouldUseTryAdd_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("DictionaryLookupShouldUseTryAdd.cs")
            .WithCodeFix<CS.DictionaryLookupShouldUseTryAddCodeFix>()
            .WithCodeFixedPaths("DictionaryLookupShouldUseTryAdd.Fixed.cs")
            .VerifyCodeFix();
}
