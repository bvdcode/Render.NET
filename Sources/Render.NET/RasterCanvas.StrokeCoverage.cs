using System;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    public partial class RasterCanvas
    {
        /// <summary>Strokes a path with fractional pixel coverage, preserving subpixel-width lines.</summary>
        public void StrokePathAntialiased(VectorPath path, Rgba32 color, StrokeStyle style, Matrix3x2 transform,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(style);
            VectorPath outline = PathStroker.Outline(path, style, transform, cancellationToken);
            ScanlineRasterizer.PaintCoverage(outline, transform, FillRule.NonZero, _target.Width, _target.Height,
                (x, y, coverage) => Paint(x, y, color with { A = (byte)MathF.Round(color.A * coverage) }, blendMode,
                    (byte)MathF.Round(255 * coverage)), cancellationToken);
        }

        /// <summary>Paints a stroke with arbitrary drawing content, applying its fractional shape once
        /// after compositing the content against the current backdrop.</summary>
        public void PaintStrokePath(VectorPath path, StrokeStyle style, Matrix3x2 transform,
            Action<RasterCanvas> paint, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(style);
            ArgumentNullException.ThrowIfNull(paint);
            byte[] shape = new byte[_target.Width * _target.Height];
            VectorPath outline = PathStroker.Outline(path, style, transform, cancellationToken);
            ScanlineRasterizer.PaintCoverage(outline, transform, FillRule.NonZero, _target.Width, _target.Height,
                (x, y, coverage) => shape[(y * _target.Width) + x] = (byte)MathF.Round(255 * coverage), cancellationToken);
            DrawLayerCore(paint, false, 1, ColorBlendMode.Normal, false, null, shape, cancellationToken);
        }

        private byte[] IntersectShape(byte[] shape)
        {
            byte[] clip = new byte[shape.Length];
            for (int i = 0; i < shape.Length; i++)
            {
                if (shape[i] > 0)
                {
                    clip[i] = _clip is null ? (byte)255 : _clip[i];
                }
            }

            return clip;
        }
    }
}
