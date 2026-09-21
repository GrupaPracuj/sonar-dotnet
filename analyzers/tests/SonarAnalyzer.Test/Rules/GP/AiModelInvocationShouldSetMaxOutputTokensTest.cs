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
public class AiModelInvocationShouldSetMaxOutputTokensTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AiModelInvocationShouldSetMaxOutputTokens>();

    [TestMethod]
    public void NoncompliantWhenOptionsIsMissingTheLimit() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text) =>
                    _chatClient.GetResponseAsync(text, new ChatOptions()); // Noncompliant {{Set 'MaxOutputTokens' on the ChatOptions passed to this AI model invocation to a positive value.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenOptionsIsNull() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text) =>
                    _chatClient.GetResponseAsync(text, null); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenLimitIsZeroOrNegativeConstant() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text) =>
                    _chatClient.GetResponseAsync(text, new ChatOptions { MaxOutputTokens = 0 }); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void CompliantWhenLimitIsPositiveConstant() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text) =>
                    _chatClient.GetResponseAsync(text, new ChatOptions { MaxOutputTokens = 512 });
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenLimitComesFromNonConstantExpression() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;
                private readonly int _limit = 256;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text) =>
                    _chatClient.GetResponseAsync(text, new ChatOptions { MaxOutputTokens = _limit });
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenOptionsComeFromAParameter() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text, ChatOptions options) =>
                    _chatClient.GetResponseAsync(text, options);
            }
            """)
            .VerifyNoIssues();

    // An agent's RunAsync overload has no ChatOptions parameter, so there is nothing to check.
    [TestMethod]
    public void CompliantForAgentRunWithNoChatOptionsParameter() =>
        builder.AddSnippet(
            "using Microsoft.Agents.AI;\n\n" + GpAiTestStubs.All + """

            public class MyAgent : AIAgent
            {
                public System.Threading.Tasks.Task Run(string message) =>
                    RunAsync(message);
            }
            """)
            .VerifyNoIssues();

    // Finding 3 regression: the cap is set through a direct assignment on the local right after construction,
    // rather than in the object initializer itself - this must not be reported as missing.
    [TestMethod]
    public void CompliantWhenLimitIsSetAfterConstructionButBeforeCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions();
                    options.MaxOutputTokens = 256;
                    return _chatClient.GetResponseAsync(text, options);
                }
            }
            """)
            .VerifyNoIssues();

    // Finding 3 regression: a later direct assignment overrides whatever the initializer set - the most recent
    // provable value before the call is what counts.
    [TestMethod]
    public void NoncompliantWhenLimitIsMutatedToZeroAfterConstruction() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions { MaxOutputTokens = 256 };
                    options.MaxOutputTokens = 0;
                    return _chatClient.GetResponseAsync(text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding 1 regression: the local's own initializer is opaque (a call to some factory method whose body is not
    // inspected) - with no direct mutation setting the cap anywhere, nothing here proves the cap is missing, so this
    // must not be reported.
    [TestMethod]
    public void CompliantWhenOptionsComeFromAnOpaqueFactoryWithNoMutation() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = CreateOptions();
                    return _chatClient.GetResponseAsync(text, options);
                }

                private static ChatOptions CreateOptions() => new ChatOptions();
            }
            """)
            .VerifyNoIssues();

    // Finding 1 regression: even though the initializer is opaque, a direct mutation setting the cap right after it
    // is still provable and must be trusted.
    [TestMethod]
    public void CompliantWhenLimitIsSetAfterAnOpaqueFactoryCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = CreateOptions();
                    options.MaxOutputTokens = 256;
                    return _chatClient.GetResponseAsync(text, options);
                }

                private static ChatOptions CreateOptions() => new ChatOptions();
            }
            """)
            .VerifyNoIssues();

    // Finding 2 regression: the declaration sits in the outer method body, while both the mutation and the usage
    // are together inside a nested "if" block - the mutation is still guaranteed to run before the usage.
    [TestMethod]
    public void CompliantWhenLimitIsSetAndUsedTogetherInsideNestedBlock() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text, bool needsLimit)
                {
                    var options = new ChatOptions();
                    if (needsLimit)
                    {
                        options.MaxOutputTokens = 256;
                        return _chatClient.GetResponseAsync(text, options);
                    }

                    return _chatClient.GetResponseAsync(text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding 2 regression: the mutation only happens conditionally (inside an "if" with no matching guarantee for
    // the code after it) - it must not be credited to a usage sitting outside that "if", since nothing proves the
    // branch actually ran.
    [TestMethod]
    public void NoncompliantWhenLimitIsSetOnlyInAnUnrelatedBranch() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text, bool needsLimit)
                {
                    var options = new ChatOptions();
                    if (needsLimit)
                    {
                        options.MaxOutputTokens = 256;
                    }

                    return _chatClient.GetResponseAsync(text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding (whole-local replacement) regression: the local is replaced with an entirely new, empty object after
    // the original, compliant declaration - the earlier cap must not leak through to the replaced object.
    [TestMethod]
    public void NoncompliantWhenLocalIsReplacedWithAFreshEmptyObjectAfterAConfiguredDeclaration() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions { MaxOutputTokens = 256 };
                    options = new ChatOptions();
                    return _chatClient.GetResponseAsync(text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding (null replacement) regression: the local is replaced with a null literal - passing it on is exactly
    // the same as passing null directly and must be reported the same way.
    [TestMethod]
    public void NoncompliantWhenLocalIsReplacedWithNull() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions { MaxOutputTokens = 256 };
                    options = null;
                    return _chatClient.GetResponseAsync(text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding (null replacement) regression: the replacement is a cast null - it must still be recognized as proven
    // null, not treated as an opaque, unprovable value.
    [TestMethod]
    public void NoncompliantWhenLocalIsReplacedWithACastNull() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions { MaxOutputTokens = 256 };
                    options = (ChatOptions)null;
                    return _chatClient.GetResponseAsync(text, options); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding (whole-local replacement) regression: the replacement object itself sets the cap - it is inspected on
    // its own, not the original declaration's now-irrelevant value.
    [TestMethod]
    public void CompliantWhenLocalIsReplacedWithAConfiguredObject() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions();
                    options = new ChatOptions { MaxOutputTokens = 256 };
                    return _chatClient.GetResponseAsync(text, options);
                }
            }
            """)
            .VerifyNoIssues();

    // Finding (whole-local replacement) regression: the replacement itself is opaque - nothing proves the cap is
    // missing unless a mutation follows it.
    [TestMethod]
    public void CompliantWhenLocalIsReplacedWithAnOpaqueFactoryCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions { MaxOutputTokens = 256 };
                    options = CreateOptions();
                    return _chatClient.GetResponseAsync(text, options);
                }

                private static ChatOptions CreateOptions() => new ChatOptions();
            }
            """)
            .VerifyNoIssues();

    // Finding (whole-local replacement) regression: a mutation right after an opaque replacement is still trusted.
    [TestMethod]
    public void CompliantWhenLimitIsSetAfterLocalIsReplacedWithAnOpaqueFactoryCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Summarizer
            {
                private readonly IChatClient _chatClient;

                public System.Threading.Tasks.Task<ChatResponse> Summarize(string text)
                {
                    var options = new ChatOptions { MaxOutputTokens = 256 };
                    options = CreateOptions();
                    options.MaxOutputTokens = 512;
                    return _chatClient.GetResponseAsync(text, options);
                }

                private static ChatOptions CreateOptions() => new ChatOptions();
            }
            """)
            .VerifyNoIssues();
}
