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
public class AiStructuredOutputShouldDeserializeSafelyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AiStructuredOutputShouldDeserializeSafely>();

    // A minimal stand-in for System.Text.Json, shaped like the real API but under our own control so the tests do
    // not depend on which BCL/NuGet surface happens to be available for the target framework running them.
    private const string JsonStubs =
        """
        namespace System.Text.Json.Serialization
        {
            public enum JsonUnmappedMemberHandling
            {
                Skip,
                Disallow,
            }
        }

        namespace System.Text.Json
        {
            public class JsonSerializerOptions
            {
                public System.Text.Json.Serialization.JsonUnmappedMemberHandling UnmappedMemberHandling { get; set; }
            }

            public static class JsonSerializer
            {
                public static T Deserialize<T>(string json) => default(T);
                public static T Deserialize<T>(string json, JsonSerializerOptions options) => default(T);
            }

            public class JsonDocument
            {
                public static JsonDocument Parse(string json) => null;
            }
        }

        namespace System.Dynamic
        {
            public class ExpandoObject { }
        }
        """;

    [TestMethod]
    public void NoncompliantWhenNoOptionsArePassed() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response) =>
                    System.Text.Json.JsonSerializer.Deserialize<InvoiceData>(response.Text); // Noncompliant {{This AI response is deserialized without a JsonSerializerOptions that disallows unmapped members - pass one with UnmappedMemberHandling.Disallow.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenOptionsDoNotDisallowUnmappedMembers() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response) =>
                    JsonSerializer.Deserialize<InvoiceData>(response.Text, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip }); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenDeserializingIntoDynamic() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Dynamic;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class Extractor
            {
                public ExpandoObject Extract(ChatResponse response) =>
                    System.Text.Json.JsonSerializer.Deserialize<ExpandoObject>(response.Text); // Noncompliant {{This AI response is deserialized directly into a dynamic/loosely typed value instead of a typed model.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantForJsonDocumentParse() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class Extractor
            {
                public JsonDocument Extract(ChatResponse response) =>
                    JsonDocument.Parse(response.Text); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void CompliantWhenOptionsDisallowUnmappedMembers() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response) =>
                    JsonSerializer.Deserialize<InvoiceData>(response.Text, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenOptionsComeFromAField() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

                public InvoiceData Extract(ChatResponse response) =>
                    JsonSerializer.Deserialize<InvoiceData>(response.Text, Options);
            }
            """)
            .VerifyNoIssues();

    // An arbitrary string that is not directly a known AI response's Text property is outside the rule's scope.
    [TestMethod]
    public void CompliantWhenSourceIsNotAKnownAiResponseText() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(string json) =>
                    System.Text.Json.JsonSerializer.Deserialize<InvoiceData>(json);
            }
            """)
            .VerifyNoIssues();

    // Finding 3 regression: the options are made safe through a direct assignment after construction.
    [TestMethod]
    public void CompliantWhenOptionsAreMadeSafeAfterConstruction() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = new JsonSerializerOptions();
                    options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options);
                }
            }
            """)
            .VerifyNoIssues();

    // Finding 3 regression: a later direct assignment overrides the safe initializer value.
    [TestMethod]
    public void NoncompliantWhenOptionsAreMutatedToPermissiveAfterConstruction() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                    options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding 5 regression: named arguments reorder "json" after "options" - the source and options arguments must
    // still be mapped by parameter identity, not by their raw position in the call.
    [TestMethod]
    public void CompliantWhenArgumentsAreReorderedWithNamedArgumentsAndOptionsAreSafe() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response) =>
                    JsonSerializer.Deserialize<InvoiceData>(options: new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }, json: response.Text);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void NoncompliantWhenArgumentsAreReorderedWithNamedArgumentsAndOptionsArePermissive() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response) =>
                    JsonSerializer.Deserialize<InvoiceData>(options: new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip }, json: response.Text); // Noncompliant
            }
            """)
            .Verify();

    // Finding 1 regression: the options local's own initializer is opaque - nothing here proves the safety setting
    // is missing.
    [TestMethod]
    public void CompliantWhenOptionsComeFromAnOpaqueFactoryWithNoMutation() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = CreateOptions();
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options);
                }

                private static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions();
            }
            """)
            .VerifyNoIssues();

    // Finding 1 regression: a direct mutation setting the safe value right after an opaque factory call is provable
    // and must be trusted.
    [TestMethod]
    public void CompliantWhenOptionsAreMadeSafeAfterAnOpaqueFactoryCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = CreateOptions();
                    options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options);
                }

                private static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions();
            }
            """)
            .VerifyNoIssues();

    // Finding 2 regression: declaration in the outer method body, mutation and usage together in a nested "if".
    [TestMethod]
    public void CompliantWhenOptionsAreMadeSafeAndUsedTogetherInsideNestedBlock() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response, bool strict)
                {
                    var options = new JsonSerializerOptions();
                    if (strict)
                    {
                        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                        return JsonSerializer.Deserialize<InvoiceData>(response.Text, options);
                    }

                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding (whole-local replacement) regression: the local is replaced with an entirely new, permissive-by-
    // default object after the original, safe declaration - the earlier setting must not leak through.
    [TestMethod]
    public void NoncompliantWhenLocalIsReplacedWithAFreshEmptyObjectAfterASafeDeclaration() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                    options = new JsonSerializerOptions();
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding (null replacement) regression: the local is replaced with a null literal - passing it on is exactly
    // the same as passing null directly and must be reported the same way.
    [TestMethod]
    public void NoncompliantWhenLocalIsReplacedWithNull() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                    options = null;
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding (whole-local replacement) regression: the replacement itself is opaque - nothing proves the setting is
    // missing unless a mutation follows it.
    [TestMethod]
    public void CompliantWhenLocalIsReplacedWithAnOpaqueFactoryCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                    options = CreateOptions();
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options);
                }

                private static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions();
            }
            """)
            .VerifyNoIssues();

    // Finding (whole-local replacement) regression: a mutation right after an opaque replacement is still trusted.
    [TestMethod]
    public void CompliantWhenOptionsAreMadeSafeAfterLocalIsReplacedWithAnOpaqueFactoryCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n" + GpAiTestStubs.All + JsonStubs + """

            public class InvoiceData { }

            public class Extractor
            {
                public InvoiceData Extract(ChatResponse response)
                {
                    var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
                    options = CreateOptions();
                    options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                    return JsonSerializer.Deserialize<InvoiceData>(response.Text, options);
                }

                private static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions();
            }
            """)
            .VerifyNoIssues();
}
