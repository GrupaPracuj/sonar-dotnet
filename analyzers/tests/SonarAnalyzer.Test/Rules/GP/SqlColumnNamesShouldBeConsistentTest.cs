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
public class SqlColumnNamesShouldBeConsistentTest
{
    // A multi-line SQL literal is reported on the line it starts on, so expectations sit above the declaration
    // and point at it with @+1 rather than trailing the closing quote.
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.SqlColumnNamesShouldBeConsistent>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void SqlColumnNamesShouldBeConsistent_ReadColumnNeverWritten() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                public const string Insert = @"
            INSERT INTO dbo.AllocatedProductsExpirations
            (AllocationId, Type, FundsJson, RequestId, AppName, RequestingUser, JwtClient, ExecutedAtUtc, WalletId) VALUES
            (@AllocationId, @Type, @FundsJson, @RequestId, @AppName, @RequestingUser, @JwtClient, @ExecutedAtUtc, @WalletId)";

                // Noncompliant@+2 {{Column 'ExpirationDateUtc' is read from 'ALLOCATEDPRODUCTSEXPIRATIONS' but never written to it anywhere in this project. Check it against the table's schema.}}
                // Noncompliant@+1 {{Column 'AppDane' is read from 'ALLOCATEDPRODUCTSEXPIRATIONS' but never written to it anywhere in this project. Check it against the table's schema.}}
                public const string Select = @"
            SELECT
                WalletId,
                AllocationId,
                Type,
                ExpirationDateUtc,
                FundsJson,
                RequestId,
                AppDane,
                JwtClient,
                RequestingUser
            FROM
                dbo.AllocatedProductsExpirations
            WHERE
                AllocationId = @AllocationId;";
            }
            """).Verify();

    [TestMethod]
    public void SqlColumnNamesShouldBeConsistent_MergeWritesCountAsWrites() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                public const string Merge = @"
            MERGE INTO dbo.Expirations AS target
            USING (VALUES (@WalletId, @AllocationId, @RequestId)) AS source (WalletId, AllocationId, RequestId)
            ON target.WalletId = source.WalletId
            WHEN NOT MATCHED THEN
                INSERT (AllocationId, RequestId, AppName, ExecutedAtUtc, WalletId)
                VALUES (@AllocationId, @RequestId, @AppName, @ExecutedAtUtc, @WalletId);";

                public const string Consistent = @"
            SELECT WalletId, AllocationId, RequestId, AppName, ExecutedAtUtc
            FROM dbo.Expirations
            WHERE AllocationId = @AllocationId;";

                // Noncompliant@+1 {{Column 'AppDane' is read from 'EXPIRATIONS' but never written to it anywhere in this project. Check it against the table's schema.}}
                public const string Typo = @"
            SELECT WalletId, AllocationId, RequestId, AppDane, ExecutedAtUtc
            FROM dbo.Expirations
            WHERE AllocationId = @AllocationId;";
            }
            """).Verify();

    [TestMethod]
    public void SqlColumnNamesShouldBeConsistent_NoIssues() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                public const string Insert = @"
            INSERT INTO dbo.Orders (OrderId, CustomerId, TotalAmount, PlacedAtUtc) VALUES
            (@OrderId, @CustomerId, @TotalAmount, @PlacedAtUtc)";

                // Database-generated columns are read but never written by application SQL.
                public const string WithAuditColumns = @"
            SELECT OrderId, CustomerId, TotalAmount, PlacedAtUtc, Id, RowVersion, RowCreatedAtUtc
            FROM dbo.Orders
            WHERE OrderId = @OrderId";

                // A join cannot attribute a column to one table, so the read side is not judged.
                public const string Joined = @"
            SELECT o.OrderId, c.UnknownColumn
            FROM dbo.Orders o
            INNER JOIN dbo.Customers c ON c.CustomerId = o.CustomerId";

                // SELECT * says nothing about column names.
                public const string Star = @"
            SELECT * FROM dbo.Orders WHERE OrderId = @OrderId";

                // An existence probe contributes no column names.
                public const string Probe = @"
            SELECT TOP 1 1 FROM dbo.Orders WHERE OrderId = @OrderId";
            }
            """).VerifyNoIssues();

    [TestMethod]
    public void SqlColumnNamesShouldBeConsistent_NotEnoughWrittenColumns() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                // Only two written columns: the INSERT is not a usable picture of the table, so the read is left alone.
                public const string Insert = @"
            INSERT INTO dbo.Orders (OrderId, CustomerId) VALUES (@OrderId, @CustomerId)";

                public const string Select = @"
            SELECT OrderId, CustomerId, TotalAmount, PlacedAtUtc
            FROM dbo.Orders
            WHERE OrderId = @OrderId";
            }
            """).VerifyNoIssues();

    [TestMethod]
    public void SqlColumnNamesShouldBeConsistent_MostOfSelectUnknown() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                public const string Insert = @"
            INSERT INTO dbo.Orders (OrderId, CustomerId, TotalAmount) VALUES (@OrderId, @CustomerId, @TotalAmount)";

                // Half or more of the list is unaccounted for: this reads a table it barely writes, not a typo.
                public const string Select = @"
            SELECT OrderId, CustomerId, TotalAmount, Currency, Channel, Status, Discount
            FROM dbo.Orders
            WHERE OrderId = @OrderId";
            }
            """).VerifyNoIssues();
}
