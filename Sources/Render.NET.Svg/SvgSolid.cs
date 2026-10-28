using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;

namespace Render.NET.Svg
{
    internal record SvgSolid(Rgba32 Color) : SvgPaint
    {
        internal override void DrawUnbounded(RasterCanvas canvas, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, float opacity, CancellationToken token) => canvas.DrawShader(_ => SvgColor.Alpha(Color, opacity), Matrix3x2.Identity, cancellationToken: token);
        internal override void Draw(RasterCanvas canvas, VectorPath path, Matrix3x2 matrix, FillRule rule, float opacity, StrokeStyle? stroke, CancellationToken token)
        {
            Rgba32 color = SvgColor.Alpha(Color, opacity);
            if (stroke is null)
            {
                canvas.FillPath(path, color, matrix, rule, cancellationToken: token);
            }
            else
            {
                canvas.StrokePath(path, color, stroke, matrix, cancellationToken: token);
            }
        }
    }
}
