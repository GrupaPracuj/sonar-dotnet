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
public class AiAgentShouldHaveStableNameTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AiAgentShouldHaveStableName>();

    [TestMethod]
    public void NoncompliantWhenNameIsMissing() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client) =>
                    new ChatClientAgent(client); // Noncompliant {{Give this ChatClientAgent a stable, deterministic name instead of an unstable or missing one.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenNameUsesNewGuid() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client) =>
                    new ChatClientAgent(client, name: Guid.NewGuid().ToString()); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenNameInterpolatesDateTimeNow() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client) =>
                    new ChatClientAgent(client, name: $"agent-{DateTime.UtcNow}"); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void CompliantWhenNameIsLiteral() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client) =>
                    new ChatClientAgent(client, name: "invoice-summarizer");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenNameIsConstOrStaticReadonly() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                private const string ConstName = "invoice-summarizer";
                private static readonly string StaticName = "invoice-summarizer-v2";

                public ChatClientAgent Create(IChatClient client) =>
                    new ChatClientAgent(client, name: ConstName);

                public ChatClientAgent CreateOther(IChatClient client) =>
                    new ChatClientAgent(client, name: StaticName);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenNameComesFromUnknownVariable() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client, string configuredName) =>
                    new ChatClientAgent(client, name: configuredName);
            }
            """)
            .VerifyNoIssues();

    // A method call whose stability cannot be proven from the call site is accepted.
    [TestMethod]
    public void CompliantWhenNameComesFromAnOpaqueMethodCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                private static string DynamicName() => Guid.NewGuid().ToString();

                public ChatClientAgent Create(IChatClient client) =>
                    new ChatClientAgent(client, name: DynamicName());
            }
            """)
            .VerifyNoIssues();

    // Options are built in the same method (the Juno AddAIAgent pattern): their Name is inspected.
    [TestMethod]
    public void CompliantWhenLocalOptionsHaveNameInInitializer() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client, string key)
                {
                    var options = new ChatClientAgentOptions { Name = key };
                    return new ChatClientAgent(client, options);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenLocalOptionsNameIsAssignedLater() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client, string key)
                {
                    var options = new ChatClientAgentOptions();
                    options.Name = key;
                    return new ChatClientAgent(client, options);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenOptionsComeFromParameter() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client, ChatClientAgentOptions options) =>
                    new ChatClientAgent(client, options);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void NoncompliantWhenLocalOptionsNameIsNeverSet() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client)
                {
                    var options = new ChatClientAgentOptions();
                    return new ChatClientAgent(client, options); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenInlineOptionsNameIsUnstable() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Agents.AI;\nusing System;\n\n" + GpAiTestStubs.All + """

            public class Factory
            {
                public ChatClientAgent Create(IChatClient client) =>
                    new ChatClientAgent(client, new ChatClientAgentOptions { Name = Guid.NewGuid().ToString() }); // Noncompliant
            }
            """)
            .Verify();
}
