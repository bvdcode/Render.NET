using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Render.NET.Tgs
{
    internal class TgsExpressionBudget(TgsExpressionOptions options, CancellationToken token)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private int _evaluations;

        internal void Check()
        {
            token.ThrowIfCancellationRequested();
            if (_elapsed.Elapsed > options.FrameTimeout)
            {
                throw new TimeoutException("Lottie frame expression time limit exceeded.");
            }
        }

        internal void Enter(int depth)
        {
            Check();
            if (++_evaluations > options.MaximumEvaluations || depth > options.MaximumRecursion)
            {
                throw new InvalidDataException("Lottie expression evaluation/dependency limit exceeded.");
            }
        }
    }
}
