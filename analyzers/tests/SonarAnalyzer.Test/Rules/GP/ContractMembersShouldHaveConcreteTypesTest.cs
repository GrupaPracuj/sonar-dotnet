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
public class ContractMembersShouldHaveConcreteTypesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractMembersShouldHaveConcreteTypes>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace System.Text.Json.Serialization
        {
            public class JsonDerivedTypeAttribute : System.Attribute
            {
                public JsonDerivedTypeAttribute(System.Type type, string discriminator) { }
            }

            public class JsonConverterAttribute : System.Attribute
            {
                public JsonConverterAttribute(System.Type type) { }
            }
        }

        namespace Contracts
        {
        public interface IPaymentMethod { }

        public abstract class PricingStrategy { }

        public sealed class PaymentMethodContract : IPaymentMethod { }
        """;

    [TestMethod]
    // The MassTransit style: the contract is an interface, the bus builds the implementation from it, and a publisher
    // fills it in with an anonymous object. Mirrors ISendMultiStepRegisterEmailCommand from GP.Smerfetka.
    public void ContractMembersShouldHaveConcreteTypes_CompliantForInterfaceMemberOnInterfaceContract() =>
        builder.AddSnippet(
            Stubs + """

            public interface ISendMultiStepRegisterEmailCommand
            {
                System.Guid ProcessId { get; }
                IPaymentMethod Payment { get; }
            }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    // The exemption is about interface contracts only: an abstract class still has no wire representation the bus can
    // invent, so it stays reported even there.
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForAbstractMemberOnInterfaceContract() =>
        builder.AddSnippet(
            Stubs + """

            public interface IOrderAccepted
            {
                PricingStrategy Pricing { get; } // Noncompliant {{'Pricing' is declared as the abstract type 'PricingStrategy', so a consumer cannot tell what to deserialize it into.}}
            }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForInterface() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public IPaymentMethod Payment { get; init; } // Noncompliant {{'Payment' is declared as the interface 'IPaymentMethod', so a consumer cannot tell what to deserialize it into.}}
            }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForAbstractClass() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public PricingStrategy Pricing { get; init; } // Noncompliant {{'Pricing' is declared as the abstract type 'PricingStrategy', so a consumer cannot tell what to deserialize it into.}}
            }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForRecordParameter() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(System.Guid OrderId, IPaymentMethod Payment); // Noncompliant@-0 {{'Payment' is declared as the interface 'IPaymentMethod', so a consumer cannot tell what to deserialize it into.}}
            }
            """)
            .Verify();

    // Standard collection interfaces have well-defined concrete types in JSON serializers.
    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForMaterializedCollections() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class CreateSecretRequest
            {
                public System.Collections.Generic.IDictionary<string, string> Metadata { get; init; }
                public System.Collections.Generic.ICollection<string> Items { get; init; }
                public System.Collections.Generic.IList<string> ErrorMessages { get; init; }
                public System.Collections.Generic.ISet<string> Tags { get; init; }
                public System.Collections.Generic.IReadOnlyList<string> ReadOnlyTags { get; init; }
                public System.Collections.Generic.IReadOnlyCollection<int> Quantities { get; init; }
                public System.Collections.Generic.IReadOnlyDictionary<string, string> ReadOnlyMetadata { get; init; }
            }
            }
            """)
            .VerifyNoIssues();

    // IEnumerable is GP0058's case, reported there on stronger grounds, so it is not reported twice.
    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForLazySequenceOwnedByGP0058() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public System.Collections.Generic.IEnumerable<string> Tags { get; init; }
            }
            }
            """)
            .VerifyNoIssues();

    // The decision has been made and written down.
    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantWhenPolymorphismIsConfigured() =>
        builder.AddSnippet(
            Stubs + """

            [System.Text.Json.Serialization.JsonDerivedType(typeof(PaymentMethodContract), "card")]
            public abstract class PaymentMethodBase { }

            public sealed class OrderAcceptedContract
            {
                public PaymentMethodBase Payment { get; init; }
            }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForMemberJsonConverter() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class PaymentConverter { }

            public sealed class OrderAcceptedContract
            {
                [System.Text.Json.Serialization.JsonConverter(typeof(PaymentConverter))]
                public IPaymentMethod Payment { get; init; }
            }

            public sealed record PaymentAcceptedContract(
                [System.Text.Json.Serialization.JsonConverter(typeof(PaymentConverter))] IPaymentMethod Payment);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForLookalikeJsonConverter() =>
        builder.AddSnippet(
            Stubs + """

            namespace Custom
            {
                public class JsonConverterAttribute : System.Attribute
                {
                    public JsonConverterAttribute(System.Type type) { }
                }
            }

            public sealed class PaymentConverter { }

            public sealed class OrderAcceptedContract
            {
                [Custom.JsonConverter(typeof(PaymentConverter))]
                public IPaymentMethod Payment { get; init; } // Noncompliant
            }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForConcreteType() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(System.Guid OrderId, PaymentMethodContract Payment);
            }
            """)
            .VerifyNoIssues();

    // An interface the team has decided to allow stops being reported, without switching the rule off for every other interface.
    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForConfiguredAllowedInterface() =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractMembersShouldHaveConcreteTypes { AllowedInterfaces = "IPaymentMethod" })
            .WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
                Stubs + """

                public sealed class OrderAcceptedContract
                {
                    public IPaymentMethod Payment { get; init; }
                }
                }
                """)
            .VerifyNoIssues();

    // The parameter replaces the defaults rather than adding to them, so the read-only collections are no longer exempt.
    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForReadOnlyListWhenItIsNotAllowed() =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractMembersShouldHaveConcreteTypes { AllowedInterfaces = "IPaymentMethod" })
            .WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
                Stubs + """

                public sealed class OrderAcceptedContract
                {
                    public System.Collections.Generic.IReadOnlyList<string> Tags { get; init; } // Noncompliant {{'Tags' is declared as the interface 'IReadOnlyList', so a consumer cannot tell what to deserialize it into.}}
                }
                }
                """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForNonContractType() =>
        builder.AddSnippet(
            Stubs + """
            }

            public sealed class OrderProcessor
            {
                public Contracts.IPaymentMethod Payment { get; init; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForInternalApplicationCommandName() =>
        builder.AddSnippet(
            """
            public interface Caller { }

            internal sealed record CreateInvitationCommand(string Email, Caller Caller);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForPublishedTypeOutsideContractsNamespace() =>
        builder.AddSnippet(
            """
            namespace GP.Juno.Abstractions
            {
                public interface IPublisher
                {
                    System.Threading.Tasks.Task Publish<T>(T message) where T : class;
                }
            }

            public interface IPaymentMethod { }

            public sealed class PaymentPublished
            {
                public IPaymentMethod Payment { get; init; } // Noncompliant {{'Payment' is declared as the interface 'IPaymentMethod', so a consumer cannot tell what to deserialize it into.}}
            }

            public sealed class PaymentService
            {
                private readonly GP.Juno.Abstractions.IPublisher publisher;

                public System.Threading.Tasks.Task Publish(PaymentPublished message) => publisher.Publish(message);
            }
            """)
            .Verify();
}
