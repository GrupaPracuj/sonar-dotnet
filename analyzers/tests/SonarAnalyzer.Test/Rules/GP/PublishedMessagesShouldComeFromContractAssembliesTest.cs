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
public class PublishedMessagesShouldComeFromContractAssembliesTest
{
    private const string MessagingStub =
        """
        namespace GP.Juno.Abstractions.EventStream
        {
            public interface IPublisher
            {
                System.Threading.Tasks.Task Publish<T>(T @event) where T : class;
                System.Threading.Tasks.Task Send<T>(T command) where T : class;
            }
        }
        """;

    private const string MvcStub =
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpPostAttribute : System.Attribute { }
            public class ApiControllerAttribute : System.Attribute { }
            public class FromBodyAttribute : System.Attribute { }
            public class FromQueryAttribute : System.Attribute { }
            public class FromServicesAttribute : System.Attribute { }
            public interface IActionResult { }
            public class ActionResult<T> { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok(object value) => null;
            }
            public abstract class Controller : ControllerBase
            {
                protected IActionResult View(object value) => null;
            }
        }

        namespace Swashbuckle.AspNetCore.Annotations
        {
            public class SwaggerIgnoreAttribute : System.Attribute { }
        }
        """;

    private const string MinimalApiStub =
        """
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public static class EndpointRouteBuilderExtensions
            {
                public static void MapPost<TRequest, TResponse>(
                    this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints,
                    string pattern,
                    System.Func<TRequest, TResponse> handler) { }
            }
        }

        namespace Microsoft.AspNetCore.Http
        {
            public interface IResult { }

            public static class Results
            {
                public static IResult Ok<T>(T value) => null;
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
                        _publisher.Publish(new OrderAccepted(id)); // Noncompliant {{Declare 'OrderAccepted' in a contract assembly; this published message uses it from 'project0', which is not one.}}
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForSentCommand() =>
        CreateBuilder()
            .AddSnippet(
                MessagingStub + """

                public sealed record AcceptOrder(System.Guid OrderId);

                public class OrderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Accept(System.Guid id) =>
                        _publisher.Send(new AcceptOrder(id)); // Noncompliant {{Declare 'AcceptOrder' in a contract assembly; this sent command uses it from 'project0', which is not one.}}
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_JunoHttpSendIsNotMessaging() =>
        CreateBuilder()
            .AddSnippet(
                """
                namespace GP.Juno.HttpClient
                {
                    public class HttpServiceResponse { }

                    public class HttpRequestProperties
                    {
                        public System.Threading.Tasks.Task<T> Send<T>(object verb, T response)
                            where T : HttpServiceResponse => null;
                    }
                }

                public class Service
                {
                    public System.Threading.Tasks.Task<GP.Juno.HttpClient.HttpServiceResponse> Send(
                        GP.Juno.HttpClient.HttpRequestProperties request,
                        GP.Juno.HttpClient.HttpServiceResponse response) =>
                        request.Send(new object(), response);
                }
                """)
            .VerifyNoIssues();

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
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForReferencedInnerContractsAssembly()
    {
        var contracts = new SnippetCompiler(
            """
            namespace GP.Jowisz.InnerContracts.Commands
            {
                public sealed class CancelAcceptanceReminder { }
            }
            """).Compilation
            .WithAssemblyName("GP.Jowisz.InnerContracts")
            .ToMetadataReference();

        CreateBuilder()
            .AddReferences([contracts])
            .AddSnippet(
                MessagingStub + """

                public class ReminderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Cancel() =>
                        _publisher.Send(new GP.Jowisz.InnerContracts.Commands.CancelAcceptanceReminder());
                }
                """)
            .VerifyNoIssues();
    }

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForReferencedInternalContractsAssembly()
    {
        var contracts = new SnippetCompiler(
            """
            namespace GP.Jowisz.InternalContracts.Commands
            {
                public sealed class RebuildAcceptance { }
            }
            """).Compilation
            .WithAssemblyName("GP.Jowisz.InternalContracts")
            .ToMetadataReference();

        CreateBuilder()
            .AddReferences([contracts])
            .AddSnippet(
                MessagingStub + """

                public class AcceptanceService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Rebuild() =>
                        _publisher.Send(new GP.Jowisz.InternalContracts.Commands.RebuildAcceptance());
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
                        _publisher.Publish(new Shared.Models.OrderAccepted(id)); // Noncompliant {{Declare 'OrderAccepted' in a contract assembly; this published message uses it from 'GP.Kaczawa.ContractsLegacy', which is not one.}}
                }
                """)
            .Verify();
    }

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForReferencedSingularContractAssembly()
    {
        var payloads = new SnippetCompiler(
            """
            namespace Shared.Messages
            {
                public sealed class OrderAccepted
                {
                    public OrderAccepted(System.Guid orderId) { }
                }
            }
            """).Compilation
            .WithAssemblyName("GP.RestApi.Filestore.Contract")
            .ToMetadataReference();

        CreateBuilder()
            .AddReferences([payloads])
            .AddSnippet(
                MessagingStub + """

                public class OrderService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Accept(System.Guid id) =>
                        _publisher.Publish(new Shared.Messages.OrderAccepted(id)); // Noncompliant {{Declare 'OrderAccepted' in a contract assembly; this published message uses it from 'GP.RestApi.Filestore.Contract', which is not one.}}
                }
                """)
            .Verify();
    }

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForReferencedCommandsAssembly()
    {
        var payloads = new SnippetCompiler(
            """
            namespace Shared.Messages
            {
                public sealed class RebuildAcceptance { }
            }
            """).Compilation
            .WithAssemblyName("GP.FileStoreTools.Commands")
            .ToMetadataReference();

        CreateBuilder()
            .AddReferences([payloads])
            .AddSnippet(
                MessagingStub + """

                public class AcceptanceService
                {
                    private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                    public System.Threading.Tasks.Task Rebuild() =>
                        _publisher.Send(new Shared.Messages.RebuildAcceptance()); // Noncompliant {{Declare 'RebuildAcceptance' in a contract assembly; this sent command uses it from 'GP.FileStoreTools.Commands', which is not one.}}
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

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForMassTransitPipelineSend() =>
        CreateBuilder()
            .AddSnippet(
                """
                namespace MassTransit
                {
                    public interface PublishContext<T> { }

                    public interface IPipe<T>
                    {
                        System.Threading.Tasks.Task Send(T context);
                    }
                }

                public sealed class HeaderFilter<TMessage>
                {
                    public System.Threading.Tasks.Task Send(
                        MassTransit.PublishContext<TMessage> context,
                        MassTransit.IPipe<MassTransit.PublishContext<TMessage>> next) =>
                        next.Send(context);
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForMassTransitSendEndpoint() =>
        CreateBuilder()
            .AddSnippet(
                """
                namespace MassTransit
                {
                    public interface ISendEndpoint
                    {
                        System.Threading.Tasks.Task Send<T>(T command);
                    }
                }

                public sealed record AcceptOrder(System.Guid OrderId);

                public sealed class OrderService
                {
                    public System.Threading.Tasks.Task Accept(
                        MassTransit.ISendEndpoint endpoint,
                        System.Guid orderId) =>
                        endpoint.Send(new AcceptOrder(orderId)); // Noncompliant {{Declare 'AcceptOrder' in a contract assembly; this sent command uses it from 'project0', which is not one.}}
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForMvcRequestAndTypedResponse() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public sealed record CreateOrder(string Name);
                public sealed record OrderCreated(System.Guid Id);

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<OrderCreated>> Create(
                        CreateOrder request) => // Noncompliant {{Declare 'CreateOrder' in a contract assembly; this REST request uses it from 'project0', which is not one.}}
                        null; // Noncompliant@-2 {{Declare 'OrderCreated' in a contract assembly; this REST response uses it from 'project0', which is not one.}}
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForMvcViewModel() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public sealed class OrdersFilterViewModel { }

                public class OrdersController : Microsoft.AspNetCore.Mvc.Controller
                {
                    public Microsoft.AspNetCore.Mvc.IActionResult Index(
                        [Microsoft.AspNetCore.Mvc.FromQuery] OrdersFilterViewModel filter) =>
                        View(filter);
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForViewRenderingController() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public sealed class ExecuteOrder { }

                public sealed class ExecuteOrderViewModel { }

                public class OrdersController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public Microsoft.AspNetCore.Mvc.IActionResult Execute(
                        [Microsoft.AspNetCore.Mvc.FromBody] ExecuteOrder request) =>
                        Ok(new ExecuteOrderViewModel());
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForApiControllerAttributeOnViewRenderingBase() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public sealed class ExecuteOrder { }

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class OrdersController : Microsoft.AspNetCore.Mvc.Controller
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public Microsoft.AspNetCore.Mvc.IActionResult Execute(
                        [Microsoft.AspNetCore.Mvc.FromBody] ExecuteOrder request) => // Noncompliant {{Declare 'ExecuteOrder' in a contract assembly; this REST request uses it from 'project0', which is not one.}}
                        Ok(null);
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForSwaggerIgnoredInfrastructureParameter() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public readonly struct ETag { }

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public Microsoft.AspNetCore.Mvc.IActionResult Update(
                        [Swashbuckle.AspNetCore.Annotations.SwaggerIgnore] ETag ifMatch) =>
                        Ok(null);
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForJunoTokenContext() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                namespace GP.Juno.Hosting.AspNetCore.Security.UserContexts
                {
                    public sealed class FromTokenAttribute : System.Attribute { }
                }

                public sealed class CompanyUserContext { }

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public Microsoft.AspNetCore.Mvc.IActionResult Create(
                        [GP.Juno.Hosting.AspNetCore.Security.UserContexts.FromToken] CompanyUserContext userContext) =>
                        Ok(null);
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForCustomMvcResultWrapper() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public sealed class HttpResponseMessageActionResult : Microsoft.AspNetCore.Mvc.IActionResult { }

                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public HttpResponseMessageActionResult Create() => null;
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForMvcRuntimeResponse() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public sealed record OrderCreated(System.Guid Id);

                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public Microsoft.AspNetCore.Mvc.IActionResult Create() =>
                        Ok(new OrderCreated(System.Guid.NewGuid())); // Noncompliant {{Declare 'OrderCreated' in a contract assembly; this REST response uses it from 'project0', which is not one.}}
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForCustomMinimalApiResultWrapper() =>
        CreateBuilder()
            .AddSnippet(
                MinimalApiStub + """

                public sealed class HttpResponseMessageResult : Microsoft.AspNetCore.Http.IResult { }

                public static class Endpoints
                {
                    public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost<object, HttpResponseMessageResult>(
                            app,
                            "/orders",
                            _ => new HttpResponseMessageResult());
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_NoncompliantForMinimalApiRequestAndResponse() =>
        CreateBuilder()
            .AddSnippet(
                MinimalApiStub + """

                public sealed record CreateOrder(string Name);
                public sealed record OrderCreated(System.Guid Id);

                public static class Endpoints
                {
                    public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(
                            app,
                            "/orders",
                            (CreateOrder request) => // Noncompliant {{Declare 'CreateOrder' in a contract assembly; this REST request uses it from 'project0', which is not one.}}
                                Microsoft.AspNetCore.Http.Results.Ok(
                                    new OrderCreated(System.Guid.NewGuid()))); // Noncompliant@-1 {{Declare 'OrderCreated' in a contract assembly; this REST response uses it from 'project0', which is not one.}}
                }
                """)
            .Verify();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForBuiltInPayloadsAndExplicitService() =>
        CreateBuilder()
            .AddSnippet(
                MvcStub + """

                public sealed class OrderService { }

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IReadOnlyList<string>>> Create(
                        System.Guid id,
                        [Microsoft.AspNetCore.Mvc.FromServices] OrderService service) =>
                        null;
                }
                """)
            .VerifyNoIssues();

    [TestMethod]
    public void PublishedMessagesShouldComeFromContractAssemblies_CompliantForMvcContractsAssembly()
    {
        var contracts = new SnippetCompiler(
            """
            namespace GP.Kaczawa.Contracts
            {
                public sealed class CreateOrder { }
                public sealed class OrderCreated { }
            }
            """).Compilation
            .WithAssemblyName("GP.Kaczawa.Contracts")
            .ToMetadataReference();

        CreateBuilder()
            .AddReferences([contracts])
            .AddSnippet(
                MvcStub + """

                [Microsoft.AspNetCore.Mvc.ApiController]
                public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpPost]
                    public Microsoft.AspNetCore.Mvc.ActionResult<GP.Kaczawa.Contracts.OrderCreated> Create(
                        GP.Kaczawa.Contracts.CreateOrder request) =>
                        null;
                }
                """)
            .VerifyNoIssues();
    }

    private static VerifierBuilder CreateBuilder(string contractAssemblyNames = CS.GpAssemblyNames.DefaultContractAssemblyNames) =>
        new VerifierBuilder()
            .AddAnalyzer(() => new CS.PublishedMessagesShouldComeFromContractAssemblies { ContractAssemblyNames = contractAssemblyNames })
            .WithOptions(LanguageOptions.CSharpLatest);
}
