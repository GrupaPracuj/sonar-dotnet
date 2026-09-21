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
public class AiDataShouldNotBeLoggedRawTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.AiDataShouldNotBeLoggedRaw>();

    private const string LoggerStubs =
        """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }

            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string message, params object[] args) { }
            }
        }
        """;

    [TestMethod]
    public void NoncompliantWhenLoggingChatMessage() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(ChatMessage message)
                {
                    _logger.LogInformation("Message: {Message}", message); // Noncompliant {{Do not log this raw AI chat message directly - log only derived metadata (model id, token counts, duration, finish reason, request id).}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenLoggingChatResponse() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(ChatResponse response)
                {
                    _logger.LogInformation("Response: {Response}", response); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenLoggingResponseText() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(ChatResponse response)
                {
                    _logger.LogInformation("Text: {Text}", response.Text); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void NoncompliantWhenLoggingMessageCollection() =>
        builder.AddSnippet(
            "using System.Collections.Generic;\nusing Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(List<ChatMessage> messages)
                {
                    _logger.LogInformation("History: {History}", messages); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void CompliantWhenLoggingScalarMetadata() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(ChatResponse response)
                {
                    _logger.LogInformation("Model {Model}, tokens {Tokens}, finish {Finish}", response.ModelId, response.Usage.TotalTokenCount, response.FinishReason);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void CompliantWhenNotALoggingCall() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\n\n" + GpAiTestStubs.All + """

            public class Chat
            {
                public void Handle(ChatMessage message)
                {
                    System.Console.WriteLine(message);
                }
            }
            """)
            .VerifyNoIssues();

    // Finding 6 regression: raw content hidden behind a string interpolation hole must still be caught.
    [TestMethod]
    public void NoncompliantWhenResponseTextIsInsideInterpolatedString() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(ChatResponse response)
                {
                    _logger.LogInformation($"Answer: {response.Text}"); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding 6 regression: raw content hidden behind a '+' string concatenation must still be caught.
    [TestMethod]
    public void NoncompliantWhenResponseTextIsConcatenated() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(ChatResponse response)
                {
                    _logger.LogInformation("Answer: " + response.Text); // Noncompliant
                }
            }
            """)
            .Verify();

    // Finding 6 regression: interpolation/concatenation built purely from safe scalar metadata must not be flagged.
    [TestMethod]
    public void CompliantWhenInterpolationAndConcatenationOnlyUseScalarMetadata() =>
        builder.AddSnippet(
            "using Microsoft.Extensions.AI;\nusing Microsoft.Extensions.Logging;\n\n" + GpAiTestStubs.All + LoggerStubs + """

            public class Chat
            {
                private readonly ILogger _logger;

                public void Handle(ChatResponse response)
                {
                    _logger.LogInformation($"Model: {response.ModelId}" + " tokens: " + response.Usage.TotalTokenCount);
                }
            }
            """)
            .VerifyNoIssues();
}
