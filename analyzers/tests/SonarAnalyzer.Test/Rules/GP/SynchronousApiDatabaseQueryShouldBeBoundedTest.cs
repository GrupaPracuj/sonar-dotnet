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
public class SynchronousApiDatabaseQueryShouldBeBoundedTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.SynchronousApiDatabaseQueryShouldBeBounded>()
        .AddReferences(MetadataReferenceFacade.SystemData)
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        using System;
        using System.Collections.Generic;
        using System.Data;
        using System.Linq;
        using System.Threading.Tasks;
        using Dapper;
        using Microsoft.EntityFrameworkCore;

        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class HttpGetAttribute : Attribute { }
            public abstract class ControllerBase { }
        }

        namespace Dapper
        {
            public readonly struct CommandDefinition
            {
                public CommandDefinition(
                    string commandText,
                    object parameters = null,
                    IDbTransaction transaction = null,
                    int? commandTimeout = null,
                    CommandType? commandType = null) { }
            }

            public static class SqlMapper
            {
                public static Task<IEnumerable<T>> QueryAsync<T>(
                    this IDbConnection connection,
                    string sql,
                    object param = null,
                    CommandType? commandType = null) => null;
                public static Task<IEnumerable<T>> QueryAsync<T>(
                    this IDbConnection connection,
                    CommandDefinition command) => null;
                public static Task<T> QuerySingleAsync<T>(this IDbConnection connection, string sql) => null;
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public interface DbSet<TEntity> : IQueryable<TEntity> { }

            public static class EntityFrameworkQueryableExtensions
            {
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => null;
                public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
            }
        }

        public sealed class Item { public int Id { get; set; } }
        """;

    [TestMethod]
    public void SynchronousApiDatabaseQueryShouldBeBounded_ReportsConstantUnboundedDapperQuery() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Task<IEnumerable<Item>> Get(IDbConnection connection) =>
                    connection.QueryAsync<Item>("select Id from Items order by Id"); // Noncompliant {{Bound the database result set used by this synchronous API path.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void SynchronousApiDatabaseQueryShouldBeBounded_AcceptsDapperTopLimitAndFetch() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task Load(IDbConnection connection, int limit)
                {
                    await connection.QueryAsync<Item>("select top (@limit) Id from Items", new { limit });
                    await connection.QueryAsync<Item>("select Id from Items limit @limit", new { limit });
                    await connection.QueryAsync<Item>("select Id from Items order by Id offset 0 rows fetch next @limit rows only", new { limit });
                    await connection.QueryAsync<int>("select count(*) from Items");
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SynchronousApiDatabaseQueryShouldBeBounded_HandlesCommandDefinitionAndStoredProcedure() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task Load(IDbConnection connection)
                {
                    var unbounded = new Dapper.CommandDefinition("select Id from Items");
                    await connection.QueryAsync<Item>(unbounded); // Noncompliant

                    var bounded = new Dapper.CommandDefinition("select top 50 Id from Items");
                    await connection.QueryAsync<Item>(bounded);

                    await connection.QueryAsync<Item>(
                        "GetItems",
                        commandType: CommandType.StoredProcedure);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SynchronousApiDatabaseQueryShouldBeBounded_DynamicSqlAndSingleRowQueriesAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task Load(IDbConnection connection, string sql)
                {
                    await connection.QueryAsync<Item>(sql);
                    await connection.QuerySingleAsync<Item>("select Id from Items");
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SynchronousApiDatabaseQueryShouldBeBounded_ReportsDirectUnboundedEfMaterialization() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task Load(Microsoft.EntityFrameworkCore.DbSet<Item> items)
                {
                    await items.Where(x => x.Id > 0).AsNoTracking().ToListAsync(); // Noncompliant
                    _ = items.Where(x => x.Id > 0).ToList(); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void SynchronousApiDatabaseQueryShouldBeBounded_AcceptsEfTakeAndUnknownHelpers() =>
        builder.AddSnippet(
            Stubs + """

            public static class QueryExtensions
            {
                public static IQueryable<T> ApplyPage<T>(this IQueryable<T> query) => query.Take(10);
            }

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public async Task Load(Microsoft.EntityFrameworkCore.DbSet<Item> items)
                {
                    await items.Where(x => x.Id > 0).Take(100).ToListAsync();
                    await items.ApplyPage().ToListAsync();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void SynchronousApiDatabaseQueryShouldBeBounded_ReportsHelperReachedFromActionButNotWorker() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Task<IEnumerable<Item>> Get(IDbConnection connection) => Load(connection);

                private static Task<IEnumerable<Item>> Load(IDbConnection connection) =>
                    connection.QueryAsync<Item>("select Id from Items"); // Noncompliant
            }

            public class Worker
            {
                public Task<IEnumerable<Item>> Load(IDbConnection connection) =>
                    connection.QueryAsync<Item>("select Id from Items");
            }
            """)
            .Verify();
}
