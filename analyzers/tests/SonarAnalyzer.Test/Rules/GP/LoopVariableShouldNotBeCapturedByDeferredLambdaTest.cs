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
public class LoopVariableShouldNotBeCapturedByDeferredLambdaTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.LoopVariableShouldNotBeCapturedByDeferredLambda>()
        .WithOptions(LanguageOptions.CSharpLatest);

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_NoncompliantWhenAddedToACollection() =>
        builder.AddSnippet(
            """
            using System;
            using System.Collections.Generic;

            public class C
            {
                public void M1()
                {
                    var tasks = new List<Action>();
                    for (int i = 0; i < 10; i++)
                    {
                        tasks.Add(() => Use(i)); // Noncompliant {{'i' is captured by reference and mutated by this loop - every deferred use of this lambda will see the SAME final value, not the value at each iteration. Copy it to a local variable inside the loop body first.}}
                    }
                }

                private static void Use(int value) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_CompliantWhenNoLambdaAtAll() =>
        builder.AddSnippet(
            """
            using System;

            public class C
            {
                public void M2()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Console.WriteLine(Compute(i));
                    }
                }

                private static int Compute(int value) => value;
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_CompliantWhenAlreadyCopiedToALocal() =>
        builder.AddSnippet(
            """
            using System;
            using System.Collections.Generic;

            public class C
            {
                public void M3()
                {
                    var tasks = new List<Action>();
                    for (int i = 0; i < 10; i++)
                    {
                        var iCopy = i;
                        tasks.Add(() => Use(iCopy));
                    }
                }

                private static void Use(int value) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_CompliantForForEach() =>
        builder.AddSnippet(
            """
            using System.Collections.Generic;

            public class C
            {
                public void M4(List<int> items)
                {
                    var results = new List<int>();
                    foreach (var x in items)
                    {
                        results.Add(x);
                    }
                }
            }
            """)
            .VerifyNoIssues();

    // Process(...) is not one of the three recognized deferred sinks (collection Add/Enqueue/Push, Task/ThreadPool/Thread,
    // event +=), so a lambda passed to it is not reported even though it still captures the mutated loop variable - the rule
    // is deliberately narrow to keep false positives at zero.
    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_CompliantWhenSinkIsNotRecognized() =>
        builder.AddSnippet(
            """
            using System;

            public class C
            {
                public void M5()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Process(() => Use(i));
                    }
                }

                private static void Process(Action action) => action();
                private static void Use(int value) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_CompliantForCustomSynchronousAdd() =>
        builder.AddSnippet(
            """
            using System;

            public sealed class Runner
            {
                public void Add(Action action) => action();
            }

            public class C
            {
                public void M(Runner runner)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        runner.Add(() => Use(i));
                    }
                }

                private static void Use(int value) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_NoncompliantWhenPassedToTaskRun() =>
        builder.AddSnippet(
            """
            using System.Threading.Tasks;

            public class C
            {
                public void M6()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Task.Run(() => Use(i)); // Noncompliant
                    }
                }

                private static void Use(int value) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_NoncompliantWhenSubscribedToAnEvent() =>
        builder.AddSnippet(
            """
            using System;

            public class C
            {
                public event Action Fired;

                public void M7()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Fired += () => Use(i); // Noncompliant
                    }
                }

                private static void Use(int value) { }
            }
            """)
            .Verify();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_CompliantWhenCapturingAnUnrelatedOuterVariable() =>
        builder.AddSnippet(
            """
            using System;
            using System.Collections.Generic;

            public class C
            {
                public void M8()
                {
                    var tasks = new List<Action>();
                    var shared = 42;
                    for (int i = 0; i < 10; i++)
                    {
                        tasks.Add(() => Use(shared));
                    }
                }

                private static void Use(int value) { }
            }
            """)
            .VerifyNoIssues();

    [TestMethod]
    public void LoopVariableShouldNotBeCapturedByDeferredLambda_CodeFix() =>
        builder.WithBasePath("GP")
            .AddPaths("LoopVariableShouldNotBeCapturedByDeferredLambda.cs")
            .WithCodeFix<CS.LoopVariableShouldNotBeCapturedByDeferredLambdaCodeFix>()
            .WithCodeFixedPaths("LoopVariableShouldNotBeCapturedByDeferredLambda.Fixed.cs")
            .VerifyCodeFix();
}
