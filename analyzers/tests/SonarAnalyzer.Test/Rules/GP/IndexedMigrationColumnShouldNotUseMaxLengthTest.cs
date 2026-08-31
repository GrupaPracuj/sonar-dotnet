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
public class IndexedMigrationColumnShouldNotUseMaxLengthTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.IndexedMigrationColumnShouldNotUseMaxLength>()
        .WithOptions(LanguageOptions.CSharpLatest)
        .WithConcurrentAnalysis(false);

    private const string Stubs =
        """
        namespace FluentMigrator
        {
            public abstract class Migration
            {
                protected CreateRoot Create => null;
                protected AlterRoot Alter => null;
                public abstract void Up();
                public abstract void Down();
            }

            public interface CreateRoot
            {
                ColumnBuilder Table(string name);
                IndexBuilder Index(string name);
                IndexBuilder PrimaryKey(string name);
            }

            public interface AlterRoot
            {
                ColumnBuilder Table(string name);
            }

            public interface ColumnBuilder
            {
                ColumnBuilder WithColumn(string name);
                ColumnBuilder AddColumn(string name);
                ColumnBuilder AlterColumn(string name);
                ColumnBuilder AsString(int length);
                ColumnBuilder PrimaryKey();
                ColumnBuilder Indexed();
                ColumnBuilder InSchema(string schema);
            }

            public interface IndexBuilder
            {
                IndexBuilder OnTable(string table);
                IndexBuilder OnColumn(string column);
                IndexBuilder Column(string column);
                IndexBuilder Columns(params string[] columns);
                IndexBuilder InSchema(string schema);
            }
        }
        """;

    [TestMethod]
    public void IndexedMigrationColumnShouldNotUseMaxLength_ReportsSeparateIndexAndInlinePrimaryKey() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class Migration0010 : FluentMigrator.Migration
            {
                public override void Up()
                {
                    Create.Table("Sessions")
                        .WithColumn("WalletId")
                        .AsString(int.MaxValue); // Noncompliant {{Give indexed column 'WalletId' a bounded string length; SQL Server cannot index NVARCHAR(MAX).}}
                    Create.Index("IX_Sessions_WalletId")
                        .OnTable("Sessions")
                        .OnColumn("WalletId");

                    Create.Table("Tokens")
                        .WithColumn("TokenId")
                        .AsString(-1) // Noncompliant {{Give indexed column 'TokenId' a bounded string length; SQL Server cannot index NVARCHAR(MAX).}}
                        .PrimaryKey();
                }

                public override void Down() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void IndexedMigrationColumnShouldNotUseMaxLength_AllowsUnindexedAndBoundedColumns() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class Migration0010 : FluentMigrator.Migration
            {
                public override void Up()
                {
                    Create.Table("Sessions")
                        .WithColumn("Payload")
                        .AsString(int.MaxValue);
                    Create.Table("Sessions")
                        .WithColumn("WalletId")
                        .AsString(256);
                    Create.Index("IX_Sessions_WalletId")
                        .OnTable("Sessions")
                        .OnColumn("WalletId");
                }

                public override void Down() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void IndexedMigrationColumnShouldNotUseMaxLength_DoesNotMatchDifferentTableOrSchema() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class Migration0010 : FluentMigrator.Migration
            {
                public override void Up()
                {
                    Create.Table("Sessions")
                        .InSchema("archive")
                        .WithColumn("WalletId")
                        .AsString(int.MaxValue);
                    Create.Index("IX_CurrentSessions_WalletId")
                        .OnTable("CurrentSessions")
                        .InSchema("archive")
                        .OnColumn("WalletId");
                    Create.Index("IX_Sessions_WalletId")
                        .OnTable("Sessions")
                        .InSchema("dbo")
                        .OnColumn("WalletId");
                }

                public override void Down() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void IndexedMigrationColumnShouldNotUseMaxLength_CorrelatesAcrossMigrationHistory() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class Migration0010 : FluentMigrator.Migration
            {
                public override void Up() =>
                    Create.Table("Sessions").WithColumn("WalletId").AsString(int.MaxValue); // Noncompliant

                public override void Down() { }
            }

            public sealed class Migration0020 : FluentMigrator.Migration
            {
                public override void Up() =>
                    Create.Index("IX_Sessions_WalletId").OnTable("Sessions").OnColumn("WalletId");

                public override void Down() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void IndexedMigrationColumnShouldNotUseMaxLength_ReportsInlineIndexedAndAddedColumns() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class Migration0010 : FluentMigrator.Migration
            {
                public override void Up()
                {
                    Create.Table("Sessions")
                        .WithColumn("WalletId")
                        .AsString(int.MaxValue) // Noncompliant {{Give indexed column 'WalletId' a bounded string length; SQL Server cannot index NVARCHAR(MAX).}}
                        .Indexed();

                    Alter.Table("Tokens")
                        .AddColumn("Tenant")
                        .AsString(int.MaxValue); // Noncompliant {{Give indexed column 'Tenant' a bounded string length; SQL Server cannot index NVARCHAR(MAX).}}
                    Create.Index("IX_Tokens_Tenant")
                        .OnTable("Tokens")
                        .OnColumn("Tenant");
                }

                public override void Down() { }
            }
            """)
            .Verify();

    [TestMethod]
    public void IndexedMigrationColumnShouldNotUseMaxLength_AllowsColumnBoundedByAnotherMigration() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class Migration0010 : FluentMigrator.Migration
            {
                public override void Up() =>
                    Create.Table("Sessions").WithColumn("WalletId").AsString(int.MaxValue);

                public override void Down() { }
            }

            public sealed class Migration0020 : FluentMigrator.Migration
            {
                public override void Up() =>
                    Alter.Table("Sessions").AlterColumn("WalletId").AsString(64);

                public override void Down() { }
            }

            public sealed class Migration0030 : FluentMigrator.Migration
            {
                public override void Up() =>
                    Create.Index("IX_Sessions_WalletId").OnTable("Sessions").OnColumn("WalletId");

                public override void Down() { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void IndexedMigrationColumnShouldNotUseMaxLength_IgnoresLookalikeApi() =>
        builder.AddSnippet(
            """
            public sealed class Builder
            {
                public Builder Table(string name) => this;
                public Builder WithColumn(string name) => this;
                public Builder AsString(int length) => this;
                public Builder PrimaryKey() => this;
            }

            public sealed class Migration0010
            {
                private readonly Builder create = new Builder();

                public void Up() =>
                    create.Table("Tokens").WithColumn("TokenId").AsString(int.MaxValue).PrimaryKey();
            }
            """)
            .VerifyNoIssues();
}
