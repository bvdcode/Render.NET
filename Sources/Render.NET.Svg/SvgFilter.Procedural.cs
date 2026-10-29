using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal partial record SvgFilter
    {
        private static PixelBuffer Turbulence(XElement p, PixelBuffer source, Matrix3x2 matrix, (float X, float Y, float Width, float Height) region, bool linear, SvgRenderContext context)
        {
            Vector2 frequency = Pair(p, "baseFrequency");
            int octaves = (int)Number(p, "numOctaves", 1);
            if (frequency.X < 0 || frequency.Y < 0 || octaves < 0 || octaves > 12)
            {
                throw new InvalidDataException("Invalid SVG turbulence frequency/octaves (maximum 12).");
            }

            SvgNoise? noise = new SvgNoise((long)Number(p, "seed"));
            bool fractal = Text(p, "type", "turbulence") == "fractalNoise";
            bool stitch = Text(p, "stitchTiles", "noStitch") == "stitch";
            if (stitch)
            {
                frequency.X = StitchFrequency(frequency.X, region.Width);
                frequency.Y = StitchFrequency(frequency.Y, region.Height);
            }

            if (!Matrix3x2.Invert(matrix, out Matrix3x2 inverse))
            {
                return context.CreateBuffer(source.Width, source.Height);
            }

            SvgNoiseOctave[] levels = new SvgNoiseOctave[octaves];
            float amplitude = 1;
            for (int octave = 0; octave < octaves; octave++)
            {
                levels[octave] = new SvgNoiseOctave(frequency, amplitude,
                    stitch ? (int)MathF.Round(region.Width * frequency.X) : 0, stitch ? (int)MathF.Round(region.Height * frequency.Y) : 0,
                    (int)MathF.Floor(region.X * frequency.X), (int)MathF.Floor(region.Y * frequency.Y));
                frequency *= 2;
                amplitude *= .5f;
            }

            return SvgPixels.Map(source, (x, y) =>
            {
                Vector2 point = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse);
                Vector4 sum = Vector4.Zero;
                foreach (SvgNoiseOctave level in levels)
                {
                    Vector4 sample = noise.Sample(point.X * level.Frequency.X, point.Y * level.Frequency.Y, level.Width, level.Height, level.OriginX, level.OriginY);
                    sum += (fractal ? sample : Vector4.Abs(sample)) * level.Amplitude;
                }

                return fractal ? (sum + Vector4.One) / 2 : sum;
            }, linear, context, false, parallel: true);
        }

        private static float StitchFrequency(float value, float size)
        {
            if (value == 0 || size <= 0)
            {
                return value;
            }

            float low = MathF.Floor(size * value) / size, high = MathF.Ceiling(size * value) / size;
            return low > 0 && value / low < high / value ? low : high;
        }

        private static PixelBuffer Lighting(XElement p, PixelBuffer input, Matrix3x2 units, Matrix3x2 matrix, bool linear, SvgRenderContext context)
        {
            XElement? light = p.Elements().FirstOrDefault();
            if (light is null || !Matrix3x2.Invert(matrix, out Matrix3x2 inverse))
            {
                return context.CreateBuffer(input.Width, input.Height);
            }

            bool specular = p.Name.LocalName == "feSpecularLighting";
            Rgba32 ink = SvgColor.Parse(Text(p, "lighting-color", "white"));
            Vector3 color = new(ink.R / 255f, ink.G / 255f, ink.B / 255f);
            if (linear)
            {
                color = new Vector3(SvgPixels.Linear(color.X), SvgPixels.Linear(color.Y), SvgPixels.Linear(color.Z));
            }

            float surface = Number(p, "surfaceScale", 1), constant = Number(p, specular ? "specularConstant" : "diffuseConstant", 1), exponent = Number(p, "specularExponent", 1);
            Vector2 step = Pair(p, "kernelUnitLength", 1);
            Vector2 dx = Vector2.TransformNormal(new Vector2(step.X, 0), units), dy = Vector2.TransformNormal(new Vector2(0, step.Y), units);
            float Height(float x, float y) => SvgPixels.Sample(input, x, y, false, "duplicate").W * surface;
            return SvgPixels.Map(input, (x, y) =>
            {
                float gx = 0, gy = 0;
                for (int i = -1; i <= 1; i++)
                {
                    float weight = i == 0 ? 2 : 1;
                    gx += weight * (Height(x + dx.X + (dy.X * i), y + dx.Y + (dy.Y * i)) - Height((x - dx.X) + (dy.X * i), (y - dx.Y) + (dy.Y * i)));
                    gy += weight * (Height(x + dy.X + (dx.X * i), y + dy.Y + (dx.Y * i)) - Height((x - dy.X) + (dx.X * i), (y - dy.Y) + (dx.Y * i)));
                }

                Vector3 normal = Vector3.Normalize(new Vector3(-gx / (8 * Math.Max(.001f, step.X)), -gy / (8 * Math.Max(.001f, step.Y)), 1));
                Vector2 local = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse);
                Vector3 position = new(local, Height(x, y));
                var (direction, attenuation) = Light(light, position);
                Vector3 half = direction + Vector3.UnitZ;
                float dot = specular ? half.LengthSquared() == 0 ? 0 : Vector3.Dot(normal, Vector3.Normalize(half)) : Vector3.Dot(normal, direction);
                float intensity = constant * attenuation * (specular ? MathF.Pow(Math.Max(0, dot), exponent) : Math.Max(0, dot));
                Vector3 rgb = Vector3.Clamp(color * intensity, Vector3.Zero, Vector3.One);
                return new Vector4(rgb, specular ? Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z)) : 1);
            }, linear, context, false);
        }

        private static (Vector3 Direction, float Attenuation) Light(XElement light, Vector3 position)
        {
            if (light.Name.LocalName == "feDistantLight")
            {
                float azimuth = (Number(light, "azimuth") * MathF.PI) / 180, elevation = (Number(light, "elevation") * MathF.PI) / 180;
                return (new Vector3(MathF.Cos(azimuth) * MathF.Cos(elevation), MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation)), 1);
            }

            Vector3 origin = new(Number(light, "x"), Number(light, "y"), Number(light, "z"));
            Vector3 vector = origin - position;
            Vector3 direction = vector.LengthSquared() == 0 ? Vector3.UnitZ : Vector3.Normalize(vector);
            if (light.Name.LocalName != "feSpotLight")
            {
                return (direction, 1);
            }

            Vector3 target = new(Number(light, "pointsAtX"), Number(light, "pointsAtY"), Number(light, "pointsAtZ"));
            Vector3 axis = target - origin;
            float cosine = axis.LengthSquared() == 0 ? 0 : Vector3.Dot(-direction, Vector3.Normalize(axis));
            float limit = (Number(light, "limitingConeAngle", 90) * MathF.PI) / 180;
            float attenuation = cosine <= 0 || cosine < MathF.Cos(limit) ? 0 : MathF.Pow(cosine, Number(light, "specularExponent", 1));
            return (direction, attenuation);
        }
    }
}
