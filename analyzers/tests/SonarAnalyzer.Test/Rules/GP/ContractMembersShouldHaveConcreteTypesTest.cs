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

        public interface IPaymentMethod { }

        public abstract class PricingStrategy { }

        public sealed class PaymentMethodContract : IPaymentMethod { }
        """;

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForInterface() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public IPaymentMethod Payment { get; init; } // Noncompliant {{'Payment' is declared as the interface 'IPaymentMethod', so a consumer cannot tell what to deserialize it into.}}
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
            """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_NoncompliantForRecordParameter() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(System.Guid OrderId, IPaymentMethod Payment); // Noncompliant@-0 {{'Payment' is declared as the interface 'IPaymentMethod', so a consumer cannot tell what to deserialize it into.}}
            """)
            .Verify();

    // Read-only collection interfaces are the shapes GP0058 asks for.
    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForReadOnlyCollections() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(
                System.Collections.Generic.IReadOnlyList<string> Tags,
                System.Collections.Generic.IReadOnlyCollection<int> Quantities,
                System.Collections.Generic.IReadOnlyDictionary<string, string> Metadata);
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
            """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForConcreteType() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(System.Guid OrderId, PaymentMethodContract Payment);
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
                """)
            .Verify();

    [TestMethod]
    public void ContractMembersShouldHaveConcreteTypes_CompliantForNonContractType() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderProcessor
            {
                public IPaymentMethod Payment { get; init; }
            }
            """)
            .VerifyNoIssues();
}
