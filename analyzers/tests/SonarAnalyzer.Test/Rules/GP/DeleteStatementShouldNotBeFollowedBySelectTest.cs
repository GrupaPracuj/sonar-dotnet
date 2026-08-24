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
public class DeleteStatementShouldNotBeFollowedBySelectTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DeleteStatementShouldNotBeFollowedBySelect>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void DeleteStatementShouldNotBeFollowedBySelect_ReportsDetachedSelect() =>
        builder.AddSnippet(
            """"
            public sealed class ClearCommonOffersInheritance
            {
                // Noncompliant@+1 {{Replace SELECT with FROM so the DELETE predicate belongs to the DELETE statement.}}
                private const string Sql = """
                    DELETE schOffers.tCommonOffersInheritance
                    SELECT DISTINCT coi.* FROM schOffers.tCommonOffersInheritance coi
                    INNER JOIN schOffers.tCommonOffers co
                      ON coi.sourceCommonOfferId = co.commonOfferID
                    WHERE co.companyID = @companyId
                    """;
            }
            """")
            .Verify();

    [TestMethod]
    public void DeleteStatementShouldNotBeFollowedBySelect_AllowsDeleteFormsWithPredicate() =>
        builder.AddSnippet(
            """"
            public sealed class PurgeQueries
            {
                private const string Joined = """
                    DELETE coi
                    FROM schOffers.tCommonOffersInheritance coi
                    INNER JOIN schOffers.tCommonOffers co ON coi.sourceCommonOfferId = co.commonOfferID
                    WHERE co.companyID = @companyId
                    """;

                private const string Standard = """
                    DELETE FROM schOffers.tCommonOffersInheritance
                    WHERE companyID IN (SELECT companyID FROM schOffers.tCommonOffers)
                    """;

                private const string Output = """
                    DELETE schOffers.tCommonOffersInheritance
                    OUTPUT deleted.Id
                    WHERE companyID = @companyId
                    """;
            }
            """")
            .VerifyNoIssues();

    [TestMethod]
    public void DeleteStatementShouldNotBeFollowedBySelect_AllowsExplicitSeparateStatementAndNonSqlText() =>
        builder.AddSnippet(
            """"
            public sealed class Texts
            {
                private const string DeliberateFullDelete = "DELETE dbo.Stage; SELECT * FROM dbo.Source";
                private const string Comment = "-- DELETE dbo.Items SELECT * FROM dbo.Source";
                private const string Quoted = "DELETE FROM dbo.Items WHERE Note = 'SELECT something'";
                private const string Prose = "Please DELETE this sentence before SELECT is discussed.";
                private const string Interpolated = $"DELETE dbo.{"Items"} SELECT * FROM dbo.Source";
            }
            """")
            .VerifyNoIssues();
}
