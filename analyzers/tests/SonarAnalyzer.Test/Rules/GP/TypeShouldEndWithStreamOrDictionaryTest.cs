using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class TypeShouldEndWithStreamOrDictionaryTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.TypeShouldEndWithStreamOrDictionary>();

    [TestMethod]
    public void TypeShouldEndWithStreamOrDictionary_NoncompliantForStreamWithoutSuffix() =>
        builder.AddSnippet(
            """
            using System.IO;

            public class MyCache : Stream // Noncompliant {{Type 'MyCache' implements System.IO.Stream and should have a name ending in 'Stream'.}}
            {
                public override bool CanRead => false;
                public override bool CanSeek => false;
                public override bool CanWrite => false;
                public override long Length => 0;
                public override long Position { get; set; }
                public override void Flush() { }
                public override int Read(byte[] buffer, int offset, int count) => 0;
                public override long Seek(long offset, SeekOrigin origin) => 0;
                public override void SetLength(long value) { }
                public override void Write(byte[] buffer, int offset, int count) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void TypeShouldEndWithStreamOrDictionary_CompliantForStreamWithSuffix() =>
        builder.AddSnippet(
            """
            using System.IO;

            public class MyCacheStream : Stream
            {
                public override bool CanRead => false;
                public override bool CanSeek => false;
                public override bool CanWrite => false;
                public override long Length => 0;
                public override long Position { get; set; }
                public override void Flush() { }
                public override int Read(byte[] buffer, int offset, int count) => 0;
                public override long Seek(long offset, SeekOrigin origin) => 0;
                public override void SetLength(long value) { }
                public override void Write(byte[] buffer, int offset, int count) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void TypeShouldEndWithStreamOrDictionary_NoncompliantForDictionaryWithoutSuffix() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class OrderData : Dictionary<string, int> // Noncompliant {{Type 'OrderData' implements IDictionary<TKey, TValue> and should have a name ending in 'Dictionary'.}}
            {
            }
            """)
            .Verify();

    [TestMethod]
    public void TypeShouldEndWithStreamOrDictionary_CompliantForDictionaryWithSuffix() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class OrderDataDictionary : Dictionary<string, int>
            {
            }
            """)
            .VerifyNoIssues();

    // The broader IEnumerable/ICollection/IList -> "Collection" entry from the same guideline table is out of
    // scope for this rule (false-positive risk with fluent builders and iterator-pattern types), so a plain
    // IEnumerable<T> implementer must never fire, proving the rule did not implement that broader check.
    [TestMethod]
    public void TypeShouldEndWithStreamOrDictionary_CompliantForPlainEnumerable() =>
        builder.AddSnippet(
            """
            using System.Collections;
            using System.Collections.Generic;

            public class OrderSequence : IEnumerable<int>
            {
                public IEnumerator<int> GetEnumerator() => throw new System.NotImplementedException();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void TypeShouldEndWithStreamOrDictionary_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("TypeShouldEndWithStreamOrDictionary.cs")
            .WithCodeFix<CS.TypeShouldEndWithStreamOrDictionaryCodeFix>()
            .WithCodeFixedPaths("TypeShouldEndWithStreamOrDictionary.Fixed.cs")
            .VerifyCodeFix();
}
