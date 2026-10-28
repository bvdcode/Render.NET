using System;
using System.Collections.Generic;
using System.Numerics;

namespace Render.NET
{
    /// <summary>The rule used to determine the inside of a path.</summary>
    public enum FillRule
    {
        /// <summary>Use the signed number of edge crossings.</summary>
        NonZero,
        /// <summary>Alternate between outside and inside at each edge crossing.</summary>
        EvenOdd
    }
}
