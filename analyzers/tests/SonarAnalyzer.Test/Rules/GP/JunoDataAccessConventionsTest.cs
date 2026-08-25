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
public class JunoDataAccessConventionsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.JunoDataAccessConventions>()
        .AddReferences(MetadataReferenceFacade.SystemData)
        .AddReferences(MetadataReferenceFacade.SystemComponentModelPrimitives)
        .WithOptions(LanguageOptions.CSharpLatest);

    // Derives from the real DbConnection, so the rule's "derives from DbConnection" test is exercised for real
    // rather than against a look-alike stub.
    private const string Stubs =
        """
        namespace Microsoft.Data.SqlClient
        {
            public class SqlConnection : System.Data.Common.DbConnection
            {
                public SqlConnection(string connectionString) { }

                public override string ConnectionString { get; set; }
                public override string Database => null;
                public override string DataSource => null;
                public override string ServerVersion => null;
                public override System.Data.ConnectionState State => System.Data.ConnectionState.Closed;

                public override void ChangeDatabase(string databaseName) { }
                public override void Close() { }
                public override void Open() { }

                protected override System.Data.Common.DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => null;
                protected override System.Data.Common.DbCommand CreateDbCommand() => null;
            }
        }

        namespace GP.Juno.Ado
        {
            public interface IAdoConnectionFactory
            {
                System.Data.Common.DbConnection CreateConnection();
            }
        }

        namespace GP.Juno.Abstractions.Ado
        {
            public interface IDbExecute<T> { }
            public interface ITransactional { }
        }

        namespace Dapper
        {
            public readonly struct CommandDefinition
            {
                public CommandDefinition(
                    string commandText,
                    object parameters = null,
                    System.Data.IDbTransaction transaction = null,
                    System.Threading.CancellationToken cancellationToken = default) { }
            }

            public static class SqlMapper
            {
                public sealed class GridReader { }

                public static void AddTypeHandler<T>(object handler) { }
                public static void AddTypeMap(System.Type type, System.Data.DbType dbType) { }
                public static void PurgeQueryCache() { }
                public static object AsTableValuedParameter(
                    this System.Data.DataTable table,
                    string typeName = null) => null;

                public static System.Collections.Generic.IEnumerable<T> Query<T>(
                    this System.Data.IDbConnection connection,
                    string sql,
                    object param = null,
                    System.Data.IDbTransaction transaction = null) => null;

                public static System.Collections.Generic.IEnumerable<T> Query<T>(
                    this System.Data.IDbConnection connection,
                    CommandDefinition command) => null;

                public static System.Threading.Tasks.Task<int> ExecuteAsync(
                    this System.Data.IDbConnection connection,
                    string sql,
                    object param = null,
                    System.Data.IDbTransaction transaction = null,
                    int? commandTimeout = null,
                    System.Data.CommandType? commandType = null) => null;

                public static System.Threading.Tasks.Task<int> ExecuteAsync(
                    this System.Data.IDbConnection connection,
                    CommandDefinition command) => null;

                public static System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<T>> QueryAsync<T>(
                    this System.Data.IDbConnection connection,
                    CommandDefinition command) => null;

                public static System.Threading.Tasks.Task<T> QuerySingleAsync<T>(
                    this System.Data.IDbConnection connection,
                    CommandDefinition command) => null;

                public static System.Threading.Tasks.Task<T> QuerySingleOrDefaultAsync<T>(
                    this System.Data.IDbConnection connection,
                    string sql,
                    object param = null,
                    System.Data.IDbTransaction transaction = null) => null;

                public static System.Threading.Tasks.Task<T> QuerySingleOrDefaultAsync<T>(
                    this System.Data.IDbConnection connection,
                    CommandDefinition command) => null;

                public static System.Threading.Tasks.Task<GridReader> QueryMultipleAsync(
                    this System.Data.IDbConnection connection,
                    CommandDefinition command) => null;

                public static System.Threading.Tasks.Task<T> ExecuteScalarAsync<T>(
                    this System.Data.IDbConnection connection,
                    CommandDefinition command) => null;
            }
        }
        """;

    [TestMethod]
    public void JunoDataAccessConventions_NoncompliantForSqlConnection() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                private readonly string _connectionString;

                public System.Data.Common.DbConnection Open() =>
                    new Microsoft.Data.SqlClient.SqlConnection(_connectionString); // Noncompliant {{Obtain the connection from Juno: express the work as an IDbExecute, or use Dapper on a connection created by IAdoConnectionFactory.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_NoncompliantForProviderFactory() =>
        builder.AddSnippet(
            Stubs + """

            public class ProviderFactory : System.Data.Common.DbProviderFactory
            {
                public override System.Data.Common.DbConnection CreateConnection() => null;
            }

            public class OrderRepository
            {
                public System.Data.Common.DbConnection Open(ProviderFactory factory) =>
                    factory.CreateConnection(); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForJunoFactory() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                private readonly GP.Juno.Ado.IAdoConnectionFactory _connectionFactory;

                public System.Data.Common.DbConnection Open() =>
                    _connectionFactory.CreateConnection();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_NoncompliantWhenDbExecuteOmitsTransaction() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> Execute(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null) =>
                    Dapper.SqlMapper.Query<int>(connection, "SELECT 1"); // Noncompliant {{Pass the active transaction to this Dapper operation.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantWhenDbExecutePassesTransaction() =>
        builder.AddSnippet(
            Stubs + """

            namespace App
            {
                using Dapper;

                public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
                {
                    public System.Collections.Generic.IEnumerable<int> Execute(
                        System.Data.IDbConnection connection,
                        System.Data.IDbTransaction dbTransaction = null) =>
                        connection.Query<int>("SELECT 1", transaction: dbTransaction);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForPositionalTransactionInExecuteAsync() =>
        builder.AddSnippet(
            Stubs + """

            public class SaveOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Threading.Tasks.Task<int> Execute(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null) =>
                    Dapper.SqlMapper.ExecuteAsync(connection, "UPDATE Orders SET Saved = 1", null, dbTransaction);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_NoncompliantForDifferentTransaction() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> Execute(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    System.Data.IDbTransaction differentTransaction = null;
                    return Dapper.SqlMapper.Query<int>(connection, "SELECT 1", transaction: differentTransaction); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_ChecksInlineAndLocalCommandDefinitions() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> MissingInline(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null) =>
                    Dapper.SqlMapper.Query<int>(connection, new Dapper.CommandDefinition("SELECT 1")); // Noncompliant

                public System.Collections.Generic.IEnumerable<int> CorrectInline(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null) =>
                    Dapper.SqlMapper.Query<int>(connection, new Dapper.CommandDefinition("SELECT 1", transaction: dbTransaction));

                public System.Collections.Generic.IEnumerable<int> MissingTargetTyped(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null) =>
                    Dapper.SqlMapper.Query<int>(connection, new("SELECT 1")); // Noncompliant

                public System.Collections.Generic.IEnumerable<int> MissingLocal(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    var command = new Dapper.CommandDefinition("SELECT 1");
                    return Dapper.SqlMapper.Query<int>(connection, command); // Noncompliant
                }

                public System.Collections.Generic.IEnumerable<int> CorrectLocal(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    var command = new Dapper.CommandDefinition("SELECT 1", transaction: dbTransaction);
                    return Dapper.SqlMapper.Query<int>(connection, command);
                }

                public System.Collections.Generic.IEnumerable<int> MissingTargetTypedLocal(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    Dapper.CommandDefinition command = new("SELECT 1");
                    return Dapper.SqlMapper.Query<int>(connection, command); // Noncompliant
                }

                public System.Collections.Generic.IEnumerable<int> WrongTransaction(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    System.Data.IDbTransaction differentTransaction = null;
                    var command = new Dapper.CommandDefinition("SELECT 1", transaction: differentTransaction);
                    return Dapper.SqlMapper.Query<int>(connection, command); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_ChecksCapturedTransactionInsideLambda() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> Missing(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    System.Func<System.Collections.Generic.IEnumerable<int>> query =
                        () => Dapper.SqlMapper.Query<int>(connection, "SELECT 1"); // Noncompliant {{Pass the active transaction to this Dapper operation.}}
                    return query();
                }

                public System.Collections.Generic.IEnumerable<int> Correct(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    System.Func<System.Collections.Generic.IEnumerable<int>> query =
                        () => Dapper.SqlMapper.Query<int>(connection, "SELECT 1", transaction: dbTransaction);
                    return query();
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_DoesNotGuessInsideUnknownCommandDefinition() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> Execute(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction,
                    Dapper.CommandDefinition command) =>
                    Dapper.SqlMapper.Query<int>(connection, command);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForProviderFactoryInsideJuno() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.Juno.Ado
            {
                public class ProviderFactory : System.Data.Common.DbProviderFactory
                {
                    public override System.Data.Common.DbConnection CreateConnection() => null;
                }

                public class ConnectionFactory
                {
                    public System.Data.Common.DbConnection Open(ProviderFactory factory) =>
                        factory.CreateConnection();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_NoncompliantForJunoSiblingNamespace() =>
        builder.AddSnippet(
            Stubs + """

            namespace GP.JunoConsumer
            {
                public class ProviderFactory : System.Data.Common.DbProviderFactory
                {
                    public override System.Data.Common.DbConnection CreateConnection() => null;
                }

                public class ConnectionFactory
                {
                    public System.Data.Common.DbConnection Open(ProviderFactory factory) =>
                        factory.CreateConnection(); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForProjectLocalExecuteContract() =>
        builder.AddSnippet(
            Stubs + """

            namespace Project.Core.Db
            {
                // Structurally the same contract as Juno's IDbExecute under a project-local name: a runner supplies
                // the connection and the ambient transaction, so where the connection came from is not this
                // method's business.
                public interface IExecute<T>
                {
                    System.Threading.Tasks.Task<T> Execute(
                        System.Data.IDbConnection dbConnection,
                        System.Data.IDbTransaction dbTransaction = null);
                }

                public class CurrentDatabaseTime : IExecute<System.DateTime>
                {
                    public System.Threading.Tasks.Task<System.DateTime> Execute(
                        System.Data.IDbConnection dbConnection,
                        System.Data.IDbTransaction dbTransaction = null) =>
                        Dapper.SqlMapper.QuerySingleOrDefaultAsync<System.DateTime>(
                            dbConnection, "SELECT GETUTCDATE()", transaction: dbTransaction);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_NoncompliantForProjectLocalExecuteOmittingTransaction() =>
        builder.AddSnippet(
            Stubs + """

            namespace Project.Core.Db
            {
                public interface IExecute<T>
                {
                    System.Threading.Tasks.Task<T> Execute(
                        System.Data.IDbConnection dbConnection,
                        System.Data.IDbTransaction dbTransaction = null);
                }

                public class SaveOrder : IExecute<int>
                {
                    public System.Threading.Tasks.Task<int> Execute(
                        System.Data.IDbConnection dbConnection,
                        System.Data.IDbTransaction dbTransaction = null) =>
                        Dapper.SqlMapper.ExecuteAsync(dbConnection, "UPDATE T SET X = 1"); // Noncompliant {{Pass the active transaction to this Dapper operation.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    // The method receives the connection, so GP0035 has nothing to say about its provenance, and it takes no
    // CancellationToken, so GP0130 has nothing it could ask for.
    public void JunoDataAccessConventions_CompliantForDapperOnReceivedConnectionWithoutToken() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                public System.Collections.Generic.IEnumerable<int> Load(System.Data.IDbConnection connection) =>
                    Dapper.SqlMapper.Query<int>(connection, "SELECT 1");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForDapperInsideDbExecute() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> Load(System.Data.IDbConnection connection) =>
                    Dapper.SqlMapper.Query<int>(connection, "SELECT 1");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForFactoryDapperMethods() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderRepository
            {
                private readonly GP.Juno.Ado.IAdoConnectionFactory connectionFactory;

                public OrderRepository(GP.Juno.Ado.IAdoConnectionFactory connectionFactory) =>
                    this.connectionFactory = connectionFactory;

                public async System.Threading.Tasks.Task Load(System.Threading.CancellationToken token)
                {
                    await using var connection = connectionFactory.CreateConnection();

                    _ = await Dapper.SqlMapper.QueryAsync<int>(connection, new Dapper.CommandDefinition("SELECT 1", cancellationToken: token));
                    _ = await Dapper.SqlMapper.QuerySingleAsync<int>(connection, new Dapper.CommandDefinition("SELECT 1", cancellationToken: token));
                    _ = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<int>(connection, new Dapper.CommandDefinition("SELECT 1", cancellationToken: token));
                    _ = await Dapper.SqlMapper.QueryMultipleAsync(connection, new Dapper.CommandDefinition("SELECT 1", cancellationToken: token));
                    _ = await Dapper.SqlMapper.ExecuteAsync(connection, new Dapper.CommandDefinition("UPDATE T SET X = 1", cancellationToken: token));
                    _ = await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection, new Dapper.CommandDefinition("SELECT 1", cancellationToken: token));
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForFactoryDapperWithTransaction() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderRepository
            {
                private readonly GP.Juno.Ado.IAdoConnectionFactory connectionFactory;

                public OrderRepository(GP.Juno.Ado.IAdoConnectionFactory connectionFactory) =>
                    this.connectionFactory = connectionFactory;

                public async System.Threading.Tasks.Task Save(System.Threading.CancellationToken token)
                {
                    await using var connection = connectionFactory.CreateConnection();
                    await connection.OpenAsync(token);
                    await using var transaction = await connection.BeginTransactionAsync(token);

                    var inline = await Dapper.SqlMapper.ExecuteAsync(connection,
                        new Dapper.CommandDefinition("UPDATE T SET X = 1", transaction: transaction, cancellationToken: token));

                    var command = new Dapper.CommandDefinition(
                        "SELECT 1", transaction: transaction, cancellationToken: token);
                    _ = await Dapper.SqlMapper.QuerySingleAsync<int>(connection, command);

                    Dapper.CommandDefinition targetTyped = new(
                        "SELECT 1", transaction: transaction, cancellationToken: token);
                    _ = await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection, targetTyped);

                    await transaction.CommitAsync(token);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_ReportsMissingTransactionOrCancellation() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderRepository
            {
                private readonly GP.Juno.Ado.IAdoConnectionFactory connectionFactory;

                public OrderRepository(GP.Juno.Ado.IAdoConnectionFactory connectionFactory) =>
                    this.connectionFactory = connectionFactory;

                public async System.Threading.Tasks.Task Save(System.Threading.CancellationToken token)
                {
                    await using var connection = connectionFactory.CreateConnection();
                    await connection.OpenAsync(token);
                    await using var transaction = await connection.BeginTransactionAsync(token);

                    _ = await Dapper.SqlMapper.ExecuteAsync(connection, // Noncompliant {{Pass the active transaction to this Dapper operation.}}
                        new Dapper.CommandDefinition("UPDATE T SET X = 1", cancellationToken: token));

                    await transaction.CommitAsync(token);

                    _ = await Dapper.SqlMapper.QuerySingleAsync<int>(connection, // Noncompliant {{Pass the CancellationToken through Dapper CommandDefinition.}}
                        new Dapper.CommandDefinition("SELECT 1"));
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_ReportsDifferentTransactionOrConnection() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderRepository
            {
                private readonly GP.Juno.Ado.IAdoConnectionFactory connectionFactory;

                public OrderRepository(GP.Juno.Ado.IAdoConnectionFactory connectionFactory) =>
                    this.connectionFactory = connectionFactory;

                public async System.Threading.Tasks.Task Save(
                    System.Data.IDbTransaction differentTransaction,
                    System.Threading.CancellationToken token)
                {
                    await using var connection = connectionFactory.CreateConnection();
                    await connection.OpenAsync(token);
                    await using var transaction = await connection.BeginTransactionAsync(token);

                    _ = await Dapper.SqlMapper.ExecuteAsync(connection, // Noncompliant
                        new Dapper.CommandDefinition(
                            "UPDATE T SET X = 1",
                            transaction: differentTransaction,
                            cancellationToken: token));

                    await using var otherConnection = connectionFactory.CreateConnection();
                    _ = await Dapper.SqlMapper.ExecuteAsync(otherConnection, // Noncompliant
                        new Dapper.CommandDefinition(
                            "UPDATE T SET X = 2",
                            transaction: transaction,
                            cancellationToken: token));
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForConnectionAndTransactionHelper() =>
        builder.AddSnippet(
            Stubs + """

            public static class OrderSql
            {
                public static System.Threading.Tasks.Task<int> Save(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction transaction,
                    System.Threading.CancellationToken token) =>
                    Dapper.SqlMapper.ExecuteAsync(connection,
                        new Dapper.CommandDefinition(
                            "UPDATE T SET X = 1",
                            transaction: transaction,
                            cancellationToken: token));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForConnectionAndCancellationHelper() =>
        builder.AddSnippet(
            Stubs + """

            public static class OrderSql
            {
                public static System.Threading.Tasks.Task<int> Load(
                    System.Data.IDbConnection connection,
                    System.Threading.CancellationToken token) =>
                    Dapper.SqlMapper.QuerySingleAsync<int>(
                        connection,
                        new Dapper.CommandDefinition("SELECT 1", cancellationToken: token));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_DoesNotDuplicateManualConnectionDiagnostic() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class OrderRepository
            {
                public async System.Threading.Tasks.Task<int> Save(
                    string connectionString,
                    System.Threading.CancellationToken token)
                {
                    await using var connection =
                        new Microsoft.Data.SqlClient.SqlConnection(connectionString); // Noncompliant

                    return await Dapper.SqlMapper.ExecuteAsync(connection,
                        new Dapper.CommandDefinition("UPDATE T SET X = 1", cancellationToken: token));
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForDapperOnReceivedConnectionInTransactionalService() =>
        builder.AddSnippet(
            Stubs + """

            public class TransactionalOrderService : GP.Juno.Abstractions.Ado.ITransactional
            {
                public System.Collections.Generic.IEnumerable<int> Load(System.Data.IDbConnection connection) =>
                    Dapper.SqlMapper.Query<int>(connection, "SELECT 1");
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForDapperTypeHandlerRegistration() =>
        builder.AddSnippet(
            Stubs + """

            public static class Setup
            {
                public static void AddDapperHandlers() =>
                    Dapper.SqlMapper.AddTypeHandler<int>(new object());

                public static void AddDapperMappings() =>
                    Dapper.SqlMapper.AddTypeMap(typeof(int), System.Data.DbType.Int32);

                public static void ClearDapperCache() =>
                    Dapper.SqlMapper.PurgeQueryCache();

                public static object CreateTableParameter(System.Data.DataTable table) =>
                    Dapper.SqlMapper.AsTableValuedParameter(table);
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void JunoDataAccessConventions_CompliantForAdoOperationOnProvidedConnection() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                public int Count(System.Data.IDbConnection connection) =>
                    connection.CreateCommand().ExecuteNonQuery();
            }
            """)
            .VerifyNoIssues();
}
