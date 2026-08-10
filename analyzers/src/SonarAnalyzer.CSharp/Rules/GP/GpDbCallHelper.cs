namespace SonarAnalyzer.CSharp.Rules;

// Kept to the concrete ADO.NET/EF surface on purpose: raw IDbConnection/IDbCommand/IDbTransaction execution (which also covers Dapper, since its
// Query/Execute methods are extension methods on those same interfaces) and DbContext.SaveChanges/SaveChangesAsync. A generic "any LINQ terminal
// operator on an IQueryable<TEntity>" check was deliberately left out - it would also catch the far more common and legitimate case of materializing
// an already-filtered, small query, so it would cost more false positives than it is worth.
internal static class GpDbCallHelper
{
    private static readonly HashSet<string> AdoExecuteMethods = new(StringComparer.Ordinal)
    {
        "Execute", "ExecuteAsync",
        "ExecuteReader", "ExecuteReaderAsync",
        "ExecuteScalar", "ExecuteScalarAsync",
        "ExecuteNonQuery", "ExecuteNonQueryAsync",
        // Dapper - extension methods on IDbConnection/IDbTransaction.
        "Query", "QueryAsync",
        "QueryFirst", "QueryFirstAsync",
        "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync",
        "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "QueryMultiple", "QueryMultipleAsync"
    };

    private static readonly HashSet<string> SaveChangesMethods = new(StringComparer.Ordinal) { "SaveChanges", "SaveChangesAsync" };

    internal static bool IsDbCall(IMethodSymbol method) =>
        IsAdoOrDapperExecute(method) || IsEntityFrameworkSaveChanges(method);

    private static bool IsAdoOrDapperExecute(IMethodSymbol method)
    {
        if (!AdoExecuteMethods.Contains(method.Name))
        {
            return false;
        }

        if (IsDbAccessType(method.ContainingType))
        {
            return true;
        }

        // For an extension method called via instance syntax (the common case for Dapper), the symbol from GetSymbolInfo is already reduced:
        // Parameters excludes the receiver, so it must be read from ReceiverType. Parameters[0] only holds the receiver when the method is
        // referenced in its unreduced/static form.
        return method.IsExtensionMethod
               && (IsDbAccessType(method.ReceiverType) || (method.Parameters.Length > 0 && IsDbAccessType(method.Parameters[0].Type)));
    }

    private static bool IsDbAccessType(ITypeSymbol type) =>
        type is not null
        && (GpJunoTypes.Implements(type, "System.Data.IDbConnection")
            || GpJunoTypes.Implements(type, "System.Data.IDbCommand")
            || GpJunoTypes.Implements(type, "System.Data.IDbTransaction"));

    private static bool IsEntityFrameworkSaveChanges(IMethodSymbol method) =>
        SaveChangesMethods.Contains(method.Name)
        && (GpJunoTypes.DerivesFrom(method.ContainingType, "Microsoft.EntityFrameworkCore.DbContext")
            || GpJunoTypes.DerivesFrom(method.ContainingType, "System.Data.Entity.DbContext"));
}
