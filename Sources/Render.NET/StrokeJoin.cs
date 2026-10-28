using System;
using System.Collections.Generic;

namespace Render.NET
{
    /// <summary>The shape connecting stroke segments.</summary>
    public enum StrokeJoin
    {
        /// <summary>Extend the outer edges to their intersection, subject to the miter limit.</summary>
        Miter,
        /// <summary>Use a circular join.</summary>
        Round,
        /// <summary>Connect the outer edges directly.</summary>
        Bevel
    }
}
