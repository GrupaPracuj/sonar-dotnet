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
public class ControllersShouldNotUseInfrastructureDirectlyTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ControllersShouldNotUseInfrastructureDirectly>()
        .WithOptions(LanguageOptions.CSharpLatest);

    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class HttpGetAttribute : System.Attribute { }
            public interface IActionResult { }
            public abstract class ControllerBase
            {
                protected IActionResult Ok() => null;
                protected IActionResult Ok(object value) => null;
            }
        }

        namespace System.Data.Common
        {
            public abstract class DbConnection { }
        }

        namespace System.Net.Http
        {
            public class HttpClient { }
        }

        namespace GP.Juno.Abstractions.Ado
        {
            public interface IDbExecute<T> { }
            public interface ITransactional { }
        }

        public class ShopDbContext : Microsoft.EntityFrameworkCore.DbContext { }
        public class OrdersConnection : System.Data.Common.DbConnection { }

        public interface IOrderRepository
        {
            object Find(int id);
        }
        """;

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_NoncompliantForFieldAndConstructorParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly ShopDbContext _context; // Noncompliant {{Do not use infrastructure type 'ShopDbContext' directly in a controller - depend on an application abstraction instead.}}

                public OrdersController(ShopDbContext context) => // Noncompliant {{Do not use infrastructure type 'ShopDbContext' directly in a controller - depend on an application abstraction instead.}}
                    _context = context;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id) => Ok(_context);
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_NoncompliantForLocalVariable() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id)
                {
                    ShopDbContext context = new ShopDbContext(); // Noncompliant {{Do not use infrastructure type 'ShopDbContext' directly in a controller - depend on an application abstraction instead.}}
                    return Ok(context);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_NoncompliantForPrimaryConstructorParameter() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController(ShopDbContext context) : Microsoft.AspNetCore.Mvc.ControllerBase // Noncompliant {{Do not use infrastructure type 'ShopDbContext' directly in a controller - depend on an application abstraction instead.}}
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id) => Ok(context);
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_NoncompliantForKnownInfrastructureDependencies() =>
        builder.AddSnippet(
            Stubs + """

            public sealed class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int> { }

            public class OrdersController(
                System.Net.Http.HttpClient client, // Noncompliant {{Do not use infrastructure type 'HttpClient' directly in a controller - depend on an application abstraction instead.}}
                OrdersConnection connection, // Noncompliant {{Do not use infrastructure type 'OrdersConnection' directly in a controller - depend on an application abstraction instead.}}
                LoadOrders operation) // Noncompliant {{Do not use infrastructure type 'LoadOrders' directly in a controller - depend on an application abstraction instead.}}
                : Microsoft.AspNetCore.Mvc.ControllerBase
            {
            }
            """)
            .Verify();

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_CompliantForRepository() =>
        builder.AddSnippet(
            Stubs + """

            public class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                private readonly IOrderRepository _orders;

                public OrdersController(IOrderRepository orders) =>
                    _orders = orders;

                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.IActionResult Get(int id) => Ok(_orders.Find(id));
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_CompliantOutsideController() =>
        builder.AddSnippet(
            Stubs + """

            public class OrderRepository : IOrderRepository
            {
                private readonly ShopDbContext _context;

                public OrderRepository(ShopDbContext context) =>
                    _context = context;

                public object Find(int id) => _context;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_NoncompliantForPersistenceDependenciesInApiProject() =>
        VerifyForAssemblyName(
            Stubs + """

            // Declaring the operation here is GP0133's subject, not this rule's.
            public sealed class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int> { }

            public sealed class RuntimeHandler
            {
                private readonly ShopDbContext context; // Noncompliant {{Do not use infrastructure type 'ShopDbContext' directly in an API project - depend on an application abstraction instead.}}
                private readonly OrdersConnection connection; // Noncompliant {{Do not use infrastructure type 'OrdersConnection' directly in an API project - depend on an application abstraction instead.}}
            }
            """,
            "GP.Shop.Api");

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_CompliantForPersistenceOutsideApiProject() =>
        VerifyForAssemblyName(
            Stubs + """

            public sealed class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int> { }

            public sealed class PersistenceHandler
            {
                private readonly ShopDbContext context;
                private readonly OrdersConnection connection;
            }
            """,
            "GP.Shop");

    [TestMethod]
    public void ControllersShouldNotUseInfrastructureDirectly_CompliantForApiCompositionRootRegistration() =>
        VerifyForAssemblyName(
            Stubs + """

            public static class ServiceSetup
            {
                public static void AddPersistence<TContext>() where TContext : Microsoft.EntityFrameworkCore.DbContext { }

                public static void Configure() =>
                    AddPersistence<ShopDbContext>();
            }
            """,
            "GP.Shop.Api");

    private static void VerifyForAssemblyName(string snippet, string assemblyName) =>
        DiagnosticVerifier.Verify(
            new SnippetCompiler(snippet).Compilation.WithAssemblyName(assemblyName),
            [new CS.ControllersShouldNotUseInfrastructureDirectly()],
            CompilationErrorBehavior.Default,
            null,
            [],
            []);
}
