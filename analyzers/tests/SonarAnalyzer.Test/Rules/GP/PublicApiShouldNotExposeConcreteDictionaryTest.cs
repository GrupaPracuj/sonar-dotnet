using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublicApiShouldNotExposeConcreteDictionaryTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PublicApiShouldNotExposeConcreteDictionary>()
        .AddReferences(MetadataReferenceFacade.SystemXml);

    [TestMethod]
    public void PublicApiShouldNotExposeConcreteDictionary_NoncompliantPublicSurface() =>
        builder.AddSnippet(
            """
            public class Registry
            {
                public System.Collections.Generic.Dictionary<string, int> Values { get; } // Noncompliant {{Expose a dictionary interface instead of the concrete 'Dictionary' type in this public property.}}
                public System.Collections.Hashtable Legacy; // Noncompliant {{Expose a dictionary interface instead of the concrete 'Hashtable' type in this public field.}}

                public Registry(System.Collections.Generic.Dictionary<string, int> values) { } // Noncompliant {{Expose a dictionary interface instead of the concrete 'Dictionary' type in this public constructor.}}

                public System.Collections.Generic.Dictionary<string, int> Load( // Noncompliant {{Expose a dictionary interface instead of the concrete 'Dictionary' type in this public method.}}
                    System.Collections.Hashtable source) => null; // Noncompliant {{Expose a dictionary interface instead of the concrete 'Hashtable' type in this public method.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PublicApiShouldNotExposeConcreteDictionary_CompliantInterfacesAndPrivateMembers() =>
        builder.AddSnippet(
            """
            public class Registry
            {
                public System.Collections.Generic.IDictionary<string, int> Values { get; }
                public System.Collections.IDictionary Legacy { get; }
                private System.Collections.Generic.Dictionary<string, int> Cache { get; }
                internal System.Collections.Hashtable Load() => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublicApiShouldNotExposeConcreteDictionary_CompliantSerializationProperty() =>
        builder.AddSnippet(
            """
            public class Registry
            {
                [System.Xml.Serialization.XmlElement]
                public System.Collections.Generic.Dictionary<string, int> SerializedValues { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublicApiShouldNotExposeConcreteDictionary_NoncompliantPublicIndexer() =>
        builder.AddSnippet(
            """
            public class Registry
            {
                public System.Collections.Generic.Dictionary<string, int> this[int index] => null; // Noncompliant {{Expose a dictionary interface instead of the concrete 'Dictionary' type in this public indexer.}}
            }
            """)
            .Verify();
}
