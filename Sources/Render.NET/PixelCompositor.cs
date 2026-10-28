using System;
using System.Numerics;

namespace Render.NET
{

    internal static class PixelCompositor
    {
        internal static void Blend(Span<byte> pixels, int offset, Rgba32 source, ColorBlendMode mode)
        {
            if (source.A == 0)
            {
                return;
            }

            if (mode == ColorBlendMode.Add)
            {
                float sa = source.A / 255f, ba = pixels[offset + 3] / 255f, total = Math.Min(1, sa + ba);
                pixels[offset] = Byte(Math.Min(1, ((source.R / 255f) * sa) + ((pixels[offset] / 255f) * ba)) / total);
                pixels[offset + 1] = Byte(Math.Min(1, ((source.G / 255f) * sa) + ((pixels[offset + 1] / 255f) * ba)) / total);
                pixels[offset + 2] = Byte(Math.Min(1, ((source.B / 255f) * sa) + ((pixels[offset + 2] / 255f) * ba)) / total);
                pixels[offset + 3] = Byte(total);
                return;
            }

            float sourceAlpha = source.A / 255f;
            float backdropAlpha = pixels[offset + 3] / 255f;
            float alpha = sourceAlpha + (backdropAlpha * (1 - sourceAlpha));
            Vector3 mixed = Mix(source, pixels, offset, mode);
            pixels[offset] = Channel(source.R, pixels[offset], mixed.X, sourceAlpha, backdropAlpha, alpha);
            pixels[offset + 1] = Channel(source.G, pixels[offset + 1], mixed.Y, sourceAlpha, backdropAlpha, alpha);
            pixels[offset + 2] = Channel(source.B, pixels[offset + 2], mixed.Z, sourceAlpha, backdropAlpha, alpha);
            pixels[offset + 3] = Byte(alpha);
        }

        private static byte Channel(byte sourceByte, byte backdropByte, float blended, float sourceAlpha, float backdropAlpha, float alpha)
        {
            float source = sourceByte / 255f;
            float backdrop = backdropByte / 255f;
            float mixed = ((1 - backdropAlpha) * source) + (backdropAlpha * blended);
            return Byte(((sourceAlpha * mixed) + ((1 - sourceAlpha) * backdropAlpha * backdrop)) / alpha);
        }

        private static Vector3 Mix(Rgba32 source, Span<byte> pixels, int offset, ColorBlendMode mode)
        {
            Vector3 s = new Vector3(source.R, source.G, source.B) / 255;
            Vector3 b = new Vector3(pixels[offset], pixels[offset + 1], pixels[offset + 2]) / 255;
            return mode switch
            {
                ColorBlendMode.Hue => SetLuminosity(SetSaturation(s, SaturationOf(b)), LuminosityOf(b)),
                ColorBlendMode.Saturation => SetLuminosity(SetSaturation(b, SaturationOf(s)), LuminosityOf(b)),
                ColorBlendMode.Color => SetLuminosity(s, LuminosityOf(b)),
                ColorBlendMode.Luminosity => SetLuminosity(b, LuminosityOf(s)),
                _ => new Vector3(BlendChannel(s.X, b.X, mode), BlendChannel(s.Y, b.Y, mode), BlendChannel(s.Z, b.Z, mode))
            };
        }

        private static float LuminosityOf(Vector3 c) => (0.3f * c.X) + (0.59f * c.Y) + (0.11f * c.Z);
        private static float Minimum(Vector3 c) => Math.Min(c.X, Math.Min(c.Y, c.Z));
        private static float Maximum(Vector3 c) => Math.Max(c.X, Math.Max(c.Y, c.Z));
        private static float SaturationOf(Vector3 c) => Maximum(c) - Minimum(c);
        private static Vector3 SetSaturation(Vector3 c, float saturation)
        {
            float range = SaturationOf(c);
            return range == 0 ? Vector3.Zero : (c - new Vector3(Minimum(c))) * (saturation / range);
        }

        private static Vector3 SetLuminosity(Vector3 c, float luminosity)
        {
            c += new Vector3(luminosity - LuminosityOf(c));
            float minimum = Minimum(c);
            float maximum = Maximum(c);
            if (minimum < 0)
            {
                c = new Vector3(luminosity) + ((c - new Vector3(luminosity)) * (luminosity / (luminosity - minimum)));
            }

            if (maximum > 1)
            {
                c = new Vector3(luminosity) + ((c - new Vector3(luminosity)) * ((1 - luminosity) / (maximum - luminosity)));
            }

            return c;
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
                ColorBlendMode.HardMix => (source <= .5f ? Burn(2 * source, backdrop) : Dodge((2 * source) - 1, backdrop)) < .5f ? 0 : 1,
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
