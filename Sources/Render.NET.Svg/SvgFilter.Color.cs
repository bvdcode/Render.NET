using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal sealed partial record SvgFilter
    {
        private static PixelBuffer ColorMatrix(XElement p, PixelBuffer input, bool linear, SvgRenderContext context)
        {
            string type = Text(p, "type", "matrix");
            float[] numbers = SvgNumbers.List(Text(p, "values", ""));
            float value = numbers.Length > 0 ? numbers[0] : 1;
            float[] m = type switch
            {
                "saturate" => new[]
                {
                    .213f + (.787f * value),
                    .715f - (.715f * value),
                    .072f - (.072f * value),
                    0,
                    0,
                    .213f - (.213f * value),
                    .715f + (.285f * value),
                    .072f - (.072f * value),
                    0,
                    0,
                    .213f - (.213f * value),
                    .715f - (.715f * value),
                    .072f + (.928f * value),
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0
                },
                "hueRotate" => HueMatrix(value),
                "luminanceToAlpha" => new[]
                {
                    0f,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    .2125f,
                    .7154f,
                    .0721f,
                    0,
                    0
                },
                _ => numbers.Length == 0 ? new[]
                {
                    1f,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0
                }

                : numbers
            };
            if (m.Length != 20)
            {
                throw new InvalidDataException("SVG color matrix requires 20 values.");
            }

            return SvgPixels.Map(input, (x, y) =>
            {
                Vector4 c = SvgPixels.At(input, x, y, linear, premultiplied: false);
                float Row(int i) => (c.X * m[i]) + (c.Y * m[i + 1]) + (c.Z * m[i + 2]) + (c.W * m[i + 3]) + m[i + 4];
                return Vector4.Clamp(new Vector4(Row(0), Row(5), Row(10), Row(15)), Vector4.Zero, Vector4.One);
            }, linear, context.Token, false);
        }

        private static float[] HueMatrix(float angle)
        {
            float c = MathF.Cos((angle * MathF.PI) / 180), s = MathF.Sin((angle * MathF.PI) / 180);
            return new[]
            {
                (.213f + (.787f * c)) - (.213f * s),
                .715f - (.715f * c) - (.715f * s),
                (.072f - (.072f * c)) + (.928f * s),
                0,
                0,
                (.213f - (.213f * c)) + (.143f * s),
                .715f + (.285f * c) + (.140f * s),
                .072f - (.072f * c) - (.283f * s),
                0,
                0,
                .213f - (.213f * c) - (.787f * s),
                (.715f - (.715f * c)) + (.715f * s),
                .072f + (.928f * c) + (.072f * s),
                0,
                0,
                0,
                0,
                0,
                1,
                0
            };
        }

        private static PixelBuffer Transfer(XElement p, PixelBuffer input, bool linear, SvgRenderContext context)
        {
            Func<float, float>[] functions = "RGBA".Select(c => TransferFunction(p.Elements().LastOrDefault(e => e.Name.LocalName == "feFunc" + c))).ToArray();
            return SvgPixels.Map(input, (x, y) =>
            {
                Vector4 c = SvgPixels.At(input, x, y, linear, premultiplied: false);
                return new Vector4(functions[0](c.X), functions[1](c.Y), functions[2](c.Z), functions[3](c.W));
            }, linear, context.Token, false);
        }

        private static Func<float, float> TransferFunction(XElement? function)
        {
            if (function == null)
            {
                return x => x;
            }

            float[] table = SvgNumbers.List(Text(function, "tableValues"));
            string type = Text(function, "type", "identity");
            return type switch
            {
                "linear" => x => (Number(function, "slope", 1) * x) + Number(function, "intercept"),
                "gamma" => x => (Number(function, "amplitude", 1) * MathF.Pow(x, Number(function, "exponent", 1))) + Number(function, "offset"),
                "discrete" when table.Length > 0 => x => table[Math.Min(table.Length - 1, (int)(x * table.Length))],
                "table" when table.Length > 1 => x => TableValue(table, x),
                "table" when table.Length == 1 => _ => table[0],
                _ => x => x
            };
        }

        private static float TableValue(float[] table, float x)
        {
            float position = x * (table.Length - 1);
            int i = Math.Min(table.Length - 2, (int)position);
            return table[i] + ((table[i + 1] - table[i]) * (position - i));
        }

        private static PixelBuffer Composite(XElement p, PixelBuffer input, PixelBuffer second, bool linear, SvgRenderContext context)
        {
            string operation = Text(p, "operator", "over");
            var coefficients = new Vector4(Number(p, "k1"), Number(p, "k2"), Number(p, "k3"), Number(p, "k4"));
            return SvgPixels.Map(input, (x, y) =>
            {
                Vector4 a = SvgPixels.At(input, x, y, linear), b = SvgPixels.At(second, x, y, linear);
                return operation switch
                {
                    "in" => a * b.W,
                    "out" => a * (1 - b.W),
                    "atop" => (a * b.W) + (b * (1 - a.W)),
                    "xor" => (a * (1 - b.W)) + (b * (1 - a.W)),
                    "arithmetic" => Arithmetic(a, b, coefficients),
                    "lighter" => Vector4.Min(a + b, Vector4.One),
                    _ => a + (b * (1 - a.W))};
            }, linear, context.Token);
        }

        private static Vector4 Arithmetic(Vector4 a, Vector4 b, Vector4 k)
        {
            Vector4 c = Vector4.Clamp((k.X * a * b) + (k.Y * a) + (k.Z * b) + new Vector4(k.W), Vector4.Zero, Vector4.One);
            return Vector4.Min(c, new Vector4(c.W));
        }

        private static PixelBuffer BlendPixels(XElement p, PixelBuffer input, PixelBuffer second, bool linear, SvgRenderContext context)
        {
            string name = Text(p, "mode", "normal").Replace("-", "");
            if (!Enum.TryParse(name, true, out ColorBlendMode mode))
            {
                throw new NotSupportedException("SVG filter blend mode: " + name);
            }

            var output = SvgPixels.Copy(second);
            for (int y = 0; y < input.Height; y++)
            {
                context.Token.ThrowIfCancellationRequested();
                for (int x = 0; x < input.Width; x++)
                {
                    int i = ((y * input.Width) + x) * 4;
                    Rgba32 color = input[x, y];
                    if (linear)
                    {
                        Vector4 a = SvgPixels.At(input, x, y, true, premultiplied: false), b = SvgPixels.At(second, x, y, true, premultiplied: false);
                        SvgPixels.Write(output.Pixels.Span, i, b, false, false);
                        color = new Rgba32(PixelCompositor.Byte(a.X), PixelCompositor.Byte(a.Y), PixelCompositor.Byte(a.Z), PixelCompositor.Byte(a.W));
                    }

                    PixelCompositor.Blend(output.Pixels.Span, i, color, mode);
                    if (linear)
                    {
                        Vector4 c = SvgPixels.Read(output.Pixels.Span, i, false, false);
                        SvgPixels.Write(output.Pixels.Span, i, c, true, false);
                    }
                }
            }

            return output;
        }

        private static PixelBuffer Merge(XElement p, PixelBuffer previous, Dictionary<string, PixelBuffer> results, PixelBuffer source, SvgRenderContext context)
        {
            var output = new PixelBuffer(source.Width, source.Height);
            var canvas = new RasterCanvas(output);
            foreach (XElement child in p.Elements().Where(e => e.Name.LocalName == "feMergeNode"))
            {
                canvas.DrawImage(Input(Text(child, "in"), previous, results, source, context), Matrix3x2.Identity, cancellationToken: context.Token);
            }

            return output;
        }

        private static PixelBuffer Flood(XElement p, PixelBuffer source)
        {
            Rgba32 color = SvgColor.Parse(Text(p, "flood-color", "black"));
            var result = new PixelBuffer(source.Width, source.Height);
            result.Clear(color with { A = (byte)MathF.Round(color.A * Math.Clamp(Number(p, "flood-opacity", 1), 0, 1)) });
            return result;
        }

        private static PixelBuffer DropShadow(XElement p, PixelBuffer input, Matrix3x2 units, bool linear, SvgRenderContext context)
        {
            PixelBuffer alpha = SvgPixels.Map(input, (x, y) => new Vector4(0, 0, 0, input[x, y].A / 255f), false, context.Token);
            PixelBuffer blur = Gaussian(alpha, Pair(p, "stdDeviation", 2), units, linear, "none", context);
            PixelBuffer offset = Offset(blur, Vector2.TransformNormal(new Vector2(Number(p, "dx", 2), Number(p, "dy", 2)), units), linear, context);
            Rgba32 color = SvgColor.Parse(Text(p, "flood-color", "black"));
            float opacity = Number(p, "flood-opacity", 1);
            PixelBuffer result = SvgPixels.Map(input, (x, y) => new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, (offset[x, y].A / 255f) * opacity * (color.A / 255f)), false, context.Token, false);
            new RasterCanvas(result).DrawImage(input, Matrix3x2.Identity, cancellationToken: context.Token);
            return result;
        }
    }
}
