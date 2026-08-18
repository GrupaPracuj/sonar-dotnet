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
public class ContractShouldNotExposeEnumsTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.ContractShouldNotExposeEnums>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void ContractShouldNotExposeEnums_NoncompliantEvenWithUnknownAndExplicitValues() =>
        builder.AddSnippet(
            """
            public enum OrderStatus // Noncompliant {{'OrderStatus' is exposed by a contract. Do not use enums in contracts because producers and consumers evolve independently.}}
            {
                Unknown = 0,
                Pending = 1,
                Accepted = 2,
            }

            namespace Contracts
            {
                public sealed record OrderAccepted(System.Guid OrderId, global::OrderStatus Status);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeEnums_NoncompliantForFlagsEnum() =>
        builder.AddSnippet(
            """
            [System.Flags]
            public enum NotificationChannels // Noncompliant
            {
                None = 0,
                Email = 1,
                Sms = 2,
            }

            namespace Contracts
            {
                public sealed record NotificationRequested(global::NotificationChannels Channels);
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeEnums_NoncompliantThroughNestedWrappers() =>
        builder.AddSnippet(
            """
            public enum OrderStatus // Noncompliant
            {
                Pending,
            }

            namespace Contracts
            {
                public sealed class OrderAccepted
                {
                    public System.Collections.Generic.IReadOnlyList<global::OrderStatus?[]> History { get; init; }
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeEnums_NoncompliantForControllerResponse() =>
        builder.AddSnippet(
            """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class HttpGetAttribute : System.Attribute { }
                public abstract class ControllerBase { }
                public class ActionResult<T> { }
            }

            public enum OrderStatus // Noncompliant
            {
                Pending,
            }

            public sealed record OrderView(OrderStatus Status);

            public sealed class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                [Microsoft.AspNetCore.Mvc.HttpGet]
                public Microsoft.AspNetCore.Mvc.ActionResult<OrderView> Get() => null;
            }
            """)
            .Verify();

    [TestMethod]
    public void ContractShouldNotExposeEnums_CompliantForInternalModel() =>
        builder.AddSnippet(
            """
            public enum ProcessingStage
            {
                Started,
                Finished,
            }

            public sealed class OrderProcessor
            {
                public ProcessingStage Stage { get; set; }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void ContractShouldNotExposeEnums_CompliantForContractSuffixAlone() =>
        builder.AddSnippet(
            """
            public enum ResponseStatus
            {
                Ready,
                Failed,
            }

            public sealed record CustomerResponse(ResponseStatus Status);
            """)
            .VerifyNoIssues();
}
