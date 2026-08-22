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
public class RowLimitedQueryShouldOrderDeterministicallyTest
{
    // A multi-line SQL literal is reported on the line it starts on, so the expectations below sit above the
    // declaration and point at it with @+1 rather than trailing the closing quote.
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.RowLimitedQueryShouldOrderDeterministically>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void RowLimitedQueryShouldOrderDeterministically_OffsetFetchPaging() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                // Noncompliant@+1 {{This query takes a subset of rows ordered only by 'CreatedDateUtc', so ties are broken arbitrarily. Add a unique column to the ORDER BY.}}
                public const string Paged = @"
            SELECT c.ExternalSystemId, c.ExternalCvId
            FROM dbo.ExtractionOrders c
            WHERE c.ExtractionVersion = @CurrentVersion
            ORDER BY c.CreatedDateUtc
            OFFSET @OffsetSize ROWS
            FETCH NEXT @PageSize ROWS ONLY;";

                public const string PagedWithTieBreaker = @"
            SELECT c.ExternalSystemId, c.ExternalCvId
            FROM dbo.ExtractionOrders c
            ORDER BY c.CreatedDateUtc, c.ExtractionOrderId
            OFFSET @OffsetSize ROWS
            FETCH NEXT @PageSize ROWS ONLY;";
            }
            """).Verify();

    [TestMethod]
    public void RowLimitedQueryShouldOrderDeterministically_TopWithRawStringLiteral() =>
        builder.AddSnippet(
            """"
            public class Queries
            {
                // Noncompliant@+1 {{This query takes a subset of rows ordered only by 'GeneratedAtUtc', so ties are broken arbitrarily. Add a unique column to the ORDER BY.}}
                private const string Sql = """
                                           SELECT TOP (1) [OfferId], [GeneratedAtUtc], [ExpiresAtUtc]
                                           FROM [dbo].[UserSpotOnOffers]
                                           WHERE [UserId] = @UserId
                                           ORDER BY [GeneratedAtUtc] DESC
                                           """;

                private const string Fixed = """
                                             SELECT TOP (1) [OfferId], [GeneratedAtUtc]
                                             FROM [dbo].[UserSpotOnOffers]
                                             ORDER BY [GeneratedAtUtc] DESC, [OfferId] DESC
                                             """;
            }
            """").Verify();

    [TestMethod]
    public void RowLimitedQueryShouldOrderDeterministically_MultipleTemporalColumns() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                // Noncompliant@+1 {{This query takes a subset of rows ordered only by 'OccurredAtUtc' and 'RecordedDateUtc', so ties are broken arbitrarily. Add a unique column to the ORDER BY.}}
                public const string BothTemporal = @"
            SELECT TOP 10 Id FROM dbo.Events
            ORDER BY OccurredAtUtc DESC, RecordedDateUtc DESC";
            }
            """).Verify();

    [TestMethod]
    public void RowLimitedQueryShouldOrderDeterministically_NoIssues() =>
        builder.AddSnippet(
            """
            public class Queries
            {
                // No row limiter: the whole result set comes back, so ties change nothing about which rows are returned.
                public const string NoLimiter = @"
            SELECT Id, CreatedAtUtc FROM dbo.Events ORDER BY CreatedAtUtc";

                // Ordered by something that is not a timestamp.
                public const string ById = @"
            SELECT TOP (1) Id FROM dbo.Events ORDER BY Id DESC";

                // A limiter with no ordering at all is a different problem, and not this rule's.
                public const string NoOrderBy = @"
            SELECT TOP (1) Id FROM dbo.Events WHERE UserId = @UserId";

                // Ordering by an expression: whether it is unique cannot be judged from the text.
                public const string OrderedByCase = @"
            SELECT TOP (1) Id FROM dbo.Events
            ORDER BY case when ExtractionVersion = @Version then 0 else 1 end";

                public const string NotSql = "ORDER BY CreatedAtUtc OFFSET";
            }
            """).VerifyNoIssues();
}
