using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    /// <summary>Scanline vector drawing, clipping, affine images, and alpha compositing into a pixel buffer.
    /// Coordinates address pixel edges. Render at a larger resolution and downsample for antialiasing.</summary>
    public sealed partial class RasterCanvas
    {
        private readonly PixelBuffer _target;
        private readonly Stack<byte[]?> _clips = new Stack<byte[]?>();
        private byte[]? _clip;
        private byte[]? _coverage;
        private byte[]? _shape;
        private PixelBuffer? _knockoutBackdrop;
        private bool _knockout;
        private RasterCanvas? _element;
        private int _paintLeft = int.MaxValue;
        private int _paintTop = int.MaxValue;
        private int _paintRight;
        private int _paintBottom;
        private int _layerDepth;

        /// <summary>Creates a canvas over an existing image.</summary>
        public RasterCanvas(PixelBuffer target)
        {
            ArgumentNullException.ThrowIfNull(target);
            _target = target;
        }

        /// <summary>Gets the backing pixel buffer.</summary>
        public PixelBuffer Buffer => _target;

        /// <summary>Composites a group once, with its own opacity, blend mode, isolation, and optional alpha mask.
        /// Non-isolated layers retain the backdrop for internal blend modes. At most eight nested layers are supported.</summary>
        public void DrawLayer(Action<RasterCanvas> draw, float opacity = 1, ColorBlendMode blendMode = ColorBlendMode.Normal,
            bool isolated = true, PixelBuffer? alphaMask = null, CancellationToken cancellationToken = default)
            => DrawLayer(draw, false, opacity, blendMode, isolated, alphaMask, cancellationToken);

        /// <summary>Composites a group with optional knockout semantics. Use DrawObject for each
        /// elementary object in a knockout group; opacity remains independent of its shape.</summary>
        public void DrawLayer(Action<RasterCanvas> draw, bool knockout, float opacity = 1, ColorBlendMode blendMode = ColorBlendMode.Normal,
            bool isolated = true, PixelBuffer? alphaMask = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(draw);
            if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1 || _layerDepth >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(opacity), "Invalid opacity or more than eight nested layers.");
            }

            if (alphaMask != null && (alphaMask.Width != _target.Width || alphaMask.Height != _target.Height))
            {
                throw new ArgumentException("The layer mask must match the canvas dimensions.", nameof(alphaMask));
            }

            var buffer = new PixelBuffer(_target.Width, _target.Height);
            if (!isolated)
            {
                _target.Pixels.CopyTo(buffer.Pixels);
            }

            var layer = new RasterCanvas(buffer)
            {
                _clip = _clip,
                _coverage = new byte[_target.Width * _target.Height],
                _shape = new byte[_target.Width * _target.Height],
                _knockoutBackdrop = !isolated ? _target : null,
                _knockout = knockout,
                _layerDepth = _layerDepth + 1
            };
            draw(layer);
            CompositeLayer(layer, opacity, blendMode, isolated, alphaMask, cancellationToken);
        }

        private void CompositeLayer(RasterCanvas layer, float opacity, ColorBlendMode blendMode, bool isolated, PixelBuffer? alphaMask, CancellationToken cancellationToken)
        {
            PixelBuffer buffer = layer._target;
            for (int y = 0; y < buffer.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < buffer.Width; x++)
                {
                    int index = (y * buffer.Width) + x;
                    byte coverage = layer._coverage![index];
                    byte shape = layer._shape![index];
                    if (shape == 0)
                    {
                        continue;
                    }

                    Rgba32 color = coverage == 0 ? default : isolated ? buffer[x, y] : RemoveBackdrop(buffer[x, y], _target[x, y], coverage);
                    float mask = alphaMask == null ? 1 : alphaMask[x, y].A / 255f;
                    Paint(x, y, color with { A = (byte)MathF.Round(coverage * opacity * mask) }, blendMode, shape);
                }
            }
        }

        private static Rgba32 RemoveBackdrop(Rgba32 result, Rgba32 backdrop, byte coverage)
        {
            float alpha = coverage / 255f;
            float retained = ((1 - alpha) * backdrop.A) / 255f;
            float resultAlpha = alpha + retained;
            byte Channel(byte value, byte background) => (byte)Math.Clamp((int)MathF.Round(((value * resultAlpha) - (background * retained)) / alpha), 0, 255);
            return new Rgba32(Channel(result.R, backdrop.R), Channel(result.G, backdrop.G), Channel(result.B, backdrop.B), coverage);
        }

        /// <summary>Paints the clipping region with colors sampled in the supplied coordinate system.</summary>
        public void DrawShader(Func<Vector2, Rgba32> sample, Matrix3x2 transform,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
            => DrawShader(sample, null, transform, blendMode, cancellationToken);

        /// <summary>Paints a shader with an explicit shape predicate, allowing a transparent
        /// object to knock out earlier group members without marking points outside its shape.</summary>
        public void DrawShader(Func<Vector2, Rgba32> sample, Func<Vector2, bool>? contains, Matrix3x2 transform,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sample);
            if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse))
            {
                return;
            }

            for (int y = 0; y < _target.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < _target.Width; x++)
                {
                    if (_clip == null || _clip[(y * _target.Width) + x] != 0)
                    {
                        Vector2 point = Vector2.Transform(new Vector2(x + 0.5f, y + 0.5f), inverse);
                        Rgba32 color = sample(point);
                        if (contains?.Invoke(point) ?? color.A != 0)
                        {
                            Paint(x, y, color, blendMode);
                        }
                    }
                }
            }
        }

        /// <summary>Saves the clipping region. At most 64 nested saves are supported.</summary>
        public void Save()
        {
            if (_clips.Count >= 64)
            {
                throw new InvalidOperationException("The canvas state stack exceeds 64 entries.");
            }

            _clips.Push(_clip);
        }

        /// <summary>Restores the most recently saved clipping region.</summary>
        public void Restore() => _clip = _clips.Pop();

        /// <summary>Intersects the clipping region with a path. An empty path produces an empty clip.</summary>
        public void ClipPath(VectorPath path, Matrix3x2 transform, FillRule rule = FillRule.NonZero, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ClipUnion(new[] { (path, transform, rule) }, cancellationToken);
        }

        /// <summary>Intersects the clipping region with a stroke, including its affine pen and dash pattern.</summary>
        public void ClipStrokePath(VectorPath path, StrokeStyle style, Matrix3x2 transform, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(style);
            ClipPath(PathStroker.Outline(path, style, transform, cancellationToken), transform, cancellationToken: cancellationToken);
        }

        /// <summary>Gets the smallest pixel rectangle containing the current clipping region. An empty clip has zero size.</summary>
        public (int X, int Y, int Width, int Height) GetClipBounds(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clip == null)
            {
                return (0, 0, _target.Width, _target.Height);
            }

            int left = _target.Width;
            int top = _target.Height;
            int right = 0;
            int bottom = 0;
            for (int y = 0; y < _target.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < _target.Width; x++)
                {
                    if (_clip[(y * _target.Width) + x] != 0)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x + 1);
                        top = Math.Min(top, y);
                        bottom = y + 1;
                    }
                }
            }

            return right <= left ? (0, 0, 0, 0) : (left, top, right - left, bottom - top);
        }

        /// <summary>Fills a transformed path using straight-alpha source-over or a separable blend mode.</summary>
        public void FillPath(VectorPath path, Rgba32 color, Matrix3x2 transform, FillRule rule = FillRule.NonZero,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ScanlineRasterizer.Paint(path, transform, rule, _target.Width, _target.Height, (y, left, right) =>
            {
                for (int x = left; x < right; x++)
                {
                    Paint(x, y, color, blendMode);
                }
            }, cancellationToken);
        }

        /// <summary>Strokes a path before applying its transform, preserving affine pen shapes.</summary>
        public void StrokePath(VectorPath path, Rgba32 color, StrokeStyle style, Matrix3x2 transform,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(style);
            FillPath(PathStroker.Outline(path, style, transform, cancellationToken), color, transform, FillRule.NonZero, blendMode, cancellationToken);
        }

        /// <summary>Draws an image with a source-pixel-to-canvas transform. Source and destination must differ.</summary>
        public void DrawImage(PixelBuffer image, Matrix3x2 transform, float opacity = 1,
            ColorBlendMode blendMode = ColorBlendMode.Normal, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (ReferenceEquals(image, _target) || !float.IsFinite(opacity) || opacity < 0 || opacity > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(image));
            }

            if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse))
            {
                return;
            }

            var rectangle = new VectorPath().Rectangle(0, 0, image.Width, image.Height);
            ScanlineRasterizer.Paint(rectangle, transform, FillRule.NonZero, _target.Width, _target.Height, (y, left, right) =>
            {
                for (int x = left; x < right; x++)
                {
                    Vector2 point = Vector2.Transform(new Vector2(x + 0.5f, y + 0.5f), inverse);
                    int sourceX = Math.Clamp((int)MathF.Floor(point.X), 0, image.Width - 1);
                    int sourceY = Math.Clamp((int)MathF.Floor(point.Y), 0, image.Height - 1);
                    Rgba32 color = image[sourceX, sourceY];
                    Paint(x, y, color with { A = (byte)MathF.Round(color.A * opacity) }, blendMode, color.A);
                }
            }, cancellationToken);
        }

        private void Paint(int x, int y, Rgba32 color, ColorBlendMode mode, byte shape = 255)
        {
            int index = (y * _target.Width) + x;
            if (_clip != null && _clip[index] == 0)
            {
                return;
            }

            PixelCompositor.Blend(_target.Pixels.Span, index * 4, color, mode);
            if (_coverage != null)
            {
                _coverage[index] = (byte)(color.A + (((_coverage[index] * (255 - color.A)) + 127) / 255));
            }

            if (_shape != null)
            {
                if (shape != 0)
                {
                    _paintLeft = Math.Min(_paintLeft, x);
                    _paintTop = Math.Min(_paintTop, y);
                    _paintRight = Math.Max(_paintRight, x + 1);
                    _paintBottom = Math.Max(_paintBottom, y + 1);
                }

                _shape[index] = (byte)(shape + (((_shape[index] * (255 - shape)) + 127) / 255));
            }
        }
    }
}
