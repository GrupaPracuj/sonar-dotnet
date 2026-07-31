using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DatabaseTransactionsShouldNotContainExternalNetworkCallsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DatabaseTransactionsShouldNotContainExternalNetworkCalls>();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantPublishInsideTransactionForUnknownType() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            public interface IEventStream
            {
                Task Publish(object message);
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly IEventStream _eventStream;

                public Service(ITransactional transactional, IEventStream eventStream)
                {
                    _transactional = transactional;
                    _eventStream = eventStream;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        await _eventStream.Publish(order);
                        transaction.Commit();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantOnlySqlStepsInsideTransaction() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            public class Service
            {
                private readonly ITransactional _transactional;

                public Service(ITransactional transactional) => _transactional = transactional;

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        transaction.Commit();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantPublishAfterCommit() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            public interface IEventStream
            {
                Task Publish(object message);
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly IEventStream _eventStream;

                public Service(ITransactional transactional, IEventStream eventStream)
                {
                    _transactional = transactional;
                    _eventStream = eventStream;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        transaction.Commit();
                    }

                    await _eventStream.Publish(order);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoPublisherInsideRunInTransaction() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using GP.Juno.Ado;

            namespace GP.Juno.Abstractions.Ado
            {
                public interface ITransactional { }
            }

            namespace GP.Juno.Abstractions.EventStream
            {
                public interface IPublisher
                {
                    Task Publish(object @event, CancellationToken cancellationToken = default(CancellationToken));
                }
            }

            namespace GP.Juno.Ado
            {
                using GP.Juno.Abstractions.Ado;

                public static class TransactionalExtensions
                {
                    public static Task RunInTransaction(this ITransactional transactional, Func<object, Task> inTransaction)
                    {
                        return inTransaction(null);
                    }
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;
                private readonly GP.Juno.Abstractions.EventStream.IPublisher _publisher;

                public Service(GP.Juno.Abstractions.Ado.ITransactional transactional, GP.Juno.Abstractions.EventStream.IPublisher publisher)
                {
                    _transactional = transactional;
                    _publisher = publisher;
                }

                public async Task Save()
                {
                    await _transactional.RunInTransaction(async tx =>
                    {
                        await _publisher.Publish(new object()); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoHttpSenderInsideRunInTransaction() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using GP.Juno.Ado;

            namespace GP.Juno.Abstractions.Ado
            {
                public interface ITransactional { }
            }

            namespace GP.Juno.HttpApiClient.HttpSending
            {
                public class HttpSender : IDisposable
                {
                    public Task<HttpSenderResponse> Get(CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new HttpSenderResponse());
                    }

                    public void Dispose() { }
                }

                public class HttpSenderResponse : IDisposable
                {
                    public void Dispose() { }
                }

                public interface IHttpSenderFactory
                {
                    HttpSender Create(string name);
                }
            }

            namespace GP.Juno.Ado
            {
                using GP.Juno.Abstractions.Ado;

                public static class TransactionalExtensions
                {
                    public static Task RunInTransaction(this ITransactional transactional, Func<object, Task> inTransaction)
                    {
                        return inTransaction(null);
                    }
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;
                private readonly GP.Juno.HttpApiClient.HttpSending.IHttpSenderFactory _senderFactory;

                public Service(GP.Juno.Abstractions.Ado.ITransactional transactional, GP.Juno.HttpApiClient.HttpSending.IHttpSenderFactory senderFactory)
                {
                    _transactional = transactional;
                    _senderFactory = senderFactory;
                }

                public async Task Save(CancellationToken cancellationToken)
                {
                    await _transactional.RunInTransaction(async tx =>
                    {
                        using (var sender = _senderFactory.Create("orders"))
                        {
                            await sender.Get(cancellationToken); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                        }
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantJunoRunInTransactionWithoutNetwork() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;
            using GP.Juno.Ado;

            namespace GP.Juno.Abstractions.Ado
            {
                public interface ITransactional { }
            }

            namespace GP.Juno.Ado
            {
                using GP.Juno.Abstractions.Ado;

                public static class TransactionalExtensions
                {
                    public static Task RunInTransaction(this ITransactional transactional, Func<object, Task> inTransaction)
                    {
                        return inTransaction(null);
                    }
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;

                public Service(GP.Juno.Abstractions.Ado.ITransactional transactional)
                {
                    _transactional = transactional;
                }

                public async Task Save()
                {
                    await _transactional.RunInTransaction(async tx =>
                    {
                        await Task.Delay(1);
                    });
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoEventStreamSendInsideRunInTransaction() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using GP.Juno.Ado;

            namespace GP.Juno.Abstractions.Ado
            {
                public interface ITransactional { }
            }

            namespace GP.Juno.EventStream
            {
                public interface EventStream
                {
                    Task Send(object message, CancellationToken cancellationToken = default(CancellationToken));
                }
            }

            namespace GP.Juno.Ado
            {
                using GP.Juno.Abstractions.Ado;

                public static class TransactionalExtensions
                {
                    public static Task RunInTransaction(this ITransactional transactional, Func<object, Task> inTransaction)
                    {
                        return inTransaction(null);
                    }
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;
                private readonly GP.Juno.EventStream.EventStream _eventStream;

                public Service(GP.Juno.Abstractions.Ado.ITransactional transactional, GP.Juno.EventStream.EventStream eventStream)
                {
                    _transactional = transactional;
                    _eventStream = eventStream;
                }

                public async Task Save()
                {
                    await _transactional.RunInTransaction(async tx =>
                    {
                        await _eventStream.Send(new object()); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoEventStreamInheritedPublishInsideUsing() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            // Matches the real GP.Juno.EventStream.EventStream shape: it declares no members of its own,
            // Publish is only inherited from a base interface (MassTransit's IPublishEndpoint in production).
            public interface IPublishEndpoint
            {
                Task Publish(object message);
            }

            namespace GP.Juno.EventStream
            {
                public interface EventStream : IPublishEndpoint
                {
                }
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly GP.Juno.EventStream.EventStream _eventStream;

                public Service(ITransactional transactional, GP.Juno.EventStream.EventStream eventStream)
                {
                    _transactional = transactional;
                    _eventStream = eventStream;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        await _eventStream.Publish(order); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                        transaction.Commit();
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoEventStreamExtensionMethodInsideUsing() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;
            using GP.Juno.EventStream;

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            namespace GP.Juno.EventStream
            {
                public interface EventStream
                {
                }

                // Matches the real GP.Juno.EventStream.SendTimeout.PublishWithTimeoutExtensions.Publish(this EventStream, ...) shape.
                public static class PublishWithTimeoutExtensions
                {
                    public static Task Publish(this EventStream eventStream, object message, System.TimeSpan timeout) =>
                        System.Threading.Tasks.Task.CompletedTask;
                }
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly GP.Juno.EventStream.EventStream _eventStream;

                public Service(ITransactional transactional, GP.Juno.EventStream.EventStream eventStream)
                {
                    _transactional = transactional;
                    _eventStream = eventStream;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        await _eventStream.Publish(order, System.TimeSpan.FromSeconds(5)); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                        transaction.Commit();
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoHttpSenderPostJsonInsideRunInTransaction() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using GP.Juno.Ado;

            namespace GP.Juno.Abstractions.Ado
            {
                public interface ITransactional { }
            }

            namespace GP.Juno.HttpApiClient.HttpSending
            {
                public class HttpSender : IDisposable
                {
                    public Task<HttpSenderResponse> PostJson<T>(T obj, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new HttpSenderResponse());
                    }

                    public void Dispose() { }
                }

                public class HttpSenderResponse : IDisposable
                {
                    public void Dispose() { }
                }

                public interface IHttpSenderFactory
                {
                    HttpSender Create(string name);
                }
            }

            namespace GP.Juno.Ado
            {
                using GP.Juno.Abstractions.Ado;

                public static class TransactionalExtensions
                {
                    public static Task RunInTransaction(this ITransactional transactional, Func<object, Task> inTransaction)
                    {
                        return inTransaction(null);
                    }
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;
                private readonly GP.Juno.HttpApiClient.HttpSending.IHttpSenderFactory _senderFactory;

                public Service(GP.Juno.Abstractions.Ado.ITransactional transactional, GP.Juno.HttpApiClient.HttpSending.IHttpSenderFactory senderFactory)
                {
                    _transactional = transactional;
                    _senderFactory = senderFactory;
                }

                public async Task Save(CancellationToken cancellationToken)
                {
                    await _transactional.RunInTransaction(async tx =>
                    {
                        using (var sender = _senderFactory.Create("orders"))
                        {
                            await sender.PostJson(new { Id = 1 }, cancellationToken); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                        }
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoIHttpClientSendInsideRunInTransaction() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;
            using GP.Juno.Ado;

            namespace GP.Juno.Abstractions.Ado
            {
                public interface ITransactional { }
            }

            namespace GP.Juno.HttpClient
            {
                public interface IHttpClient
                {
                    Task<object> Send(object verb, Func<object, object> contentFactory = null);
                }
            }

            namespace GP.Juno.Ado
            {
                using GP.Juno.Abstractions.Ado;

                public static class TransactionalExtensions
                {
                    public static Task RunInTransaction(this ITransactional transactional, Func<object, Task> inTransaction)
                    {
                        return inTransaction(null);
                    }
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;
                private readonly GP.Juno.HttpClient.IHttpClient _httpClient;

                public Service(GP.Juno.Abstractions.Ado.ITransactional transactional, GP.Juno.HttpClient.IHttpClient httpClient)
                {
                    _transactional = transactional;
                    _httpClient = httpClient;
                }

                public async Task Save()
                {
                    await _transactional.RunInTransaction(async tx =>
                    {
                        await _httpClient.Send(new object()); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantJunoHttpSenderHeadInsideRunInTransaction() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using GP.Juno.Ado;

            namespace GP.Juno.Abstractions.Ado
            {
                public interface ITransactional { }
            }

            namespace GP.Juno.HttpApiClient.HttpSending
            {
                public class HttpSender : IDisposable
                {
                    public Task<HttpSenderResponse> Head(CancellationToken cancellationToken) => Task.FromResult(new HttpSenderResponse());
                    public void Dispose() { }
                }

                public class HttpSenderResponse : IDisposable
                {
                    public void Dispose() { }
                }

                public interface IHttpSenderFactory
                {
                    HttpSender Create(string name);
                }
            }

            namespace GP.Juno.Ado
            {
                using GP.Juno.Abstractions.Ado;

                public static class TransactionalExtensions
                {
                    public static Task RunInTransaction(this ITransactional transactional, Func<object, Task> inTransaction) => inTransaction(null);
                }
            }

            public class Service
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;
                private readonly GP.Juno.HttpApiClient.HttpSending.IHttpSenderFactory _senderFactory;

                public Service(GP.Juno.Abstractions.Ado.ITransactional transactional, GP.Juno.HttpApiClient.HttpSending.IHttpSenderFactory senderFactory)
                {
                    _transactional = transactional;
                    _senderFactory = senderFactory;
                }

                public async Task Save(CancellationToken cancellationToken)
                {
                    await _transactional.RunInTransaction(async tx =>
                    {
                        using (var sender = _senderFactory.Create("orders"))
                        {
                            await sender.Head(cancellationToken); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                        }
                    });
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantInsideTransactionScopeForUnknownType() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;

            namespace System.Transactions
            {
                public class TransactionScope : IDisposable
                {
                    public void Complete() { }
                    public void Dispose() { }
                }
            }

            public class EventStream
            {
                public Task Publish(object message) => Task.CompletedTask;
            }

            public class Service
            {
                private readonly EventStream _eventStream = new EventStream();

                public async Task Save()
                {
                    using (var scope = new System.Transactions.TransactionScope())
                    {
                        await _eventStream.Publish(new object());
                        scope.Complete();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantCallAfterTransactionScopeComplete() =>
        builder.AddSnippet(
            """
            using System;
            using System.Threading.Tasks;

            namespace System.Transactions
            {
                public class TransactionScope : IDisposable
                {
                    public void Complete() { }
                    public void Dispose() { }
                }
            }

            public class EventStream
            {
                public Task Publish(object message) => Task.CompletedTask;
            }

            public class Service
            {
                private readonly EventStream _eventStream = new EventStream();

                public async Task Save()
                {
                    using (var scope = new System.Transactions.TransactionScope())
                    {
                        scope.Complete();
                    }

                    await _eventStream.Publish(new object());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantForNonNetworkSendMethod() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public class PayloadSender
            {
                public Task Send(object payload) => Task.CompletedTask;
            }

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly PayloadSender _sender;

                public Service(ITransactional transactional, PayloadSender sender)
                {
                    _transactional = transactional;
                    _sender = sender;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        await _sender.Send(order);
                        transaction.Commit();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantForFrameworkHttpClient() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            namespace System.Net.Http
            {
                public class HttpClient
                {
                    public Task<object> GetAsync(string url) => Task.FromResult((object)null);
                }
            }

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

                public Service(ITransactional transactional)
                {
                    _transactional = transactional;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        await _httpClient.GetAsync("/orders"); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                        transaction.Commit();
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_CompliantForCustomHttpClientType() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            namespace MyCompany.Http
            {
                public class HttpClient
                {
                    public Task<object> GetAsync(string url) => Task.FromResult((object)null);
                }
            }

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly MyCompany.Http.HttpClient _httpClient = new MyCompany.Http.HttpClient();

                public Service(ITransactional transactional)
                {
                    _transactional = transactional;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        await _httpClient.GetAsync("/orders");
                        transaction.Commit();
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseTransactionsShouldNotContainExternalNetworkCalls_NoncompliantForAnyJunoServiceBusMethod() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            namespace GP.Juno.EventStream
            {
                public interface EventStream
                {
                    Task Ping();
                }
            }

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction();
            }

            public interface ITransaction : System.IDisposable
            {
                Task Execute(object command);
                void Commit();
            }

            public class Service
            {
                private readonly ITransactional _transactional;
                private readonly GP.Juno.EventStream.EventStream _eventStream;

                public Service(ITransactional transactional, GP.Juno.EventStream.EventStream eventStream)
                {
                    _transactional = transactional;
                    _eventStream = eventStream;
                }

                public async Task Save(object order)
                {
                    using (var transaction = await _transactional.StartTransaction())
                    {
                        await transaction.Execute(order);
                        await _eventStream.Ping(); // Noncompliant {{Do not call external network resources inside a database transaction before commit.}}
                        transaction.Commit();
                    }
                }
            }
            """)
            .Verify();
}
