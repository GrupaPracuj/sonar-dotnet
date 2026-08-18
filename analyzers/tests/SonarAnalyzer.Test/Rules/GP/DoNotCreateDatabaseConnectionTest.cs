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
        """;

    [TestMethod]
    public void DoNotCreateDatabaseConnection_NoncompliantForSqlConnection() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                private readonly string _connectionString;

                public System.Data.Common.DbConnection Open() =>
                    new Microsoft.Data.SqlClient.SqlConnection(_connectionString); // Noncompliant {{Obtain the connection from Juno (IAdoConnectionFactory / IDbExecute) instead of creating it directly with 'SqlConnection'.}}
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
                    factory.CreateConnection(); // Noncompliant {{Obtain the connection from Juno (IAdoConnectionFactory / IDbExecute) instead of creating it directly with 'ProviderFactory.CreateConnection'.}}
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
                        factory.CreateConnection(); // Noncompliant {{Obtain the connection from Juno (IAdoConnectionFactory / IDbExecute) instead of creating it directly with 'ProviderFactory.CreateConnection'.}}
                }
            }
            """)
            .Verify();

    // Dapper on a connection Juno handed out is the sanctioned pattern, so nothing about using a connection is
    // reported - only producing one outside Juno.
    [TestMethod]
    public void DoNotCreateDatabaseConnection_CompliantForQueryingAProvidedConnection() =>
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
