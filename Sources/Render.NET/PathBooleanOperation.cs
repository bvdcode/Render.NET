using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    /// <summary>A planar set operation on filled vector paths.</summary>
    public enum PathBooleanOperation
    {
        /// <summary>Points inside either path.</summary>
        Union,
        /// <summary>Points inside both paths.</summary>
        Intersection,
        /// <summary>Points inside this path and outside the other path.</summary>
        Difference,
        /// <summary>Points inside exactly one path.</summary>
        ExclusiveOr
    }
}
