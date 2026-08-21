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
public class PropertyShouldNotReturnArrayTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.PropertyShouldNotReturnArray>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForPublicGetOnlyProperty() =>
        builder.AddSnippet(
            """
            public class Book
            {
                public string[] Pages { get; } // Noncompliant {{'Pages' returns an array - callers can mutate it through this property. Return a read-only collection, or a method that returns a copy.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForFreshExpressionBodiedArrays() =>
        builder.AddSnippet(
            """
            using System.Linq;

            public class Survey
            {
                private readonly int[] stored = [1, 2, 3];

                public int[] Questions => [.. stored];
                public int[] Answers => new[] { 1, 2, 3 };
                public int[] Scores => stored.ToArray();
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForStoredArrays() =>
        builder.AddSnippet(
            """
            public class Survey
            {
                private readonly int[] questions = [1, 2, 3];

                public int[] Questions => questions; // Noncompliant
                public int[] Answers { get; } // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantWhenAnyGetterPathReturnsStoredArray() =>
        builder.AddSnippet(
            """
            public class Survey
            {
                private readonly int[] questions = [1, 2, 3];

                public int[] Questions // Noncompliant
                {
                    get
                    {
                        if (questions.Length == 0)
                        {
                            return new int[0];
                        }

                        return questions;
                    }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForProtectedSetter() =>
        builder.AddSnippet(
            """
            public class Book
            {
                protected int[] Scores { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForPublicSetter() =>
        builder.AddSnippet(
            """
            public class Book
            {
                public int[] Scores { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForPrivateSetter() =>
        builder.AddSnippet(
            """
            public class Book
            {
                public int[] Scores { get; private set; } // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForPrivateProperty() =>
        builder.AddSnippet(
            """
            public class Book
            {
                private string[] Pages { get; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForPrivateGetter() =>
        builder.AddSnippet(
            """
            public class Book
            {
                public string[] Pages { private get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForReadOnlyCollectionProperty() =>
        builder.AddSnippet(
            """
            using System.Collections.ObjectModel;

            public class Book
            {
                public ReadOnlyCollection<string> Pages { get; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForBinaryPayload() =>
        builder.AddSnippet(
            """
            public class ApplicationFile
            {
                public byte[] Content { get; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForAttributeType() =>
        builder.AddSnippet(
            """
            public class MyAttribute : System.Attribute
            {
                public string[] AllowedValues { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForMessageContractType() =>
        builder.AddSnippet(
            """
            namespace Contracts
            {
                public class UpdateOrder
                {
                    public int[] ItemIds { get; }
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForSourceContractAssemblyWithNestedContractsEventsNamespace() =>
        VerifyForAssemblyName(
            """
            namespace GP.Warta.Features.Contracts.Events
            {
                public interface IPolicyPublished
                {
                    int[] ItemIds { get; }
                }
            }
            """,
            "GP.Warta.Features.Contracts");

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForSourceContractAssemblyWithNestedContractsModelsNamespace() =>
        VerifyForAssemblyName(
            """
            namespace GP.Narew.Contracts.Models
            {
                public interface IPolicySnapshot
                {
                    int[] ItemIds { get; }
                }
            }
            """,
            "GP.Narew.Contracts");

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForSingularContractAssemblyName() =>
        VerifyForAssemblyName(
            """
            namespace GP.RestApi.Filestore.Contract.Models
            {
                public sealed class ExportedFile
                {
                    public string[] Paths { get; } // Noncompliant {{'Paths' returns an array - callers can mutate it through this property. Return a read-only collection, or a method that returns a copy.}}
                }
            }
            """,
            "GP.RestApi.Filestore.Contract");

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForCommandsAssemblyName() =>
        VerifyForAssemblyName(
            """
            namespace GP.FileStoreTools.Commands.Models
            {
                public sealed class BatchCommand
                {
                    public string[] Paths { get; } // Noncompliant {{'Paths' returns an array - callers can mutate it through this property. Return a read-only collection, or a method that returns a copy.}}
                }
            }
            """,
            "GP.FileStoreTools.Commands");

    [TestMethod]
    public void PropertyShouldNotReturnArray_NoncompliantForRequestSuffixAlone() =>
        builder.AddSnippet(
            """
            public class UpdateOrderRequest
            {
                public int[] ItemIds { get; } // Noncompliant
            }
            """)
            .Verify();

    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForControllerResponseOutsideContractsNamespace() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public abstract class ControllerBase { }
                public class ActionResult<T> { }
            }

            public sealed class OrderView
            {
                public int[] ItemIds { get; }
            }

            public sealed class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<OrderView> Get() => null;
            }
            """)
            .VerifyNoIssues();

    // Only the override in Book is excluded - the abstract declaration in BookBase is still reported, since that is
    // the one site where the shape could actually be changed.
    [TestMethod]
    public void PropertyShouldNotReturnArray_CompliantForOverride() =>
        builder.AddSnippet(
            """
            public abstract class BookBase
            {
                public abstract string[] Pages { get; } // Noncompliant
            }

            public class Book : BookBase
            {
                public override string[] Pages { get; }
            }
            """)
            .Verify();

    private static void VerifyForAssemblyName(string snippet, string assemblyName) =>
        DiagnosticVerifier.Verify(
            new SnippetCompiler(snippet).Compilation.WithAssemblyName(assemblyName),
            [new CS.PropertyShouldNotReturnArray()],
            CompilationErrorBehavior.Default,
            null,
            [],
            []);
}
