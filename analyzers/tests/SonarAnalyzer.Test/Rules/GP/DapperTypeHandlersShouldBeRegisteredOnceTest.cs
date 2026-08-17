using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DapperTypeHandlersShouldBeRegisteredOnceTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DapperTypeHandlersShouldBeRegisteredOnce>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Dapper
        {
            public static class SqlMapper
            {
                public static void AddTypeHandler(object handler) { }
            }
        }

        public sealed class MoneyTypeHandler { }

        """;

    [TestMethod]
    public void DapperTypeHandlersShouldBeRegisteredOnce_NoncompliantInInstanceConstructor() =>
        builder.AddSnippet(
            Stubs + """
            public class OrderRepository
            {
                public OrderRepository()
                {
                    Dapper.SqlMapper.AddTypeHandler(new MoneyTypeHandler()); // Noncompliant {{Register Dapper type handlers once during application startup, not in an instance constructor.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DapperTypeHandlersShouldBeRegisteredOnce_NoncompliantInConstructorExpressionBody() =>
        builder.AddSnippet(
            Stubs + """
            public class OrderRepository
            {
                public OrderRepository() =>
                    Dapper.SqlMapper.AddTypeHandler(new MoneyTypeHandler()); // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void DapperTypeHandlersShouldBeRegisteredOnce_CompliantAtCompositionRoot() =>
        builder.AddSnippet(
            Stubs + """
            public static class DapperConfiguration
            {
                public static void Configure() =>
                    Dapper.SqlMapper.AddTypeHandler(new MoneyTypeHandler());
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DapperTypeHandlersShouldBeRegisteredOnce_CompliantInStaticConstructor() =>
        builder.AddSnippet(
            Stubs + """
            public static class DapperConfiguration
            {
                static DapperConfiguration()
                {
                    Dapper.SqlMapper.AddTypeHandler(new MoneyTypeHandler());
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DapperTypeHandlersShouldBeRegisteredOnce_IgnoresDeferredNestedFunctions() =>
        builder.AddSnippet(
            Stubs + """
            public class OrderRepository
            {
                public OrderRepository()
                {
                    System.Action configureLater = () =>
                        Dapper.SqlMapper.AddTypeHandler(new MoneyTypeHandler());

                    void ConfigureLater()
                    {
                        Dapper.SqlMapper.AddTypeHandler(new MoneyTypeHandler());
                    }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void DapperTypeHandlersShouldBeRegisteredOnce_IgnoresUnrelatedMethod() =>
        builder.AddSnippet(
            Stubs + """
            public static class LocalMapper
            {
                public static void AddTypeHandler(object handler) { }
            }

            public class OrderRepository
            {
                public OrderRepository()
                {
                    LocalMapper.AddTypeHandler(new MoneyTypeHandler());
                }
            }
            """)
            .VerifyNoIssues();
}
