using System;
using System.IO;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Core;

namespace Render.NET.Pdf
{
    internal record TilingPatternCommand(IPdfDrawCommand[] Commands, PdfRectangle Box, double XStep, double YStep,
        Matrix3x2 Placement, float Opacity, ColorBlendMode Blend, int TilingType) : IPdfDrawCommand
    {
        private const int MaximumTiles = 4096;
        private const int MaximumTileOperations = 1_000_000;

        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            Matrix3x2 transform = Placement * device;
            if (!Matrix3x2.Invert(transform, out Matrix3x2 inverse))
            {
                return;
            }

            if (!float.IsFinite(inverse.M11 + inverse.M12 + inverse.M21 + inverse.M22 + inverse.M31 + inverse.M32))
            {
                throw new InvalidDataException("Pattern placement exceeds finite rendering coordinates.");
            }

            if (TilingType != 2 && !NeedsBackdrop && Box.Width <= XStep && Box.Height <= YStep && TryTexture(canvas, transform, token, out PixelBuffer? texture))
            {
                canvas.DrawShader(point => Sample(texture!, point), transform, Blend, token);
                return;
            }

            DrawTiles(canvas, transform, inverse, token);
        }

        internal bool NeedsBackdrop => Array.Exists(Commands, UsesBackdrop);

        private static bool UsesBackdrop(IPdfDrawCommand command) => command switch
        {
            FillCommand fill => fill.Blend != ColorBlendMode.Normal,
            StrokeCommand stroke => stroke.Blend != ColorBlendMode.Normal,
            ImageCommand image => image.Blend != ColorBlendMode.Normal,
            ShadingCommand shading => shading.Blend != ColorBlendMode.Normal,
            StateCommand or ClipCommand => false,
            PatternPaintCommand paint => UsesBackdrop(paint.Pattern),
            MaskedCommand masked => UsesBackdrop(masked.Command),
            GroupCommand group => group.Blend != ColorBlendMode.Normal || (!group.Isolated && Array.Exists(group.Commands, UsesBackdrop)),
            TilingPatternCommand tile => tile.Blend != ColorBlendMode.Normal || tile.NeedsBackdrop,
            _ => true
        };

        private bool TryTexture(RasterCanvas canvas, Matrix3x2 transform, CancellationToken token, out PixelBuffer? texture)
        {
            double width = Math.Max(1, Math.Ceiling(Box.Width * new Vector2(transform.M11, transform.M12).Length()));
            double height = Math.Max(1, Math.Ceiling(Box.Height * new Vector2(transform.M21, transform.M22).Length()));
            texture = null;
            if (!double.IsFinite(width * height) || width * height > (long)canvas.Buffer.Width * canvas.Buffer.Height)
            {
                return false;
            }

            texture = new PixelBuffer((int)width, (int)height);
            Matrix3x2 local = Matrix3x2.CreateTranslation((float)-Box.Left, (float)-Box.Bottom)
                * Matrix3x2.CreateScale((float)(width / Box.Width), (float)(height / Box.Height));
            GroupCommand.DrawCommands(Commands, new RasterCanvas(texture), local, token);
            return true;
        }

        private Rgba32 Sample(PixelBuffer texture, Vector2 point)
        {
            double x = Wrap(point.X - Box.Left, XStep);
            double y = Wrap(point.Y - Box.Bottom, YStep);
            if (x >= Box.Width || y >= Box.Height)
            {
                return default;
            }

            int px = Math.Min(texture.Width - 1, (int)((x * texture.Width) / Box.Width));
            int py = Math.Min(texture.Height - 1, (int)((y * texture.Height) / Box.Height));
            Rgba32 color = texture[px, py];
            return color with { A = (byte)MathF.Round(color.A * Opacity) };
        }

        private static double Wrap(double coordinate, double step) => ((coordinate % step) + step) % step;

        private void DrawTiles(RasterCanvas canvas, Matrix3x2 transform, Matrix3x2 inverse, CancellationToken token)
        {
            (int X, int Y, int Width, int Height) clip = canvas.GetClipBounds(token);
            if (clip.Width == 0 || clip.Height == 0)
            {
                return;
            }

            Vector2 a = Vector2.Transform(new Vector2(clip.X, clip.Y), inverse);
            Vector2 b = Vector2.Transform(new Vector2(clip.X + clip.Width, clip.Y), inverse);
            Vector2 c = Vector2.Transform(new Vector2(clip.X, clip.Y + clip.Height), inverse);
            Vector2 d = Vector2.Transform(new Vector2(clip.X + clip.Width, clip.Y + clip.Height), inverse);
            Vector2 minimum = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 maximum = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            double left = Math.Floor((minimum.X - Box.Right) / XStep);
            double bottom = Math.Floor((minimum.Y - Box.Top) / YStep);
            double right = Math.Ceiling((maximum.X - Box.Left) / XStep);
            double top = Math.Ceiling((maximum.Y - Box.Bottom) / YStep);
            double count = ((right - left) + 1) * ((top - bottom) + 1);
            if (!double.IsFinite(count) || count > MaximumTiles || count * Commands.Length > MaximumTileOperations
                || left < int.MinValue || bottom < int.MinValue || right >= int.MaxValue || top >= int.MaxValue)
            {
                throw new InvalidDataException("Tiling pattern exceeds its tile or replay operation budget.");
            }

            canvas.DrawLayer(layer =>
            {
                for (int row = (int)bottom; row <= (int)top; row++)
                {
                    for (int column = (int)left; column <= (int)right; column++)
                    {
                        token.ThrowIfCancellationRequested();
                        Matrix3x2 placement = Matrix3x2.CreateTranslation((float)(column * XStep), (float)(row * YStep)) * transform;
                        GroupCommand.DrawCommands(Commands, layer, placement, token);
                    }
                }
            }, Opacity, Blend, isolated: false, cancellationToken: token);
        }
    }
}
