using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class ServiceDiscoveryShouldGoThroughJunoTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ServiceDiscoveryShouldGoThroughJuno>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Consul
        {
            public class ServiceEntry
            {
                public string ServiceAddress { get; set; }
            }

            public interface ICatalogEndpoint
            {
                System.Threading.Tasks.Task<ServiceEntry[]> Service(string name);
            }

            public interface IConsulClient
            {
                ICatalogEndpoint Catalog { get; }
                System.Threading.Tasks.Task AcquireLock(string key);
            }

            public class ConsulClient : IConsulClient
            {
                public ICatalogEndpoint Catalog => null;
                public System.Threading.Tasks.Task AcquireLock(string key) => null;
            }

            public class AgentServiceRegistration { }
        }
        """;

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantForCatalogQuery() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                private readonly Consul.IConsulClient _consul;

                public System.Threading.Tasks.Task<Consul.ServiceEntry[]> Resolve() =>
                    _consul.Catalog.Service("orders"); // Noncompliant {{Resolve the service through Juno instead of querying 'IConsulClient' directly.}}
                                                       // Noncompliant@-1 {{Resolve the service through Juno instead of querying 'ICatalogEndpoint' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantForClientConstruction() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                public Consul.IConsulClient Create() => new Consul.ConsulClient(); // Noncompliant {{Resolve the service through Juno instead of querying 'ConsulClient' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantForRegistration() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                public Consul.AgentServiceRegistration Register() => new Consul.AgentServiceRegistration(); // Noncompliant {{Resolve the service through Juno instead of querying 'AgentServiceRegistration' directly.}}
            }
            """)
            .Verify();

    // Locking on Consul belongs to GP0040, so it is not reported twice.
    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantForLockingCoveredByGP0040() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderImport
            {
                private readonly Consul.IConsulClient _consul;

                public System.Threading.Tasks.Task Import() => _consul.AcquireLock("order-import");
            }
            """)
            .VerifyNoIssues();

    // Juno is the layer that wraps Consul, so its own code is not reported.
    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantInsideJuno() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.Juno.Discovery
            {
                public class ConsulResolver
                {
                    private readonly Consul.IConsulClient _consul;

                    public System.Threading.Tasks.Task<Consul.ServiceEntry[]> Resolve() =>
                        _consul.Catalog.Service("orders");
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantWithoutConsul() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                public string Resolve() => "orders";
            }
            """)
            .VerifyNoIssues();
}
