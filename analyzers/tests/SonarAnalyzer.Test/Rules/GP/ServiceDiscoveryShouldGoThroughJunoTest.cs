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

            public interface IHealthEndpoint
            {
                System.Threading.Tasks.Task<ServiceEntry[]> Service(string name);
            }

            public interface IAgentEndpoint
            {
                System.Threading.Tasks.Task ServiceRegister(AgentServiceRegistration registration);
            }

            public interface IKVEndpoint
            {
                System.Threading.Tasks.Task Get(string key);
            }

            public interface IConsulClient
            {
                ICatalogEndpoint Catalog { get; }
                IKVEndpoint KV { get; }
                System.Threading.Tasks.Task AcquireLock(string key);
            }

            public class ConsulClient : IConsulClient
            {
                public ICatalogEndpoint Catalog => null;
                public IKVEndpoint KV => null;
                public System.Threading.Tasks.Task AcquireLock(string key) => null;
            }

            public class AgentServiceRegistration { }
        }

        namespace Akka.Cluster.Discovery
        {
            public abstract class DiscoveryService { }
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
                    _consul.Catalog.Service("orders"); // Noncompliant {{Resolve the service through Juno instead of querying 'ICatalogEndpoint' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantForClientConstructionAlone() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                public Consul.IConsulClient Create() => new Consul.ConsulClient();
            }
            """)
            .VerifyNoIssues();

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

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantForAgentRegistration() =>
        builder.AddSnippet(
            Stubs + """

            public class Startup
            {
                private readonly Consul.IAgentEndpoint _agent;

                public System.Threading.Tasks.Task Register() =>
                    _agent.ServiceRegister(new Consul.AgentServiceRegistration()); // Noncompliant {{Resolve the service through Juno instead of querying 'IAgentEndpoint' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantForConsulKeyValueAccess() =>
        builder.AddSnippet(
            Stubs + """

            public class Settings
            {
                private readonly Consul.IConsulClient _consul;

                public System.Threading.Tasks.Task Read() => _consul.KV.Get("settings");
            }
            """)
            .VerifyNoIssues();

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
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantInsideAkkaDiscoveryProviderAssembly() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class ConsulDiscoveryService : Akka.Cluster.Discovery.DiscoveryService
            {
                private readonly Consul.ICatalogEndpoint catalog;

                public System.Threading.Tasks.Task<Consul.ServiceEntry[]> Resolve() =>
                    catalog.Service("orders");
            }

            public static class RegistrationExtensions
            {
                public static Consul.AgentServiceRegistration CreateRegistration() =>
                    new Consul.AgentServiceRegistration();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantInsideJunoConsumerNamespace() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.JunoConsumer
            {
                public class ConsulResolver
                {
                    private readonly Consul.IConsulClient _consul;

                    public System.Threading.Tasks.Task<Consul.ServiceEntry[]> Resolve() =>
                        _consul.Catalog.Service("orders"); // Noncompliant {{Resolve the service through Juno instead of querying 'ICatalogEndpoint' directly.}}
                }
            }
            """)
            .Verify();

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
