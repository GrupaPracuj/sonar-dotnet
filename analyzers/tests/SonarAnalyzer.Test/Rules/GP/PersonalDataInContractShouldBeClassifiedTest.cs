using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PersonalDataInContractShouldBeClassifiedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PersonalDataInContractShouldBeClassified>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        public class PersonalDataAttribute : System.Attribute { }

        public interface IContainsPersonalData { }
        """;

    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_NoncompliantForUnclassifiedProperty() =>
        builder.AddSnippet(
            Stubs + """

            public class CandidateRegisteredEvent
            {
                public System.Guid CandidateId { get; set; }
                public string Email { get; set; } // Noncompliant {{'Email' is personal data - classify it with an approved attribute or interface.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_NoncompliantForPositionalRecord() =>
        builder.AddSnippet(
            Stubs + """

            public sealed record CandidateRegisteredContract(System.Guid CandidateId, string Pesel); // Noncompliant@-0 {{'Pesel' is personal data - classify it with an approved attribute or interface.}}
            """)
            .Verify();

    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_CompliantWhenMemberIsClassified() =>
        CreateBuilderWithConfiguration(attributes: "PersonalData")
            .AddSnippet(
            Stubs + """

            public class CandidateRegisteredEvent
            {
                [PersonalData]
                public string Email { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_CompliantWhenContractIsClassified() =>
        CreateBuilderWithConfiguration(attributes: "PersonalData")
            .AddSnippet(
            Stubs + """

            [PersonalData]
            public sealed record CandidateRegisteredContract(System.Guid CandidateId, string Email, string Phone);
            """)
            .VerifyNoIssues();

    // On a positional record the attribute is usually written with the property: target, which puts it on the
    // generated property rather than on the parameter.
    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_CompliantForPropertyTargetedAttribute() =>
        CreateBuilderWithConfiguration(attributes: "PersonalData")
            .AddSnippet(
            Stubs + """

            public sealed record CandidateRegisteredContract(System.Guid CandidateId, [property: PersonalData] string Email);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_CompliantWhenContractImplementsMarker() =>
        CreateBuilderWithConfiguration(interfaces: "IContainsPersonalData")
            .AddSnippet(
            Stubs + """

            public class CandidateRegisteredEvent : IContainsPersonalData
            {
                public string Email { get; set; }
            }
            """)
            .VerifyNoIssues();

    // Configuration is per-solution, so an attribute that is not configured does not classify anything.
    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_NoncompliantWhenAttributeIsNotConfigured() =>
        builder.AddSnippet(
            Stubs + """

            public class CandidateRegisteredEvent
            {
                [PersonalData]
                public string Email { get; set; } // Noncompliant {{'Email' is personal data - classify it with an approved attribute or interface.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PersonalDataInContractShouldBeClassified_CompliantForNonContractType() =>
        builder.AddSnippet(
            Stubs + """

            public class CandidateProfile
            {
                public string Email { get; set; }
            }
            """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilderWithConfiguration(string attributes = "", string interfaces = "") =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.PersonalDataInContractShouldBeClassified
            {
                ClassificationAttributes = attributes,
                ClassificationInterfaces = interfaces,
            })
            .WithOptions(LanguageOptions.CSharpLatest);
}
