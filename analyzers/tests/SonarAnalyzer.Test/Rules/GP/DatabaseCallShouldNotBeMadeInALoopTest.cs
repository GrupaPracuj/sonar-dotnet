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
public class DatabaseCallShouldNotBeMadeInALoopTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DatabaseCallShouldNotBeMadeInALoop>()
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
            public sealed class HttpPostAttribute : Attribute { }
            public abstract class ControllerBase { }
        }

        namespace Dapper
        {
            public static class SqlMapper
            {
                public static Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection connection, string sql, object param = null) => null;
                public static Task<T> QuerySingleAsync<T>(this IDbConnection connection, string sql, object param = null) => null;
                public static Task<int> ExecuteAsync(this IDbConnection connection, string sql, object param = null) => null;
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public interface DbSet<TEntity> : IQueryable<TEntity> { }

            public static class EntityFrameworkQueryableExtensions
            {
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => null;
            }
        }

        namespace GP.Juno.Abstractions.Ado
        {
            public interface IDbExecute<T> { }
            public interface IDbTransaction
            {
                Task<T> Execute<T>(IDbExecute<T> operation);
            }
        }

        public sealed class Item { public int Id { get; set; } }
        public sealed class LoadItem : GP.Juno.Abstractions.Ado.IDbExecute<Item>
        {
            public LoadItem(int id) { }
        }
        """;

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_ReportsDapperQueryAndExecuteFromAction() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public async Task Load(IDbConnection connection, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await connection.QuerySingleAsync<Item>("select * from Items where Id = @id", new { id }); // Noncompliant
                        await connection.ExecuteAsync("delete from Items where Id = @id", new { id }); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_ReportsEfAndJunoOperationsFromAction() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public async Task Load(
                    Microsoft.EntityFrameworkCore.DbSet<Item> items,
                    GP.Juno.Abstractions.Ado.IDbTransaction transaction,
                    IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await items.Where(x => x.Id == id).ToListAsync(); // Noncompliant
                        _ = items.Where(x => x.Id == id).ToList(); // Noncompliant
                        await transaction.Execute(new LoadItem(id)); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_ReportsHelperReachedFromAction() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Task Load(IDbConnection connection, IEnumerable<int> ids) =>
                    LoadCore(connection, ids);

                private static async Task LoadCore(IDbConnection connection, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await connection.QuerySingleAsync<Item>("select * from Items where Id = @id", new { id }); // Noncompliant
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_ReportsInterfaceImplementationReachedFromAction() =>
        builder.AddSnippet(
            Stubs + """

            public interface IItemLoader
            {
                Task Load(IDbConnection connection, IEnumerable<int> ids);
            }

            public sealed class ItemLoader : IItemLoader
            {
                public async Task Load(IDbConnection connection, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await connection.QuerySingleAsync<Item>("select * from Items where Id = @id", new { id }); // Noncompliant
                    }
                }
            }

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Task Load(IItemLoader loader, IDbConnection connection, IEnumerable<int> ids) =>
                    loader.Load(connection, ids);
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_NonApiAndRetryLoopsAreIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public class Worker
            {
                public async Task Load(IDbConnection connection, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        await connection.QuerySingleAsync<Item>("select * from Items where Id = @id", new { id });
                    }
                }
            }

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public async Task Load(IDbConnection connection, int attempts)
                {
                    for (var attempt = 0; attempt < attempts; attempt++)
                    {
                        await connection.QueryAsync<Item>("select top 10 * from Items");
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseCallShouldNotBeMadeInALoop_IndirectLoopDependencyIsIgnored() =>
        builder.AddSnippet(
            Stubs + """

            public class ItemsController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpPost]
                public async Task Load(IDbConnection connection, IEnumerable<int> ids)
                {
                    foreach (var id in ids)
                    {
                        var parameters = new { id };
                        await connection.QuerySingleAsync<Item>("select * from Items where Id = @id", parameters);
                    }
                }
            }
            """)
            .VerifyNoIssues();
}
