using System.Buffers;
using SkiaSharp;
using Svg.Skia;

namespace Render.NET.Svg.Benchmarks
{
    internal static class SkiaReference
    {
        internal static PixelBuffer Raster(string xml, int size)
        {
            using SKSvg parsed = Parse(xml);
            using SKSurface surface = Surface(parsed.Picture!, size);
            PixelBuffer result = new(size, size);
            using MemoryHandle pixels = result.Pixels.Pin();
            using SKColorSpace srgb = SKColorSpace.CreateSrgb();
            unsafe
            {
                if (!surface.ReadPixels(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul, srgb), (IntPtr)pixels.Pointer, size * 4, 0, 0))
                {
                    throw new InvalidOperationException("Skia RGBA readback failed.");
                }
            }

            return result;
        }

        internal static long Png(string xml, int size)
        {
            using SKSvg parsed = Parse(xml);
            using SKSurface surface = Surface(parsed.Picture!, size);
            using SKImage snapshot = surface.Snapshot();
            using SKData png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray().Length;
        }

        private static SKSvg Parse(string xml)
        {
            SKSvg svg = new();
            if (svg.FromSvg(xml) is null)
            {
                svg.Dispose();
                throw new InvalidDataException("Svg.Skia returned no picture.");
            }

            return svg;
        }

        private static SKSurface Surface(SKPicture picture, int size)
        {
            using SKColorSpace srgb = SKColorSpace.CreateSrgb();
            SKSurface surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul, srgb)) ?? throw new InvalidOperationException("No Skia CPU surface.");
            surface.Canvas.Clear(SKColors.Transparent);
            SKRect box = picture.CullRect;
            float scale = Math.Min(size / box.Width, size / box.Height);
            surface.Canvas.Translate((size - (box.Width * scale)) / 2, (size - (box.Height * scale)) / 2);
            surface.Canvas.Scale(scale);
            surface.Canvas.Translate(-box.Left, -box.Top);
            surface.Canvas.DrawPicture(picture);
            surface.Canvas.Flush();
            return surface;
        }
    }
}
