using System;
using System.Collections.Generic;

namespace Render.NET
{
    /// <summary>The shape of an open stroke's end.</summary>
    public enum StrokeCap
    {
        /// <summary>End exactly at the endpoint.</summary>
        Butt,
        /// <summary>A semicircular end.</summary>
        Round,
        /// <summary>Extend by half the line width.</summary>
        Square
    }

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

    /// <summary>Stroke dimensions in the path's coordinate system.</summary>
    public sealed record StrokeStyle
    {
        /// <summary>Gets the stroke width. Zero requests a one-device-pixel hairline.</summary>
        public float Width { get; init; } = 1;
        /// <summary>Gets the line cap.</summary>
        public StrokeCap Cap { get; init; }
        /// <summary>Gets the line join.</summary>
        public StrokeJoin Join { get; init; }
        /// <summary>Gets the maximum miter length divided by half the line width.</summary>
        public float MiterLimit { get; init; } = 10;
        /// <summary>Gets alternating dash and gap lengths. An empty list is solid.</summary>
        public IReadOnlyList<float> Dashes { get; init; } = Array.Empty<float>();
        /// <summary>Gets the distance into the repeated dash pattern.</summary>
        public float DashOffset { get; init; }
    }
}
