using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    /// <summary>Scanline vector drawing, clipping, affine images, and alpha compositing into a pixel buffer.
    /// Coordinates address pixel edges. Render at a larger resolution and downsample for antialiasing.</summary>
    public sealed class RasterCanvas
    {
        private readonly PixelBuffer _target;
        private readonly Stack<byte[]?> _clips = new Stack<byte[]?>();
        private byte[]? _clip;
        private byte[]? _coverage;
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
                _layerDepth = _layerDepth + 1
            };
            draw(layer);
            for (int y = 0; y < buffer.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < buffer.Width; x++)
                {
                    int index = (y * buffer.Width) + x;
                    byte coverage = layer._coverage[index];
                    if (coverage == 0)
                    {
                        continue;
                    }

                    Rgba32 color = isolated ? buffer[x, y] : RemoveBackdrop(buffer[x, y], _target[x, y], coverage);
                    float mask = alphaMask == null ? 1 : alphaMask[x, y].A / 255f;
                    Paint(x, y, color with { A = (byte)MathF.Round(coverage * opacity * mask) }, blendMode);
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
                        Paint(x, y, sample(Vector2.Transform(new Vector2(x + 0.5f, y + 0.5f), inverse)), blendMode);
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
            byte[]? previous = _clip;
            var next = new byte[_target.Width * _target.Height];
            ScanlineRasterizer.Paint(path, transform, rule, _target.Width, _target.Height, (y, left, right) =>
            {
                for (int x = left; x < right; x++)
                {
                    int index = (y * _target.Width) + x;
                    next[index] = previous == null ? (byte)255 : previous[index];
                }
            }, cancellationToken);
            _clip = next;
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
                    Paint(x, y, color with { A = (byte)MathF.Round(color.A * opacity) }, blendMode);
                }
            }, cancellationToken);
        }

        private void Paint(int x, int y, Rgba32 color, ColorBlendMode mode)
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
        }
    }
}
