using System;

namespace Render.NET
{
    /// <summary>Separable color blend modes, evaluated in sRGB.</summary>
    public enum ColorBlendMode
    {
        /// <summary>Source-over alpha compositing.</summary>
        Normal,
        /// <summary>Multiply source and backdrop.</summary>
        Multiply,
        /// <summary>Invert, multiply, and invert the result.</summary>
        Screen,
        /// <summary>Keep the darker color.</summary>
        Darken,
        /// <summary>Keep the lighter color.</summary>
        Lighten,
        /// <summary>Brighten the backdrop.</summary>
        ColorDodge,
        /// <summary>Darken the backdrop.</summary>
        ColorBurn,
        /// <summary>Multiply or screen according to the source.</summary>
        HardLight,
        /// <summary>Apply a soft lighting curve.</summary>
        SoftLight,
        /// <summary>Multiply or screen according to the backdrop.</summary>
        Overlay,
        /// <summary>Absolute channel difference.</summary>
        Difference,
        /// <summary>A lower-contrast difference.</summary>
        Exclusion
    }

    internal static class PixelCompositor
    {
        internal static void Blend(Span<byte> pixels, int offset, Rgba32 source, ColorBlendMode mode)
        {
            if (source.A == 0)
            {
                return;
            }

            float sourceAlpha = source.A / 255f;
            float backdropAlpha = pixels[offset + 3] / 255f;
            float alpha = sourceAlpha + (backdropAlpha * (1 - sourceAlpha));
            pixels[offset] = Channel(source.R, pixels[offset], sourceAlpha, backdropAlpha, alpha, mode);
            pixels[offset + 1] = Channel(source.G, pixels[offset + 1], sourceAlpha, backdropAlpha, alpha, mode);
            pixels[offset + 2] = Channel(source.B, pixels[offset + 2], sourceAlpha, backdropAlpha, alpha, mode);
            pixels[offset + 3] = Byte(alpha);
        }

        private static byte Channel(byte sourceByte, byte backdropByte, float sourceAlpha, float backdropAlpha, float alpha, ColorBlendMode mode)
        {
            float source = sourceByte / 255f;
            float backdrop = backdropByte / 255f;
            float mixed = ((1 - backdropAlpha) * source) + (backdropAlpha * BlendChannel(source, backdrop, mode));
            return Byte(((sourceAlpha * mixed) + ((1 - sourceAlpha) * backdropAlpha * backdrop)) / alpha);
        }

        private static float BlendChannel(float source, float backdrop, ColorBlendMode mode)
        {
            return mode switch
            {
                ColorBlendMode.Multiply => source * backdrop,
                ColorBlendMode.Screen => (source + backdrop) - (source * backdrop),
                ColorBlendMode.Darken => Math.Min(source, backdrop),
                ColorBlendMode.Lighten => Math.Max(source, backdrop),
                ColorBlendMode.ColorDodge => Dodge(source, backdrop),
                ColorBlendMode.ColorBurn => Burn(source, backdrop),
                ColorBlendMode.HardLight => HardLight(source, backdrop),
                ColorBlendMode.Overlay => HardLight(backdrop, source),
                ColorBlendMode.SoftLight => SoftLight(source, backdrop),
                ColorBlendMode.Difference => Math.Abs(backdrop - source),
                ColorBlendMode.Exclusion => (backdrop + source) - (2 * backdrop * source),
                _ => source
            };
        }

        private static float Dodge(float source, float backdrop) => backdrop == 0 ? 0 : source >= 1 ? 1 : Math.Min(1, backdrop / (1 - source));
        private static float Burn(float source, float backdrop) => backdrop >= 1 ? 1 : source == 0 ? 0 : 1 - Math.Min(1, (1 - backdrop) / source);

        private static float HardLight(float source, float backdrop) => source <= 0.5f
            ? 2 * source * backdrop : 1 - (2 * (1 - source) * (1 - backdrop));

        private static float SoftLight(float source, float backdrop)
        {
            float curve = backdrop <= 0.25f ? ((((16 * backdrop) - 12) * backdrop) + 4) * backdrop : MathF.Sqrt(backdrop);
            return source <= 0.5f ? backdrop - ((1 - (2 * source)) * backdrop * (1 - backdrop))
                : backdrop + (((2 * source) - 1) * (curve - backdrop));
        }

        internal static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    }
}
