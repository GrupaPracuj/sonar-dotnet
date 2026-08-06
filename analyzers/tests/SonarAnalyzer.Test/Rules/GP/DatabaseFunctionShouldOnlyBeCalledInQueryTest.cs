using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DatabaseFunctionShouldOnlyBeCalledInQueryTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DatabaseFunctionShouldOnlyBeCalledInQuery>()
        .WithOptions(LanguageOptions.CSharpLatest);

    // 'using' directives have to come before any type declaration in the snippet, so they are kept separate from
    // Stubs (which declares the types) rather than appended after it.
    private const string Usings =
        """
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        """;

    // A minimal stand-in for the real EF Core shape: EF.Functions returns a DbFunctions instance, and Like is an
    // extension method declared on a separate static class (DbFunctionsExtensions, mirroring the real
    // Microsoft.EntityFrameworkCore.DbFunctionsExtensions) - not on DbFunctions itself - so the analyzer has to read
    // the receiver from ReceiverType rather than ContainingType. IQueryable<T>/Where/Expression<T> are the real BCL
    // types already available by default in this test project; no mock is needed for them.
    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbFunctions { }

            public static class EF
            {
                public static DbFunctions Functions => null;
            }

            public static class DbFunctionsExtensions
            {
                public static bool Like(this DbFunctions _, string matchExpression, string pattern) => throw new System.NotSupportedException();
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class DbFunctionAttribute : System.Attribute { }
        }

        public class Customer
        {
            public string Name { get; set; }
            public System.DateTime CreatedAt { get; set; }
        }

        public static class CustomFunctions
        {
            [Microsoft.EntityFrameworkCore.DbFunction]
            public static bool IsWeekend(System.DateTime date) => throw new System.NotSupportedException();
        }
        """;

    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_NoncompliantAsPlainStatement() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public bool M(string name) =>
                    EF.Functions.Like(name, "%foo%"); // Noncompliant {{'Like' is only meaningful inside a query expression translated to SQL - calling it here throws NotSupportedException at runtime.}}
            }
            """)
            .Verify();

    // The lambda here converts to System.Func<bool>, not Expression<TDelegate> - it never becomes part of a query.
    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_NoncompliantInsideFuncLambda() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public void M(string name)
                {
                    var f = new System.Func<bool>(() => EF.Functions.Like(name, "%foo%")); // Noncompliant
                }
            }
            """)
            .Verify();

    // Neither of the two nesting levels converts to Expression<TDelegate>, so this must still be reported - the
    // ancestor walk has to keep going past the first enclosing lambda instead of stopping there.
    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_NoncompliantInsideNestedFuncLambdas() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public void M(string name)
                {
                    var f = new System.Func<System.Func<bool>>(() => (System.Func<bool>)(() => EF.Functions.Like(name, "%foo%"))); // Noncompliant
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_CompliantInsideQueryableWhere() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public void M(System.Collections.Generic.List<Customer> customers)
                {
                    var results = customers.AsQueryable().Where(c => EF.Functions.Like(c.Name, "%foo%")).ToList();
                }
            }
            """)
            .VerifyNoIssues();

    // The call itself sits inside Any's Func<Customer, bool> lambda (LINQ to Objects, over the plain Customer[]
    // array) - but that lambda is nested inside the outer Where lambda, which does convert to
    // Expression<Func<Customer, bool>>. Every enclosing lambda has to be checked, not just the nearest one, so this
    // is still compliant.
    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_CompliantInsideLambdaNestedUnderAnExpressionLambda() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public void M(System.Collections.Generic.List<Customer> customers)
                {
                    var results = customers.AsQueryable().Where(c => new[] { c }.Any(x => EF.Functions.Like(x.Name, "%foo%"))).ToList();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_NoncompliantForDbFunctionAttributeOutsideQuery() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public bool M() =>
                    CustomFunctions.IsWeekend(System.DateTime.Now); // Noncompliant {{'IsWeekend' is only meaningful inside a query expression translated to SQL - calling it here throws NotSupportedException at runtime.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_CompliantForDbFunctionAttributeInsideQuery() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public void M(System.Collections.Generic.List<Customer> customers)
                {
                    var results = customers.AsQueryable().Where(c => CustomFunctions.IsWeekend(c.CreatedAt)).ToList();
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DatabaseFunctionShouldOnlyBeCalledInQuery_CompliantForOrdinaryMethodCall() =>
        builder.AddSnippet(
            Usings + Stubs + """

            public class C
            {
                public void M(System.Collections.Generic.List<Customer> customers)
                {
                    var results = customers.AsQueryable().Where(c => c.Name.StartsWith("foo")).ToList();
                }
            }
            """)
            .VerifyNoIssues();
}
