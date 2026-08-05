using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ContractAssemblyShouldNotUseForbiddenTypesTest
{
    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }

        namespace MassTransit
        {
            public interface ConsumeContext<T> { }
        }
        """;

    // The verifier compiles snippets into an assembly whose name does not contain "Contracts", so the default
    // configuration would never fire - the tests name the assembly through the parameter instead.
    [TestMethod]
    public void ContractAssemblyShouldNotUseForbiddenTypes_NoncompliantForEntityFrameworkType() =>
        CreateBuilder()
            .AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public Microsoft.EntityFrameworkCore.DbContext Context { get; init; } // Noncompliant {{'DbContext' comes from 'Microsoft.EntityFrameworkCore', which a contract assembly should not depend on.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractAssemblyShouldNotUseForbiddenTypes_NoncompliantForRecordParameter() =>
        CreateBuilder()
            .AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(MassTransit.ConsumeContext<string> Context); // Noncompliant@-0 {{'ConsumeContext' comes from 'MassTransit', which a contract assembly should not depend on.}}
            """)
            .Verify();

    // A forbidden type hidden inside a generic argument is still a dependency of the assembly.
    [TestMethod]
    public void ContractAssemblyShouldNotUseForbiddenTypes_NoncompliantInsideGenericArgument() =>
        CreateBuilder()
            .AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public System.Collections.Generic.IReadOnlyList<Microsoft.EntityFrameworkCore.DbContext> Contexts { get; init; } // Noncompliant {{'DbContext' comes from 'Microsoft.EntityFrameworkCore', which a contract assembly should not depend on.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractAssemblyShouldNotUseForbiddenTypes_CompliantForBclOnlyContract() =>
        CreateBuilder()
            .AddSnippet(
            Stubs + """

            public sealed record OrderAcceptedContract(
                System.Guid OrderId,
                System.Collections.Generic.IReadOnlyList<string> Tags,
                System.DateTimeOffset OccurredAt);
            """)
            .VerifyNoIssues();

    // Outside a contract assembly the rule does not run at all.
    [TestMethod]
    public void ContractAssemblyShouldNotUseForbiddenTypes_CompliantOutsideContractAssembly() =>
        new VerifierBuilder<CS.ContractAssemblyShouldNotUseForbiddenTypes>()
            .WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public Microsoft.EntityFrameworkCore.DbContext Context { get; init; }
            }
            """)
            .VerifyNoIssues();

    // A namespace the team wants kept out of its contracts is reported once it is named, even though it is not one of the defaults.
    [TestMethod]
    public void ContractAssemblyShouldNotUseForbiddenTypes_NoncompliantForConfiguredNamespace() =>
        CreateBuilder("Shop.Internals")
            .AddSnippet(
            Stubs + """

            namespace Shop.Internals
            {
                public sealed class PricingEngine { }
            }

            public sealed class OrderAcceptedContract
            {
                public Shop.Internals.PricingEngine Pricing { get; init; } // Noncompliant {{'PricingEngine' comes from 'Shop.Internals', which a contract assembly should not depend on.}}
            }
            """)
            .Verify();

    // The parameter replaces the defaults, so the namespaces it does not name stop being reported.
    [TestMethod]
    public void ContractAssemblyShouldNotUseForbiddenTypes_CompliantForDefaultNamespaceWhenItIsNoLongerConfigured() =>
        CreateBuilder("Shop.Internals")
            .AddSnippet(
            Stubs + """

            public sealed class OrderAcceptedContract
            {
                public Microsoft.EntityFrameworkCore.DbContext Context { get; init; }
            }
            """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder() =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractAssemblyShouldNotUseForbiddenTypes { ContractAssemblyNames = "project" })
            .WithOptions(LanguageOptions.CSharpLatest);

    private static VerifierBuilder CreateBuilder(string forbiddenNamespaces) =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.ContractAssemblyShouldNotUseForbiddenTypes
            {
                ContractAssemblyNames = "project",
                ForbiddenNamespaces = forbiddenNamespaces,
            })
            .WithOptions(LanguageOptions.CSharpLatest);
}
