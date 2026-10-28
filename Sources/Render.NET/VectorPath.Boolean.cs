using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    public partial class VectorPath
    {
        /// <summary>Combines filled paths by splitting a planar edge arrangement and tracing its boundary.
        /// Curves use the supplied user-unit tolerance. Work is bounded to 16,384 flattened edges and two million candidate intersections.</summary>
        public VectorPath Combine(VectorPath other, PathBooleanOperation operation, FillRule rule = FillRule.NonZero, float tolerance = .1f, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(other);
            if (!float.IsFinite(tolerance) || tolerance <= 0 || !Enum.IsDefined(operation))
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            }

            return new PathArrangement(this, other, operation, rule, tolerance, cancellationToken).Build();
        }
    }
}
