using System;

namespace Render.NET.Tgs
{
    /// <summary>Explicit opt-in and resource bounds for managed JavaScript expressions.</summary>
    public record TgsExpressionOptions
    {
        /// <summary>Gets the maximum JavaScript statements per property evaluation.</summary>
        public int MaximumStatements { get; init; } = 50_000;
        /// <summary>Gets the maximum JavaScript allocation per property evaluation.</summary>
        public long MaximumMemoryBytes { get; init; } = 32 * 1024 * 1024;
        /// <summary>Gets the maximum JavaScript call depth.</summary>
        public int MaximumRecursion { get; init; } = 32;
        /// <summary>Gets the maximum evaluations across all compositions in one frame.</summary>
        public int MaximumEvaluations { get; init; } = 256;
        /// <summary>Gets the time limit for one property evaluation.</summary>
        public TimeSpan EvaluationTimeout { get; init; } = TimeSpan.FromSeconds(1);
        /// <summary>Gets the total expression time limit for one frame.</summary>
        public TimeSpan FrameTimeout { get; init; } = TimeSpan.FromSeconds(5);

        internal void Validate()
        {
            if (MaximumStatements <= 0 || MaximumStatements == int.MaxValue || MaximumMemoryBytes <= 0 || MaximumMemoryBytes == long.MaxValue || MaximumRecursion <= 0 || MaximumRecursion > 256 || MaximumEvaluations <= 0 || EvaluationTimeout <= TimeSpan.Zero || EvaluationTimeout == TimeSpan.MaxValue || FrameTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(TgsExpressionOptions));
            }
        }
    }
}
