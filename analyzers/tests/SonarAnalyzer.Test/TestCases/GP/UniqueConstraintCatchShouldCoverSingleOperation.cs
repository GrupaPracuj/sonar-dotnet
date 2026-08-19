using GP.Juno.Abstractions.Ado;
using GP.Juno.Ado;

namespace Microsoft.Data.SqlClient
{
    public class SqlException : System.Exception
    {
        public int Number { get; }
    }
}

namespace GP.Juno.Abstractions.Ado
{
    public interface IDbExecute<T> { }
    public interface ITransactional { }

    public static class TransactionExtensions
    {
        public static System.Threading.Tasks.Task<T> Execute<T>(
            this System.Data.IDbTransaction transaction,
            IDbExecute<T> command) => null;
    }
}

namespace GP.Juno.Ado
{
    public static class TransactionalExtensions
    {
        public static System.Threading.Tasks.Task RunInTransaction(
            this GP.Juno.Abstractions.Ado.ITransactional transactional,
            System.Func<System.Data.IDbTransaction, System.Threading.Tasks.Task> callback) => null;
    }
}

public static class SqlServerErrors
{
    public static bool IsUniqueConstraintViolation(Microsoft.Data.SqlClient.SqlException exception) => true;
    public static bool IsRetryable(Microsoft.Data.SqlClient.SqlException exception, int errorNumber) => true;
}

public sealed class InsertBookOperation : GP.Juno.Abstractions.Ado.IDbExecute<int> { }
public sealed class InsertAuthorsOperation : GP.Juno.Abstractions.Ado.IDbExecute<int> { }

public sealed class CatalogRepository(GP.Juno.Abstractions.Ado.ITransactional transactional)
{
    public async System.Threading.Tasks.Task BroadCatch()
    {
        try
        {
            await GP.Juno.Ado.TransactionalExtensions.RunInTransaction(transactional, async transaction =>
            {
                await GP.Juno.Abstractions.Ado.TransactionExtensions.Execute(
                    transaction,
                    new InsertBookOperation());
                await transaction.Execute(new InsertAuthorsOperation());
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 2601 or 2627) // Noncompliant {{Catch this unique-constraint violation around one database operation; this try scope executes 2.}}
        {
            throw;
        }
    }

    public async System.Threading.Tasks.Task BroadCatchUsingHelper()
    {
        try
        {
            await transactional.RunInTransaction(async transaction =>
            {
                await transaction.Execute(new InsertBookOperation());
                await transaction.Execute(new InsertAuthorsOperation());
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (SqlServerErrors.IsUniqueConstraintViolation(exception)) // Noncompliant
        {
            throw;
        }
    }

    public async System.Threading.Tasks.Task SingleOperation(System.Data.IDbTransaction transaction)
    {
        try
        {
            await transaction.Execute(new InsertBookOperation());
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 2627)
        {
            throw;
        }
    }

    public async System.Threading.Tasks.Task DifferentSqlError(System.Data.IDbTransaction transaction)
    {
        try
        {
            await transaction.Execute(new InsertBookOperation());
            await transaction.Execute(new InsertAuthorsOperation());
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 1205)
        {
            throw;
        }
    }

    public async System.Threading.Tasks.Task NestedFunctionIsNotExecuted(System.Data.IDbTransaction transaction)
    {
        try
        {
            async System.Threading.Tasks.Task Later()
            {
                await transaction.Execute(new InsertBookOperation());
                await transaction.Execute(new InsertAuthorsOperation());
            }

            await transaction.Execute(new InsertBookOperation());
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 2601)
        {
            throw;
        }
    }

    public async System.Threading.Tasks.Task UnrelatedConstantInFilter(System.Data.IDbTransaction transaction)
    {
        try
        {
            await transaction.Execute(new InsertBookOperation());
            await transaction.Execute(new InsertAuthorsOperation());
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (
            exception.Number == 1205 || SqlServerErrors.IsRetryable(exception, 2601))
        {
            throw;
        }
    }

    public async System.Threading.Tasks.Task UnrelatedExecuteApi(System.Data.IDbTransaction transaction)
    {
        try
        {
            await Own.TransactionExtensions.Execute(transaction, new InsertBookOperation());
            await Own.TransactionExtensions.Execute(transaction, new InsertAuthorsOperation());
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 2601)
        {
            throw;
        }
    }
}

namespace Own
{
    public static class TransactionExtensions
    {
        public static System.Threading.Tasks.Task<int> Execute(
            this System.Data.IDbTransaction transaction,
            object command) => null;
    }
}
