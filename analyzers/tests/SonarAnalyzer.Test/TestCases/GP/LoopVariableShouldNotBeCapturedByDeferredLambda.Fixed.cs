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
                var iCopy = i;
                tasks.Add(() => Use(iCopy)); // Fixed
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
