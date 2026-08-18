using System;
using System.Collections.Generic;

namespace Tests.Diagnostics
{
    public class LoopVariableShouldNotBeCapturedByDeferredLambda
    {
        public void CapturesLoopVariable()
        {
            var tasks = new List<Action>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(() => Use(i)); // Noncompliant {{'i' is captured by reference and mutated by this loop - every deferred use of this lambda will see the SAME final value, not the value at each iteration. Copy it to a local variable inside the loop body first.}}
            }
        }

        public void AlreadyCopied()
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
}
