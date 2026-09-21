/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

namespace SonarAnalyzer.Test.Rules.GP;

// Shared stand-ins for the "AI" rule family (GP0141, GP0143, GP0145, GP0146) test corpora. This repository does not
// reference the real Microsoft.Extensions.AI / Microsoft.Agents.AI packages (they are still pre-1.0, and every rule
// here is defined against symbol names rather than a hard package reference - see GpAi.cs), so the tests define
// minimal stand-ins under the SDK's actual namespaces instead, the same way JunoHttpClientStubs stands in for
// GP.Juno in HttpCallShouldPropagateCancellationTokenTest. None of these rules requires or recognizes a custom
// marker attribute, so no marker attribute stand-ins are declared here either.
internal static class GpAiTestStubs
{
    internal const string ChatClientStubs =
        """
        namespace Microsoft.Extensions.AI
        {
            public class ChatMessage
            {
                public string Text { get; set; }
            }

            public class UsageDetails
            {
                public long TotalTokenCount { get; set; }
            }

            public class UsageContent
            {
                public UsageDetails Details { get; set; }
            }

            public class ChatResponseFormat
            {
                public static ChatResponseFormat ForJsonSchema<T>() => new ChatResponseFormat();
            }

            public class ChatOptions
            {
                public int? MaxOutputTokens { get; set; }
                public ChatResponseFormat ResponseFormat { get; set; }
            }

            public class ChatResponse
            {
                public string Text { get; set; }
                public UsageDetails Usage { get; set; }
                public string ModelId { get; set; }
                public string FinishReason { get; set; }
            }

            public interface IChatClient
            {
                System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(string prompt, ChatOptions options = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken));
                System.Threading.Tasks.Task<ChatResponse> GetStreamingResponseAsync(string prompt, ChatOptions options = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken));
            }

            public class DefaultChatClient : IChatClient
            {
                public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(string prompt, ChatOptions options = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => null;
                public System.Threading.Tasks.Task<ChatResponse> GetStreamingResponseAsync(string prompt, ChatOptions options = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => null;
            }
        }
        """;

    internal const string AgentStubs =
        """
        namespace Microsoft.Agents.AI
        {
            public class AgentRunResponse
            {
                public string Text { get; set; }
                public Microsoft.Extensions.AI.UsageDetails Usage { get; set; }
            }

            public abstract class AIAgent
            {
                public virtual System.Threading.Tasks.Task<AgentRunResponse> RunAsync(string message, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => null;
                public virtual System.Threading.Tasks.Task<AgentRunResponse> RunStreamingAsync(string message, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) => null;
            }

            public class ChatClientAgent : AIAgent
            {
                public ChatClientAgent(Microsoft.Extensions.AI.IChatClient chatClient, string name = null) { }
            }
        }
        """;

    internal const string All = ChatClientStubs + "\n" + AgentStubs;
}
