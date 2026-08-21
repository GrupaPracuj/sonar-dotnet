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
public class DoNotCreateDatabaseConnectionTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotCreateDatabaseConnection>()
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
                    System.Data.IDbTransaction transaction = null) { }
            }

            public static class SqlMapper
            {
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
            }
        }
        """;

    [TestMethod]
    public void DoNotCreateDatabaseConnection_NoncompliantForSqlConnection() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                private readonly string _connectionString;

                public System.Data.Common.DbConnection Open() =>
                    new Microsoft.Data.SqlClient.SqlConnection(_connectionString); // Noncompliant {{Perform database access through Juno IDbExecute instead of using 'SqlConnection' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateDatabaseConnection_NoncompliantForProviderFactory() =>
        builder.AddSnippet(
            Stubs + """

            public class ProviderFactory : System.Data.Common.DbProviderFactory
            {
                public override System.Data.Common.DbConnection CreateConnection() => null;
            }

            public class OrderRepository
            {
                public System.Data.Common.DbConnection Open(ProviderFactory factory) =>
                    factory.CreateConnection(); // Noncompliant {{Perform database access through Juno IDbExecute instead of using 'ProviderFactory.CreateConnection' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateDatabaseConnection_CompliantForJunoFactory() =>
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
    public void DoNotCreateDatabaseConnection_NoncompliantWhenDbExecuteOmitsTransaction() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> Execute(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null) =>
                    Dapper.SqlMapper.Query<int>(connection, "SELECT 1"); // Noncompliant {{Pass the IDbExecute transaction to this Dapper operation.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateDatabaseConnection_CompliantWhenDbExecutePassesTransaction() =>
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
    public void DoNotCreateDatabaseConnection_CompliantForPositionalTransactionInExecuteAsync() =>
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
    public void DoNotCreateDatabaseConnection_NoncompliantForDifferentTransaction() =>
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
    public void DoNotCreateDatabaseConnection_ChecksInlineAndLocalCommandDefinitions() =>
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
    public void DoNotCreateDatabaseConnection_ChecksCapturedTransactionInsideLambda() =>
        builder.AddSnippet(
            Stubs + """

            public class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int>
            {
                public System.Collections.Generic.IEnumerable<int> Missing(
                    System.Data.IDbConnection connection,
                    System.Data.IDbTransaction dbTransaction = null)
                {
                    System.Func<System.Collections.Generic.IEnumerable<int>> query =
                        () => Dapper.SqlMapper.Query<int>(connection, "SELECT 1"); // Noncompliant
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
    public void DoNotCreateDatabaseConnection_DoesNotGuessInsideUnknownCommandDefinition() =>
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
    public void DoNotCreateDatabaseConnection_CompliantForProviderFactoryInsideJuno() =>
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
    public void DoNotCreateDatabaseConnection_NoncompliantForJunoSiblingNamespace() =>
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
                        factory.CreateConnection(); // Noncompliant {{Perform database access through Juno IDbExecute instead of using 'ProviderFactory.CreateConnection' directly.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateDatabaseConnection_NoncompliantForDapperOutsideDbExecute() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                public System.Collections.Generic.IEnumerable<int> Load(System.Data.IDbConnection connection) =>
                    Dapper.SqlMapper.Query<int>(connection, "SELECT 1"); // Noncompliant {{Perform database access through Juno IDbExecute instead of using 'Dapper.Query' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateDatabaseConnection_CompliantForDapperInsideDbExecute() =>
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
    public void DoNotCreateDatabaseConnection_NoncompliantForDapperInsideTransactionalService() =>
        builder.AddSnippet(
            Stubs + """

            public class TransactionalOrderService : GP.Juno.Abstractions.Ado.ITransactional
            {
                public System.Collections.Generic.IEnumerable<int> Load(System.Data.IDbConnection connection) =>
                    Dapper.SqlMapper.Query<int>(connection, "SELECT 1"); // Noncompliant {{Perform database access through Juno IDbExecute instead of using 'Dapper.Query' directly.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotCreateDatabaseConnection_CompliantForDapperTypeHandlerRegistration() =>
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
    public void DoNotCreateDatabaseConnection_CompliantForAdoOperationOnProvidedConnection() =>
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
