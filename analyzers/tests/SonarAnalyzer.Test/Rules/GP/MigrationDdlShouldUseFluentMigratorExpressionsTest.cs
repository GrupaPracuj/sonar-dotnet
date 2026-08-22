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
public class MigrationDdlShouldUseFluentMigratorExpressionsTest
{
    // These snippets declare FluentMigrator stubs, which the concurrency wrapper would move to another namespace.
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.MigrationDdlShouldUseFluentMigratorExpressions>()
        .WithOptions(LanguageOptions.CSharpLatest)
        .WithConcurrentAnalysis(false);

    [TestMethod]
    public void MigrationDdlShouldUseFluentMigratorExpressions_ReportsTableAndIndexDdl() =>
        builder.AddSnippet(
            """"
            using FluentMigrator;

            public sealed class Migration0010 : Migration
            {
                public override void Up()
                {
                    // Noncompliant@+1 {{Replace raw CREATE TABLE or CREATE INDEX DDL with FluentMigrator expressions.}}
                    Execute.Sql("""
                                CREATE TABLE [dbo].[BookEditions]
                                (
                                    [Isbn] nvarchar(20) NOT NULL
                                );
                                """);

                    // Noncompliant@+1
                    Execute.Sql(sql: """
                                     create unique nonclustered index [UX_BookLoans_BookItemId_Active]
                                         on [dbo].[BookLoans] ([BookItemId]);
                                     """);

                    const string CreateIndex = "CREATE /* formatting */ INDEX IX_BookItems_Isbn ON dbo.BookItems (Isbn)";
                    Execute.Sql(CreateIndex); // Noncompliant
                }

                public override void Down() { }
            }

            namespace FluentMigrator
            {
                using FluentMigrator.Builders.Execute;

                public abstract class Migration
                {
                    protected IExecuteExpressionRoot Execute => null;
                    public abstract void Up();
                    public abstract void Down();
                }
            }

            namespace FluentMigrator.Builders.Execute
            {
                public interface IExecuteExpressionRoot
                {
                    void Sql(string sql);
                }
            }
            """").Verify();

    [TestMethod]
    public void MigrationDdlShouldUseFluentMigratorExpressions_ReportsInterpolatedDdl() =>
        builder.AddSnippet(
            """""
            using FluentMigrator;

            public sealed class Migration0010 : Migration
            {
                public override void Up()
                {
                    var table = "BookItems";
                    // Noncompliant@+1
                    Execute.Sql($"""
                                 CREATE TABLE [dbo].[{table}] ([Id] uniqueidentifier NOT NULL);
                                 """);
                }

                public override void Down() { }
            }

            namespace FluentMigrator
            {
                using FluentMigrator.Builders.Execute;

                public abstract class Migration
                {
                    protected IExecuteExpressionRoot Execute => null;
                    public abstract void Up();
                    public abstract void Down();
                }
            }

            namespace FluentMigrator.Builders.Execute
            {
                public interface IExecuteExpressionRoot
                {
                    void Sql(string sql);
                }
            }
            """"").Verify();

    [TestMethod]
    public void MigrationDdlShouldUseFluentMigratorExpressions_AllowsUnsupportedAndUncertainShapes() =>
        builder.AddSnippet(
            """"
            using FluentMigrator;
            using FluentMigrator.Builders.Execute;

            public sealed class Migration0010 : Migration
            {
                public override void Up()
                {
                    Execute.Sql("ALTER TABLE dbo.BookEditions ADD CONSTRAINT CK_Json CHECK (ISJSON(AuthorsJson) = 1)");
                    Execute.Sql("CREATE SCHEMA archive");
                    Execute.Sql("CREATE VIEW dbo.ActiveLoans AS SELECT Id FROM dbo.BookLoans");
                    Execute.Sql("UPDATE dbo.BookItems SET Isbn = @Isbn");
                    Execute.Sql("INSERT INTO dbo.BookItems (Id) VALUES (@Id)");
                    Execute.Sql("EXEC dbo.RebuildCatalog");
                    Execute.Sql("-- CREATE TABLE dbo.CommentedOut (Id int)");
                    Execute.Sql("SELECT 'CREATE INDEX IX_NotDdl ON dbo.Items(Id)'");
                    Execute.Sql("CREATE TABLE #ImportedIds (Id int)");
                    Execute.Sql("""
                                CREATE PROCEDURE dbo.RefreshCatalog
                                AS
                                CREATE TABLE #Work (Id int);
                                """);
                    Execute.Sql("SET ANSI_NULLS ON; CREATE TABLE dbo.GeneratedScript (Id int)");

                    var statementKind = "TABLE";
                    Execute.Sql($"CREATE {statementKind} dbo.Dynamic (Id int)");

                    var dynamicSql = GetSql();
                    Execute.Sql(dynamicSql);
                }

                public override void Down() { }

                private static string GetSql() => "CREATE TABLE dbo.Dynamic (Id int)";
            }

            public sealed class NotAMigration
            {
                public void Run(IExecuteExpressionRoot execute) =>
                    execute.Sql("CREATE TABLE dbo.External (Id int)");
            }

            public sealed class LookalikeMigration : Migration
            {
                private readonly OtherExecutor other = new OtherExecutor();

                public override void Up() =>
                    other.Sql("CREATE TABLE dbo.Lookalike (Id int)");

                public override void Down() { }
            }

            public sealed class OtherExecutor
            {
                public void Sql(string sql) { }
            }

            namespace FluentMigrator
            {
                using FluentMigrator.Builders.Execute;

                public abstract class Migration
                {
                    protected IExecuteExpressionRoot Execute => null;
                    public abstract void Up();
                    public abstract void Down();
                }
            }

            namespace FluentMigrator.Builders.Execute
            {
                public interface IExecuteExpressionRoot
                {
                    void Sql(string sql);
                }
            }
            """").VerifyNoIssues();
}
