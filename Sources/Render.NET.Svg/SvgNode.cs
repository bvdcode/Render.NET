using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;

namespace Render.NET.Svg
{
    internal record SvgNode(VectorPath? Path, SvgPaint? Fill, SvgPaint? Stroke, StrokeStyle Pen, FillRule Rule, FillRule ClipRule, Matrix3x2 Transform, float Opacity, float FillOpacity, float StrokeOpacity, SvgClip? Clip, SvgNode[] Children)
    {
        internal SvgMask? Mask { get; init; }
        internal SvgFilter? Filter { get; init; }
        internal ColorBlendMode BlendMode { get; init; }
        internal PixelBuffer? Image { get; init; }
        internal Matrix3x2 ImageTransform { get; init; } = Matrix3x2.Identity;
        internal VectorPath? ImageViewport { get; init; }

        internal void Draw(RasterCanvas canvas, Matrix3x2 parent, SvgRenderContext context)
        {
            CancellationToken token = context.Token;
            token.ThrowIfCancellationRequested();
            if (Opacity == 0)
            {
                return;
            }

            Matrix3x2 matrix = Transform * parent;
            if (Mask is not null || Filter is not null)
            {
                DrawEffects(canvas, matrix, context);
                return;
            }

            canvas.Save();
            try
            {
                ApplyClip(canvas, matrix, token);
                if (Opacity < 1 || BlendMode != ColorBlendMode.Normal)
                {
                    canvas.DrawLayer(layer => Paint(layer, matrix, context), Opacity, BlendMode, cancellationToken: token);
                }
                else
                {
                    Paint(canvas, matrix, context);
                }
            }
            finally
            {
                canvas.Restore();
            }
        }

        private void ApplyClip(RasterCanvas canvas, Matrix3x2 matrix, CancellationToken token)
        {
            if (Clip is null)
            {
                return;
            }

            Matrix3x2 clipMatrix = matrix;
            if (Clip.ObjectBox)
            {
                (float X, float Y, float Width, float Height) box = Bounds(token);
                clipMatrix = Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y) * matrix;
            }

            canvas.ClipUnion(Clip.Shapes.Select(p => (p.Path, clipMatrix, p.Rule)), token);
        }

        private void DrawEffects(RasterCanvas canvas, Matrix3x2 matrix, SvgRenderContext context)
        {
            using IDisposable reservation = context.Reserve(canvas.Buffer.Width, canvas.Buffer.Height, 2);
            PixelBuffer? source = new PixelBuffer(canvas.Buffer.Width, canvas.Buffer.Height);
            Paint(new RasterCanvas(source), matrix, context);
            (float X, float Y, float Width, float Height) box = Bounds(context.Token);
            if (Filter is not null)
            {
                source = Filter.Apply(source, box, matrix, context, canvas.Buffer, Fill, Stroke, FillOpacity, StrokeOpacity);
            }

            if (Mask is not null)
            {
                Mask.Apply(source, box, matrix, context);
            }

            canvas.Save();
            try
            {
                ApplyClip(canvas, matrix, context.Token);
                canvas.DrawImage(source, Matrix3x2.Identity, Opacity, BlendMode, context.Token);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private void Paint(RasterCanvas canvas, Matrix3x2 matrix, SvgRenderContext context)
        {
            CancellationToken token = context.Token;
            if (Image is not null)
            {
                canvas.Save();
                try
                {
                    if (ImageViewport is not null)
                    {
                        canvas.ClipPath(ImageViewport, matrix, cancellationToken: token);
                    }

                    canvas.DrawImage(Image, ImageTransform * matrix, cancellationToken: token);
                }
                finally
                {
                    canvas.Restore();
                }
            }

            if (Path is not null)
            {
                Fill?.Draw(canvas, Path, matrix, Rule, FillOpacity, null, token);
                if (Pen.Width > 0)
                {
                    Stroke?.Draw(canvas, Path, matrix, Rule, StrokeOpacity, Pen, token);
                }
            }

            foreach (SvgNode child in Children)
            {
                child.Draw(canvas, matrix, context);
            }
        }

        internal (float X, float Y, float Width, float Height) Bounds(CancellationToken token) => Outline(Matrix3x2.Identity).GetBounds(token);
        internal void ClipShapes(Matrix3x2 parent, List<SvgClipShape> result)
        {
            if (Clip is not null)
            {
                throw new NotSupportedException("Nested SVG clipping inside clipPath");
            }

            if (Path is not null)
            {
                result.Add(new SvgClipShape(Path.Transform(parent), ClipRule));
            }

            foreach (SvgNode child in Children)
            {
                child.ClipShapes(child.Transform * parent, result);
            }
        }

        internal VectorPath Outline(Matrix3x2 parent)
        {
            VectorPath? result = new VectorPath();
            if (ImageViewport is not null)
            {
                result.Append(ImageViewport.Transform(parent));
            }

            if (Path is not null)
            {
                result.Append(Path.Transform(parent));
            }

            foreach (SvgNode child in Children)
            {
                result.Append(child.Outline(child.Transform * parent));
            }

            return result;
        }
    }
}
