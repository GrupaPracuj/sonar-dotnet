using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotUseUnsafeDeserializationTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotUseUnsafeDeserialization>();

    private const string Stubs =
        """
        namespace System.Runtime.Serialization.Formatters.Binary
        {
            public class BinaryFormatter
            {
                public object Deserialize(System.IO.Stream stream) => null;
            }
        }

        namespace System.Runtime.Serialization
        {
            public class NetDataContractSerializer
            {
                public object ReadObject(System.IO.Stream stream) => null;
            }

            public class DataContractSerializer
            {
                public DataContractSerializer(System.Type type) { }
                public object ReadObject(System.IO.Stream stream) => null;
            }
        }

        namespace Newtonsoft.Json
        {
            public enum TypeNameHandling { None, Objects, Arrays, Auto, All }

            public class JsonSerializerSettings
            {
                public TypeNameHandling TypeNameHandling { get; set; }
            }
        }
        """;

    [TestMethod]
    public void DoNotUseUnsafeDeserialization_NoncompliantForBinaryFormatter() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderReader
            {
                public object Read(System.IO.Stream stream)
                {
                    var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter(); // Noncompliant {{'BinaryFormatter' lets the payload decide which types to instantiate - use a serializer that deserializes into a known type.}}
                    return formatter.Deserialize(stream);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotUseUnsafeDeserialization_NoncompliantForNetDataContractSerializer() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderReader
            {
                public object Read(System.IO.Stream stream) =>
                    new System.Runtime.Serialization.NetDataContractSerializer().ReadObject(stream); // Noncompliant {{'NetDataContractSerializer' lets the payload decide which types to instantiate - use a serializer that deserializes into a known type.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotUseUnsafeDeserialization_NoncompliantForTypeNameHandlingInInitializer() =>
        builder.AddSnippet(
            Stubs + """

            public class SerializerFactory
            {
                public Newtonsoft.Json.JsonSerializerSettings Create() =>
                    new Newtonsoft.Json.JsonSerializerSettings
                    {
                        TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All // Noncompliant {{'TypeNameHandling.All' lets the payload decide which types to instantiate - use a serializer that deserializes into a known type.}}
                    };
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotUseUnsafeDeserialization_NoncompliantForTypeNameHandlingAssignment() =>
        builder.AddSnippet(
            Stubs + """

            public class SerializerFactory
            {
                public Newtonsoft.Json.JsonSerializerSettings Create()
                {
                    var settings = new Newtonsoft.Json.JsonSerializerSettings();
                    settings.TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto; // Noncompliant {{'TypeNameHandling.Auto' lets the payload decide which types to instantiate - use a serializer that deserializes into a known type.}}
                    return settings;
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotUseUnsafeDeserialization_CompliantForTypeNameHandlingNone() =>
        builder.AddSnippet(
            Stubs + """

            public class SerializerFactory
            {
                public Newtonsoft.Json.JsonSerializerSettings Create() =>
                    new Newtonsoft.Json.JsonSerializerSettings
                    {
                        TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None
                    };
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotUseUnsafeDeserialization_CompliantForDataContractSerializer() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderReader
            {
                public object Read(System.IO.Stream stream) =>
                    new System.Runtime.Serialization.DataContractSerializer(typeof(string)).ReadObject(stream);
            }
            """)
            .VerifyNoIssues();
}
