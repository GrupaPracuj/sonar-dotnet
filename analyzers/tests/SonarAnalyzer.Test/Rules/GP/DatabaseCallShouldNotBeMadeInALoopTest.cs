using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DatabaseCallShouldNotBeMadeInALoopTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DatabaseCallShouldNotBeMadeInALoop>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        using System.Collections.Generic;
        using System.Data;
        using System.Threading.Tasks;
        using Dapper;

        namespace System.Data
        {
            public interface IDbConnection { }

            public interface IDbCommand
            {
                int ExecuteNonQuery();
            }
        }

        // Dapper's real Query/Execute methods are extension methods on IDbConnection - mirrored here to exercise that path.
        namespace Dapper
        {
            public static class SqlMapper
            {
                public static Task<int> ExecuteAsync(this IDbConnection connection, string sql, object param = null) => null;
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public int SaveChanges() => 0;
                public Task<int> SaveChangesAsync() => null;
            }
        }

        public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext { }

        """;

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_NoncompliantForDapperExecuteAsyncInForEach() =>
        builder.AddSnippet(
            Stubs + """
            public class Repository
            {
                public async Task UpdateAll(IDbConnection connection, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await connection.ExecuteAsync("UPDATE Items SET Touched = 1 WHERE Id = @id", new { id }); // Noncompliant {{This database call runs once per loop iteration - batch the calls or move it outside the loop.}}
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_NoncompliantForRawAdoExecuteInFor() =>
        builder.AddSnippet(
            Stubs + """
            public class Repository
            {
                public void UpdateAll(IDbCommand command, int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        command.ExecuteNonQuery(); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_NoncompliantForSaveChangesInWhile() =>
        builder.AddSnippet(
            Stubs + """
            public class Repository
            {
                public void UpdateAll(ShopDbContext context, IEnumerator<int> ids)
                {
                    while (ids.MoveNext())
                    {
                        context.SaveChanges(); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_CompliantSaveChangesOutsideLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Repository
            {
                public void UpdateAll(ShopDbContext context, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        Track(id);
                    }
                    context.SaveChanges();
                }

                private static void Track(int id) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_CompliantWhenCallIsInsideLocalFunctionInvokedFromLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Repository
            {
                public async Task UpdateAll(IDbConnection connection, IEnumerable<int> ids)
                {
                    Task Update(int id) => connection.ExecuteAsync("UPDATE Items SET Touched = 1 WHERE Id = @id", new { id });

                    foreach (var id in ids)
                    {
                        await Update(id);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // EF LINQ query execution (context.Set<T>().FirstOrDefault(), ToList(), ...) is out of scope for this rule on
    // purpose - see the rule description's Exceptions section - so a lookalike method name that isn't one of the
    // recognized ADO/Dapper/SaveChanges members is never flagged, regardless of where it is declared.
    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_CompliantForLinqLookalikeInsideLoop() =>
        builder.AddSnippet(
            Stubs + """
            public class Orders
            {
                public object FirstOrDefault(int id) => null;
            }

            public class Repository
            {
                public void Process(Orders orders, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        orders.FirstOrDefault(id);
                    }
                }
            }
            """)
            .VerifyNoIssues();
}
