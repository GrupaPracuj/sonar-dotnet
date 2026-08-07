using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class PublishedMessagesShouldComeFromContractAssembliesTest
{
    private const string MessagingStub =
        """
        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
            }
        }
        """;

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForServiceType() =>
        CreateBuilder()
            .AddSnippet(
                MessagingStub + """

                public sealed record OrderAccepted(System.Guid OrderId);

                public class OrderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Accept(System.Guid id) =>
                        _publisher.Publish(new OrderAccepted(id)); // Noncompliant {{Publish 'OrderAccepted' from a contract assembly; it is declared in 'project0'.}}
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForReferencedContractsAssembly()
    {
        var contracts = new SnippetCompiler(
            """
            namespace GP.Kaczawa.Contracts
            {
                public sealed class OrderAccepted
                {
                    public OrderAccepted(System.Guid orderId) { }
                }
            }
            """).Compilation
            .WithAssemblyName("GP.Kaczawa.Contracts")
            .ToMetadataReference();

        CreateBuilder()
            .AddReferences([contracts])
            .AddSnippet(
                MessagingStub + """

                public class OrderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Accept(System.Guid id) =>
                        _publisher.Publish(new GP.Kaczawa.Contracts.OrderAccepted(id));
                }
                """)
            .VerifyNoIssues();
    }

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForConfiguredAssemblyName()
    {
        var contracts = new SnippetCompiler(
            """
            namespace Shared.Messages
            {
                public sealed class OrderAccepted
                {
                    public OrderAccepted(System.Guid orderId) { }
                }
            }
            """).Compilation
            .WithAssemblyName("GP.Kaczawa.Messages")
            .ToMetadataReference();

        CreateBuilder("Messages")
            .AddReferences([contracts])
            .AddSnippet(
                MessagingStub + """

                public class OrderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Accept(System.Guid id) =>
                        _publisher.Publish(new Shared.Messages.OrderAccepted(id));
                }
                """)
            .VerifyNoIssues();
    }

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForAssemblyContainingSimilarWord()
    {
        var models = new SnippetCompiler(
            """
            namespace Shared.Models
            {
                public sealed class OrderAccepted
                {
                    public OrderAccepted(System.Guid orderId) { }
                }
            }
            """).Compilation
            .WithAssemblyName("GP.Kaczawa.ContractsLegacy")
            .ToMetadataReference();

        CreateBuilder()
            .AddReferences([models])
            .AddSnippet(
                MessagingStub + """

                public class OrderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Accept(System.Guid id) =>
                        _publisher.Publish(new Shared.Models.OrderAccepted(id)); // Noncompliant {{Publish 'OrderAccepted' from a contract assembly; it is declared in 'GP.Kaczawa.ContractsLegacy'.}}
                }
                """)
            .Verify();
    }

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForShapelessPayloadHandledByGP0055() =>
        CreateBuilder()
            .AddSnippet(
                MessagingStub + """

                public class OrderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Accept(System.Guid id) =>
                        _publisher.Publish(new { OrderId = id });
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForNonMessagingPublish() =>
        CreateBuilder()
            .AddSnippet(
                """
                public sealed record OrderAccepted(System.Guid OrderId);

                public class Recorder
                {
                    public void Publish<T>(T value) { }

                    public void Record(System.Guid id) => Publish(new OrderAccepted(id));
                }
                """)
            .VerifyNoIssues();

    private static VerifierBuilder CreateBuilder(string contractAssemblyNames = "Contracts") =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.PublishedMessagesShouldComeFromContractAssemblies { ContractAssemblyNames = contractAssemblyNames })
            .WithOptions(LanguageOptions.CSharpLatest);
}
