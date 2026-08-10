using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractShouldEnableNullableReferenceTypesTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldEnableNullableReferenceTypes>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractShouldEnableNullableReferenceTypes_NoncompliantWithoutNullableContext() =>
        builder.AddSnippet(
            """
            public sealed record CandidateRegisteredContract(System.Guid CandidateId, string Email); // Noncompliant@-0 {{'CandidateRegisteredContract' is declared without nullable reference types, so its members do not say which values are optional.}}
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldEnableNullableReferenceTypes_NoncompliantForClassContract() =>
        builder.AddSnippet(
            """
            public sealed class OrderAcceptedEvent // Noncompliant {{'OrderAcceptedEvent' is declared without nullable reference types, so its members do not say which values are optional.}}
            {
                public string CustomerReference { get; init; }
            }
            """)
            .Verify();

    // A per-file "#nullable enable" is enough - the rule reads the context at the declaration, not the project setting.
    [TestMethod]
    public void ContractShouldEnableNullableReferenceTypes_CompliantWithFileScopedNullable() =>
        builder.AddSnippet(
            """
            #nullable enable

            public sealed record CandidateRegisteredContract(System.Guid CandidateId, string Email, string? MiddleName);
            """)
            .VerifyNoIssues();

    // Annotations are what matter; warnings may stay off.
    [TestMethod]
    public void ContractShouldEnableNullableReferenceTypes_CompliantWithAnnotationsOnly() =>
        builder.AddSnippet(
            """
            #nullable enable annotations

            public sealed record CandidateRegisteredContract(System.Guid CandidateId, string Email);
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldEnableNullableReferenceTypes_CompliantForNonContractType() =>
        builder.AddSnippet(
            """
            public sealed class CandidateProfile
            {
                public string Email { get; init; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldEnableNullableReferenceTypes_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("ContractShouldEnableNullableReferenceTypes.cs")
            .WithCodeFix<CS.ContractShouldEnableNullableReferenceTypesCodeFix>()
            .WithCodeFixedPaths("ContractShouldEnableNullableReferenceTypes.Fixed.cs")
            .VerifyCodeFix();

    [TestMethod]
    public void ContractShouldEnableNullableReferenceTypes_CodeFixReplacesDisable() =>
        builder.WithBasePath("GP")
            .AddPaths("ContractShouldEnableNullableReferenceTypes_Disabled.cs")
            .WithCodeFix<CS.ContractShouldEnableNullableReferenceTypesCodeFix>()
            .WithCodeFixedPaths("ContractShouldEnableNullableReferenceTypes_Disabled.Fixed.cs")
            .VerifyCodeFix();
}
