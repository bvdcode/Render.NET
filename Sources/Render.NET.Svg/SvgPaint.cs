using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;

namespace Render.NET.Svg
{
    internal abstract record SvgPaint
    {
        internal abstract void Draw(RasterCanvas canvas, VectorPath path, Matrix3x2 matrix, FillRule rule, float opacity, StrokeStyle? stroke, CancellationToken token);
        internal virtual void DrawUnbounded(RasterCanvas canvas, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, float opacity, CancellationToken token) => Draw(canvas, new VectorPath().Rectangle(box.X, box.Y, box.Width, box.Height), matrix, FillRule.NonZero, opacity, null, token);
    }
}
