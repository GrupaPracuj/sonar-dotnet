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
public class PersistenceOperationShouldNotBeDeclaredInApiProjectTest
{
    private const string Stubs =
        """
        namespace GP.Juno.Abstractions.Ado
        {
            public interface IDbExecute { }
            public interface IDbExecute<T> { }
            public interface ITransactional { }
        }
        """;

    [TestMethod]
    public void PersistenceOperationShouldNotBeDeclaredInApiProject_NoncompliantForDbExecuteInApiAssembly() =>
        VerifyForAssemblyName(
            Stubs + """

            public sealed class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int> { } // Noncompliant {{Declare 'LoadOrders' in the data access project - an API assembly is not the place for a persistence operation.}}

            public sealed class DeleteOrder : GP.Juno.Abstractions.Ado.IDbExecute { } // Noncompliant

            public sealed class OrdersUnitOfWork : GP.Juno.Abstractions.Ado.ITransactional { } // Noncompliant
            """,
            "GP.Shop.Api");

    [TestMethod]
    public void PersistenceOperationShouldNotBeDeclaredInApiProject_CompliantOutsideApiAssembly() =>
        VerifyForAssemblyName(
            Stubs + """

            public sealed class LoadOrders : GP.Juno.Abstractions.Ado.IDbExecute<int> { }
            """,
            "GP.Shop.DataAccess");

    [TestMethod]
    public void PersistenceOperationShouldNotBeDeclaredInApiProject_CompliantForOrdinaryApiType() =>
        VerifyForAssemblyName(
            Stubs + """

            public sealed class OrdersService
            {
                public int Count() => 0;
            }
            """,
            "GP.Shop.Api");

    // What an abstract operation is depends on the classes deriving from it, and those are reported on their own.
    [TestMethod]
    public void PersistenceOperationShouldNotBeDeclaredInApiProject_CompliantForAbstractOperation() =>
        VerifyForAssemblyName(
            Stubs + """

            public abstract class OrderQuery : GP.Juno.Abstractions.Ado.IDbExecute<int> { }
            """,
            "GP.Shop.Api");

    private static void VerifyForAssemblyName(string snippet, string assemblyName) =>
        DiagnosticVerifier.Verify(
            new SnippetCompiler(snippet).Compilation.WithAssemblyName(assemblyName),
            [new CS.PersistenceOperationShouldNotBeDeclaredInApiProject()],
            CompilationErrorBehavior.Default,
            null,
            [],
            []);
}
