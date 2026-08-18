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
public class EventShouldNotBeRaisedWithNullSenderOrDataTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.EventShouldNotBeRaisedWithNullSenderOrData>().WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_NoncompliantForNullSenderOnDirectInvocation() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                public event EventHandler Shipped;

                public void Ship()
                {
                    Shipped(null, EventArgs.Empty); // Noncompliant {{Do not pass null as the sender - use 'this' (or the actual raising instance) so subscribers know who raised 'Shipped'.}}
                }
            }
            """)
            .Verify();

    // Proves the "?.Invoke(...)" conditional-access shape resolves to the same event symbol as the direct call.
    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_NoncompliantForNullSenderOnConditionalInvoke() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                public event EventHandler Shipped;

                public void Ship()
                {
                    Shipped?.Invoke(null, EventArgs.Empty); // Noncompliant {{Do not pass null as the sender - use 'this' (or the actual raising instance) so subscribers know who raised 'Shipped'.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_NoncompliantForNullSenderOnPlainInvoke() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                public event EventHandler Shipped;

                public void Ship()
                {
                    Shipped.Invoke(null, EventArgs.Empty); // Noncompliant {{Do not pass null as the sender - use 'this' (or the actual raising instance) so subscribers know who raised 'Shipped'.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_NoncompliantForNullEventData() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                public event EventHandler Shipped;

                public void Ship()
                {
                    Shipped(this, null); // Noncompliant {{Do not pass null as the event data for 'Shipped' - pass EventArgs.Empty instead, callers expect a non-null value.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_NoncompliantForBothNull() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                public event EventHandler Shipped;

                public void Ship()
                {
                    Shipped(null, null); // Noncompliant {{Do not pass null as the sender - use 'this' (or the actual raising instance) so subscribers know who raised 'Shipped'.}}
                                          // Noncompliant@-1 {{Do not pass null as the event data for 'Shipped' - pass EventArgs.Empty instead, callers expect a non-null value.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_NoncompliantForCustomEventArgsSubclass() =>
        builder.AddSnippet(
            """
            using System;

            public class FooEventArgs : EventArgs { }

            public class Order
            {
                public event EventHandler<FooEventArgs> FooRaised;

                public void Raise()
                {
                    FooRaised(this, null); // Noncompliant {{Do not pass null as the event data for 'FooRaised' - pass a non-null 'FooEventArgs' instance instead.}}
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_CompliantWhenSenderAndDataAreProper() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                public event EventHandler Shipped;

                public void Ship()
                {
                    Shipped(this, EventArgs.Empty);
                }
            }
            """)
            .VerifyNoIssues();

    // The sender check deliberately does not cover static events - there is no single instance to point to, and
    // the guideline's advice for that case is more contested.
    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_CompliantForStaticEventNullSender() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                public static event EventHandler Shipped;

                public static void Ship()
                {
                    Shipped(null, EventArgs.Empty);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_CompliantForNonEventDelegateField() =>
        builder.AddSnippet(
            """
            using System;

            public class Order
            {
                private EventHandler _shipped;

                public void Ship()
                {
                    _shipped(null, null);
                }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void EventShouldNotBeRaisedWithNullSenderOrData_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("EventShouldNotBeRaisedWithNullSenderOrData.cs")
            .WithCodeFix<CS.EventShouldNotBeRaisedWithNullSenderOrDataCodeFix>()
            .WithCodeFixedPaths("EventShouldNotBeRaisedWithNullSenderOrData.Fixed.cs")
            .VerifyCodeFix();
}
