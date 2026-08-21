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
            public class QueryResult<T>
            {
                public T Response { get; set; }
            }

            public class ServiceEntry
            {
                public string ServiceAddress { get; set; }
            }

            public interface ICatalogEndpoint
            {
                System.Threading.Tasks.Task<QueryResult<string[]>> Datacenters();
                System.Threading.Tasks.Task<QueryResult<System.Collections.Generic.Dictionary<string, string[]>>> Services();
                System.Threading.Tasks.Task<QueryResult<ServiceEntry[]>> Service(string name);
            }

            public interface IHealthEndpoint
            {
                System.Threading.Tasks.Task<QueryResult<ServiceEntry[]>> Service(string name);
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
                IHealthEndpoint Health { get; }
                IKVEndpoint KV { get; }
                System.Threading.Tasks.Task AcquireLock(string key);
            }

            public class ConsulClient : IConsulClient
            {
                public ICatalogEndpoint Catalog => null;
                public IHealthEndpoint Health => null;
                public IKVEndpoint KV => null;
                public System.Threading.Tasks.Task AcquireLock(string key) => null;
            }

            public class AgentServiceRegistration { }
        }

        namespace Akka.Cluster.Discovery
        {
            public abstract class DiscoveryService { }
        }

        namespace System.Net.Http
        {
            public class HttpClient
            {
                public System.Uri BaseAddress { get; set; }
                public System.Threading.Tasks.Task<string> GetStringAsync(string requestUri) => null;
            }
        }
        """;

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantWhenCatalogResultFeedsOutboundCall() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                private readonly Consul.IConsulClient _consul;
                private readonly System.Net.Http.HttpClient _httpClient;

                public async System.Threading.Tasks.Task<string> Resolve()
                {
                    var services = await _consul.Catalog.Service("orders"); // Noncompliant {{Resolve the service through Juno instead of querying 'ICatalogEndpoint' directly.}}
                    var address = services.Response[0].ServiceAddress;
                    return await _httpClient.GetStringAsync($"http://{address}/orders");
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantWhenHealthResultFeedsOutboundCall() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                private readonly Consul.IConsulClient _consul;
                private readonly System.Net.Http.HttpClient _httpClient;

                public async System.Threading.Tasks.Task<string> Resolve()
                {
                    var services = await _consul.Health.Service("orders"); // Noncompliant {{Resolve the service through Juno instead of querying 'IHealthEndpoint' directly.}}
                    var address = services.Response[0].ServiceAddress;
                    return await _httpClient.GetStringAsync($"http://{address}/orders");
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_NoncompliantWhenCatalogResultConfiguresHttpClientBaseAddress() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderClient
            {
                private readonly Consul.IConsulClient _consul;
                private readonly System.Net.Http.HttpClient _httpClient;

                public async System.Threading.Tasks.Task<string> Resolve()
                {
                    var services = await _consul.Catalog.Service("orders"); // Noncompliant {{Resolve the service through Juno instead of querying 'ICatalogEndpoint' directly.}}
                    _httpClient.BaseAddress = new System.Uri($"http://{services.Response[0].ServiceAddress}");
                    return await _httpClient.GetStringAsync("/orders");
                }
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
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantForCatalogInventoryReads() =>
        builder.AddSnippet(
            Stubs + """

            public class DiscoveryInventory
            {
                private readonly Consul.IConsulClient _consul;

                public async System.Threading.Tasks.Task<object> Read()
                {
                    var datacenters = await _consul.Catalog.Datacenters();
                    var services = await _consul.Catalog.Services();
                    return new object[] { datacenters.Response, services.Response };
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantForHealthAddressListConstruction() =>
        builder.AddSnippet(
            Stubs + """

            public class DiscoveryInventory
            {
                private readonly Consul.IConsulClient _consul;

                public async System.Threading.Tasks.Task<string[]> Read()
                {
                    var services = await _consul.Health.Service("orders");
                    return new[] { services.Response[0].ServiceAddress };
                }
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

                    public System.Threading.Tasks.Task<Consul.QueryResult<Consul.ServiceEntry[]>> Resolve() =>
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

                public System.Threading.Tasks.Task<Consul.QueryResult<Consul.ServiceEntry[]>> Resolve() =>
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
    public void ServiceDiscoveryShouldGoThroughJuno_CompliantInsideJunoConsumerNamespaceWithoutOutboundCall() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.JunoConsumer
            {
                public class ConsulResolver
                {
                    private readonly Consul.IConsulClient _consul;

                    public System.Threading.Tasks.Task<Consul.QueryResult<Consul.ServiceEntry[]>> Resolve() =>
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
