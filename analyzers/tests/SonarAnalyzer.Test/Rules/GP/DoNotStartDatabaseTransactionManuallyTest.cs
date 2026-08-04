using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotStartDatabaseTransactionManuallyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotStartDatabaseTransactionManually>()
        .AddReferences(MetadataReferenceFacade.SystemData)
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        using System.Data;
        using System.Threading.Tasks;

        namespace GP.Juno.Abstractions.Ado
        {
            public interface ITransaction : System.IDisposable
            {
                void Commit();
            }

            public interface ITransactional
            {
                Task<ITransaction> StartTransaction(IsolationLevel isolationLevel);
            }
        }
        """;

    [TestMethod]
    public void DoNotStartDatabaseTransactionManually_NoncompliantForBeginTransaction() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository
            {
                public System.Data.IDbTransaction Start(System.Data.IDbConnection connection) =>
                    connection.BeginTransaction(); // Noncompliant {{Start the transaction with Juno's ITransactional instead of calling 'BeginTransaction' on the connection.}}
            }
            """)
            .Verify();

    // The ITransactional implementation is the type whose job is to produce the transaction Juno tracks.
    [TestMethod]
    public void DoNotStartDatabaseTransactionManually_CompliantInsideTransactionalImplementation() =>
        builder.AddSnippet(
            Stubs + """

            public class JunoTransactional : GP.Juno.Abstractions.Ado.ITransactional
            {
                private readonly System.Data.IDbConnection _connection;

                public Task<GP.Juno.Abstractions.Ado.ITransaction> StartTransaction(System.Data.IsolationLevel isolationLevel)
                {
                    var transaction = _connection.BeginTransaction(isolationLevel);
                    return null;
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DoNotStartDatabaseTransactionManually_CompliantForJunoTransactional() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderService
            {
                private readonly GP.Juno.Abstractions.Ado.ITransactional _transactional;

                public Task<GP.Juno.Abstractions.Ado.ITransaction> Start() =>
                    _transactional.StartTransaction(System.Data.IsolationLevel.ReadCommitted);
            }
            """)
            .VerifyNoIssues();
}
