using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DomainShouldNotDependOnTransportTest
{
    private const string Stubs =
        """
        namespace MassTransit
        {
            public interface ConsumeContext<T> { }
            public interface IPublishEndpoint { }
        }

        namespace System.Net.Http
        {
            public class HttpResponseMessage { }
        }
        """;

    [TestMethod]
    public void DomainShouldNotDependOnTransport_NoncompliantForConsumeContextParameter() =>
        CreateBuilder("MyCompany.Orders.Domain")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Domain
            {
                public sealed class Order
                {
                    public void Accept(MassTransit.ConsumeContext<string> context) { } // Noncompliant {{'ConsumeContext' comes from 'MassTransit', which domain code should not depend on.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DomainShouldNotDependOnTransport_NoncompliantForHttpProperty() =>
        CreateBuilder("MyCompany.Orders.Domain")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Domain
            {
                public sealed class Order
                {
                    public System.Net.Http.HttpResponseMessage LastResponse { get; set; } // Noncompliant {{'HttpResponseMessage' comes from 'System.Net.Http', which domain code should not depend on.}}
                }
            }
            """)
            .Verify();

    // Hidden inside a generic argument, which is how it usually arrives.
    [TestMethod]
    public void DomainShouldNotDependOnTransport_NoncompliantInsideGenericReturnType() =>
        CreateBuilder("MyCompany.Orders.Domain")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Domain
            {
                public sealed class Order
                {
                    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> Send() => null; // Noncompliant {{'HttpResponseMessage' comes from 'System.Net.Http', which domain code should not depend on.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DomainShouldNotDependOnTransport_CompliantForPlainValues() =>
        CreateBuilder("MyCompany.Orders.Domain")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Domain
            {
                public sealed class Order
                {
                    public void Accept(System.Guid customerId, System.DateTimeOffset acceptedAt) { }
                }
            }
            """)
            .VerifyNoIssues();

    // Application and infrastructure code is exactly where these types belong.
    [TestMethod]
    public void DomainShouldNotDependOnTransport_CompliantOutsideDomainNamespace() =>
        CreateBuilder("MyCompany.Orders.Domain")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Application
            {
                public sealed class OrderConsumer
                {
                    public void Accept(MassTransit.ConsumeContext<string> context) { }
                }
            }
            """)
            .VerifyNoIssues();

    // Nothing configured and a non-domain assembly name means the rule cannot match anything.
    [TestMethod]
    public void DomainShouldNotDependOnTransport_CompliantWithoutConfiguration() =>
        new VerifierBuilder<CS.DomainShouldNotDependOnTransport>()
            .WithOptions(LanguageOptions.CSharpLatest)
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Domain
            {
                public sealed class Order
                {
                    public void Accept(MassTransit.ConsumeContext<string> context) { }
                }
            }
            """)
            .VerifyNoIssues();

    // Naming the assembly makes the whole of it domain code, so a namespace that was not listed is now in scope.
    [TestMethod]
    public void DomainShouldNotDependOnTransport_NoncompliantForConfiguredDomainAssembly() =>
        CreateBuilder(domainAssemblyNames: "project")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Application
            {
                public sealed class Order
                {
                    public void Accept(MassTransit.ConsumeContext<string> context) { } // Noncompliant {{'ConsumeContext' comes from 'MassTransit', which domain code should not depend on.}}
                }
            }
            """)
            .Verify();

    // The forbidden list replaces the defaults: what it names is still reported, what it leaves out is not.
    [TestMethod]
    public void DomainShouldNotDependOnTransport_ForbiddenNamespacesReplacesTheDefaults() =>
        CreateBuilder(domainNamespaces: "MyCompany.Orders.Domain", forbiddenNamespaces: "System.Net.Http")
            .AddSnippet(
            Stubs + """

            namespace MyCompany.Orders.Domain
            {
                public sealed class Order
                {
                    public System.Net.Http.HttpResponseMessage LastResponse { get; set; } // Noncompliant {{'HttpResponseMessage' comes from 'System.Net.Http', which domain code should not depend on.}}

                    public void Accept(MassTransit.ConsumeContext<string> context) { }
                }
            }
            """)
            .Verify();

    // A null argument leaves that parameter at the rule's own default, so a test states only what it actually varies.
    private static VerifierBuilder CreateBuilder(string domainNamespaces = "", string domainAssemblyNames = null, string forbiddenNamespaces = null) =>
        new VerifierBuilder()
            .AddAnalyzer(() => CreateAnalyzer(domainNamespaces, domainAssemblyNames, forbiddenNamespaces))
            .WithOptions(LanguageOptions.CSharpLatest);

    private static CS.DomainShouldNotDependOnTransport CreateAnalyzer(string domainNamespaces, string domainAssemblyNames, string forbiddenNamespaces)
    {
        var analyzer = new CS.DomainShouldNotDependOnTransport { DomainNamespaces = domainNamespaces };
        if (domainAssemblyNames is not null)
        {
            analyzer.DomainAssemblyNames = domainAssemblyNames;
        }
        if (forbiddenNamespaces is not null)
        {
            analyzer.ForbiddenNamespaces = forbiddenNamespaces;
        }
        return analyzer;
    }
}
