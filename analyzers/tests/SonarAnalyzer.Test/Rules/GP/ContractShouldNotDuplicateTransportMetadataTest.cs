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
public class ContractShouldNotDuplicateTransportMetadataTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotDuplicateTransportMetadata>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string MessagingStub =
        """
        namespace GP.Juno.Abstractions
        {
            public interface ISender
            {
                System.Threading.Tasks.Task Send<T>(T message) where T : class;
            }
        }
        """;

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_NoncompliantForMessageId() =>
        builder.AddSnippet(
            """
            namespace GP.Orders.Contracts
            {
                public sealed record OrderAccepted(System.Guid OrderId, System.Guid MessageId); // Noncompliant@-0 {{'MessageId' duplicates transport metadata - read it from the consume context instead.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_NoncompliantForProperties() =>
        builder.AddSnippet(
            """
            namespace Contracts
            {
                public class OrderAccepted
                {
                    public System.Guid OrderId { get; init; }
                    public System.Guid ConversationId { get; init; } // Noncompliant {{'ConversationId' duplicates transport metadata - read it from the consume context instead.}}
                    public System.DateTimeOffset SentTime { get; init; } // Noncompliant {{'SentTime' duplicates transport metadata - read it from the consume context instead.}}
                    public string SourceAddress { get; init; } // Noncompliant {{'SourceAddress' duplicates transport metadata - read it from the consume context instead.}}
                }
            }
            """)
            .Verify();

    // A domain identifier is named after what it identifies, so it does not collide with the metadata names.
    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForDomainIdentifiers() =>
        builder.AddSnippet(
            """
            namespace Contracts
            {
                public sealed record OrderAccepted(
                    System.Guid OrderId,
                    System.Guid ProcessId,
                    string CustomerReference,
                    System.DateTimeOffset OccurredAt);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_NoncompliantForSentMessageOutsideContractsNamespace() =>
        builder.AddSnippet(
            MessagingStub + """

            public sealed record OrderAccepted(System.Guid OrderId, System.Guid RequestId); // Noncompliant@-0 {{'RequestId' duplicates transport metadata - read it from the consume context instead.}}

            public sealed class OrderService
            {
                private readonly GP.Juno.Abstractions.ISender sender;

                public System.Threading.Tasks.Task Send(OrderAccepted message) => sender.Send(message);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForSqlDtoNameAlone() =>
        builder.AddSnippet(
            """
            namespace Database.Sql
            {
                internal sealed record GratisInfoDto(System.Guid RequestId);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            public class InboxRecord
            {
                public System.Guid MessageId { get; set; }
                public System.DateTimeOffset SentTime { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForHttpOnlyTrackingResponse() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                public sealed class HttpPostAttribute : System.Attribute { }
                public class ActionResult<T> { }
            }

            namespace Contracts
            {
                public sealed record CreatedDiscountCode(System.Guid CorrelationId);
            }

            public sealed class DiscountCodesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.ActionResult<Contracts.CreatedDiscountCode> Create() => null;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForHttpOnlyIdempotencyRequest() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                public sealed class ApiControllerAttribute : System.Attribute { }
                public sealed class HttpPostAttribute : System.Attribute { }
            }

            namespace Contracts
            {
                public sealed class IdempotentCustomerCreateModel
                {
                    public System.Guid RequestId { get; init; }
                }
            }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public sealed class CustomersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public void Create(Contracts.IdempotentCustomerCreateModel request) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForNestedCommandEnvelope() =>
        builder.AddSnippet(
            """
            namespace System.Text.Json
            {
                public readonly struct JsonElement { }
            }

            namespace Contracts
            {
                public sealed class CommandSequenceItem
                {
                    public string Command { get; init; }
                    public string RequestId { get; init; }
                    public System.Text.Json.JsonElement Payload { get; init; }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_CompliantForNestedScheduledMessageEnvelope() =>
        builder.AddSnippet(
            """
            namespace Contracts
            {
                public sealed class SpecificScheduleForEndpointRecurringCommand
                {
                    public System.Guid CorrelationId { get; init; }
                    public object Message { get; init; }
                    public string MessageType { get; init; }
                    public System.Collections.Generic.KeyValuePair<string, object>[] MessageHeaders { get; init; }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotDuplicateTransportMetadata_NoncompliantWhenHttpContractIsAlsoPublished() =>
        builder.AddSnippet(
            MessagingStub + """

            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                public sealed class HttpPostAttribute : System.Attribute { }
                public class ActionResult<T> { }
            }

            namespace Contracts
            {
                public sealed record CreatedDiscountCode(System.Guid CorrelationId); // Noncompliant
            }

            public sealed class DiscountCodesController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.ActionResult<Contracts.CreatedDiscountCode> Create() => null;
            }

            public sealed class Publisher
            {
                public System.Threading.Tasks.Task Publish(
                    GP.Juno.Abstractions.ISender sender,
                    Contracts.CreatedDiscountCode message) =>
                    sender.Send(message);
            }
            """)
            .Verify();
}
