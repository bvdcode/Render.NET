using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal sealed partial record SvgFilter(SvgRegion Region, bool PrimitiveBox, XElement[] Primitives, bool Linear)
    {
        internal IReadOnlyDictionary<XElement, SvgNode> Images { get; init; } = new Dictionary<XElement, SvgNode>();
        internal bool Unbounded { get; init; }

        internal static readonly HashSet<string> Operations = new("feGaussianBlur feOffset feColorMatrix feComponentTransfer feComposite feBlend feMerge feFlood feTile feMorphology feConvolveMatrix feDisplacementMap feTurbulence feDiffuseLighting feSpecularLighting feDropShadow feImage".Split(' '));
        internal PixelBuffer Apply(PixelBuffer source, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, SvgRenderContext context, PixelBuffer background, SvgPaint? fill, SvgPaint? stroke, float fillOpacity, float strokeOpacity)
        {
            int named = Primitives.Select(p => Text(p, "result")).Where(n => n.Length > 0).Distinct().Count();
            using IDisposable reservation = context.Reserve(source.Width, source.Height, named + 8);
            var results = new Dictionary<string, PixelBuffer>(StringComparer.Ordinal)
            {
                ["SourceGraphic"] = source
            };
            PrepareInputs(results, background, fill, stroke, fillOpacity, strokeOpacity, box, matrix, source, context);
            var regions = new Dictionary<PixelBuffer, (float X, float Y, float Width, float Height)>
            {
                [source] = Region.Path(box).GetBounds(context.Token)
            };
            PixelBuffer previous = source;
            Matrix3x2 units = PrimitiveBox ? Matrix3x2.CreateScale(box.Width, box.Height) * Matrix3x2.CreateTranslation(box.X, box.Y) * matrix : matrix;
            foreach (XElement primitive in Primitives)
            {
                context.Token.ThrowIfCancellationRequested();
                PixelBuffer input = Input(Text(primitive, "in"), previous, results, source, context);
                PixelBuffer second = Input(Text(primitive, "in2", "SourceGraphic"), source, results, source, context);
                bool linear = Text(primitive, "color-interpolation-filters", Linear ? "linearRGB" : "sRGB") != "sRGB";
                var region = PrimitiveRegion(primitive, box, context);
                previous = Execute(primitive, input, second, results, source, units, matrix, region, regions.GetValueOrDefault(input, Region.Path(box).GetBounds(context.Token)), linear, context);
                ClipPrimitive(previous, region, matrix, context);
                regions[previous] = region;
                string name = Text(primitive, "result");
                if (name.Length > 0 && name != "SourceGraphic" && name != "SourceAlpha")
                {
                    results[name] = previous;
                }
            }

            if (Primitives.Length == 0)
            {
                previous = new PixelBuffer(source.Width, source.Height);
            }

            if (Unbounded)
            {
                return previous;
            }

            var clipped = new PixelBuffer(source.Width, source.Height);
            var canvas = new RasterCanvas(clipped);
            canvas.ClipPath(Region.Path(box), matrix, cancellationToken: context.Token);
            canvas.DrawImage(previous, Matrix3x2.Identity, cancellationToken: context.Token);
            return clipped;
        }

        private static PixelBuffer Input(string name, PixelBuffer fallback, Dictionary<string, PixelBuffer> results, PixelBuffer source, SvgRenderContext context)
        {
            if (name.Length == 0)
            {
                return fallback;
            }

            if (results.TryGetValue(name, out PixelBuffer? found))
            {
                return found;
            }

            if (name == "SourceAlpha")
            {
                PixelBuffer alpha = SvgPixels.Map(source, (x, y) => new Vector4(0, 0, 0, source[x, y].A / 255f), false, context.Token);
                results[name] = alpha;
                return alpha;
            }

            return new PixelBuffer(source.Width, source.Height);
        }

        private PixelBuffer Execute(XElement p, PixelBuffer input, PixelBuffer second, Dictionary<string, PixelBuffer> results, PixelBuffer source, Matrix3x2 units, Matrix3x2 matrix, (float X, float Y, float Width, float Height) region, (float X, float Y, float Width, float Height) inputRegion, bool linear, SvgRenderContext context) => ColorOperations.Contains(p.Name.LocalName) ? ExecuteColor(p, input, second, results, source, linear, context) : p.Name.LocalName switch
        {
            "feGaussianBlur" => Gaussian(input, Pair(p, "stdDeviation"), units, linear, Text(p, "edgeMode", "none"), context),
            "feOffset" => Offset(input, Vector2.TransformNormal(new Vector2(Number(p, "dx"), Number(p, "dy")), units), linear, context),
            "feDropShadow" => DropShadow(p, input, units, linear, context),
            "feMorphology" => Morphology(p, input, units, linear, context),
            "feConvolveMatrix" => Convolve(p, input, units, linear, context),
            "feDisplacementMap" => Displace(p, input, second, units, linear, context),
            "feTurbulence" => Turbulence(p, source, matrix, region, linear, context),
            "feDiffuseLighting" or "feSpecularLighting" => Lighting(p, input, units, matrix, linear, context),
            "feTile" => Tile(input, inputRegion, matrix, linear, context),
            "feImage" => FilterImage(p, source, units, matrix, region, context),
            _ => SvgPixels.Copy(input)};
        private static readonly HashSet<string> ColorOperations = new("feColorMatrix feComponentTransfer feComposite feBlend feMerge feFlood".Split(' '));
        private static PixelBuffer ExecuteColor(XElement p, PixelBuffer input, PixelBuffer second, Dictionary<string, PixelBuffer> results, PixelBuffer source, bool linear, SvgRenderContext context) => p.Name.LocalName switch
        {
            "feColorMatrix" => ColorMatrix(p, input, linear, context),
            "feComponentTransfer" => Transfer(p, input, linear, context),
            "feComposite" => Composite(p, input, second, linear, context),
            "feBlend" => BlendPixels(p, input, second, linear, context),
            "feMerge" => Merge(p, input, results, source, context),
            "feFlood" => Flood(p, source),
            _ => SvgPixels.Copy(input)};
        private (float X, float Y, float Width, float Height) PrimitiveRegion(XElement primitive, (float X, float Y, float Width, float Height) box, SvgRenderContext context)
        {
            var region = Region.Path(box).GetBounds(context.Token);
            float Get(string name, float fallback, float size, float origin, float viewport) => primitive.Attribute(name) is { } a ? PrimitiveBox ? (SvgLength.Parse(a.Value, 1) * size) + origin : SvgLength.Parse(a.Value, viewport) : fallback;
            return (Get("x", region.X, box.Width, box.X, Region.ViewWidth), Get("y", region.Y, box.Height, box.Y, Region.ViewHeight), Get("width", region.Width, box.Width, 0, Region.ViewWidth), Get("height", region.Height, box.Height, 0, Region.ViewHeight));
        }

        private static void Clip(PixelBuffer image, (float X, float Y, float Width, float Height) region, Matrix3x2 matrix, SvgRenderContext context)
        {
            var mask = new PixelBuffer(image.Width, image.Height);
            var path = region.Width <= 0 || region.Height <= 0 ? new VectorPath() : new VectorPath().Rectangle(region.X, region.Y, region.Width, region.Height);
            new RasterCanvas(mask).FillPath(path, new Rgba32(0, 0, 0), matrix, cancellationToken: context.Token);
            Span<byte> pixels = image.Pixels.Span;
            ReadOnlySpan<byte> clip = mask.Pixels.Span;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (clip[i] == 0)
                {
                    pixels[i] = 0;
                }
            }
        }

        private void ClipPrimitive(PixelBuffer image, (float X, float Y, float Width, float Height) region, Matrix3x2 matrix, SvgRenderContext context)
        {
            if (!Unbounded)
            {
                Clip(image, region, matrix, context);
            }
        }

        private void PrepareInputs(Dictionary<string, PixelBuffer> results, PixelBuffer background, SvgPaint? fill, SvgPaint? stroke, float fillOpacity, float strokeOpacity, (float X, float Y, float Width, float Height) box, Matrix3x2 matrix, PixelBuffer source, SvgRenderContext context)
        {
            HashSet<string> requested = Primitives.SelectMany(p => p.DescendantsAndSelf()).SelectMany(p => p.Attributes()).Where(a => a.Name.LocalName is "in" or "in2").Select(a => a.Value).ToHashSet(StringComparer.Ordinal);
            if (requested.Contains("BackgroundImage"))
            {
                results["BackgroundImage"] = background;
            }

            if (requested.Contains("BackgroundAlpha"))
            {
                results["BackgroundAlpha"] = SvgPixels.Map(source, (x, y) => new Vector4(0, 0, 0, background[x, y].A / 255f), false, context.Token);
            }

            void Paint(string name, SvgPaint? paint, float opacity)
            {
                if (requested.Contains(name))
                {
                    var buffer = new PixelBuffer(source.Width, source.Height);
                    paint?.DrawUnbounded(new RasterCanvas(buffer), box, matrix, opacity, context.Token);
                    results[name] = buffer;
                }
            }

            Paint("FillPaint", fill, fillOpacity);
            Paint("StrokePaint", stroke, strokeOpacity);
        }

        private PixelBuffer FilterImage(XElement p, PixelBuffer source, Matrix3x2 units, Matrix3x2 matrix, (float X, float Y, float Width, float Height) region, SvgRenderContext context)
        {
            var result = new PixelBuffer(source.Width, source.Height);
            if (!Images.TryGetValue(p, out SvgNode? node) || region.Width <= 0 || region.Height <= 0)
            {
                return result;
            }

            var canvas = new RasterCanvas(result);
            if (node.Image != null)
            {
                Matrix3x2 viewport = SvgRenderer.Viewport((0, 0, node.Image.Width, node.Image.Height), region.Width, region.Height, Text(p, "preserveAspectRatio", "xMidYMid meet")) * Matrix3x2.CreateTranslation(region.X, region.Y);
                canvas.DrawImage(node.Image, viewport * matrix, cancellationToken: context.Token);
            }
            else
            {
                node.Draw(canvas, units, context);
            }

            return result;
        }

        private static string Text(XElement p, string name, string fallback = "") => (string?)p.Attribute(name) ?? fallback;
        private static float Number(XElement p, string name, float fallback = 0) => p.Attribute(name) is { } a ? SvgLength.Parse(a.Value) : fallback;
        private static Vector2 Pair(XElement p, string name, float fallback = 0)
        {
            float[] values = p.Attribute(name) is { } a ? SvgNumbers.List(a.Value) : new[]
            {
                fallback
            };
            if (values.Length is < 1 or > 2)
            {
                throw new InvalidDataException("Expected one or two SVG filter numbers.");
            }

            return new Vector2(values[0], values.Length == 2 ? values[1] : values[0]);
        }

        private static readonly HashSet<string> CssTransferNames = new("brightness contrast opacity invert".Split(' '));

        internal static XElement? CssOperation(string name, string value)
        {
            if (!new[]
            {
                "blur",
                "hue-rotate",
                "saturate",
                "brightness",
                "contrast",
                "opacity",
                "invert",
                "grayscale",
                "sepia"
            }.Contains(name))
            {
                return null;
            }

            if (value.Length == 0)
            {
                value = name is "blur" or "hue-rotate" ? "0" : "1";
            }

            if (name == "blur")
            {
                return new XElement("feGaussianBlur", new XAttribute("stdDeviation", SvgLength.Parse(value)));
            }

            if (name == "hue-rotate")
            {
                return new XElement("feColorMatrix", new XAttribute("type", "hueRotate"), new XAttribute("values", CssAngle(value)));
            }

            float n = SvgLength.Parse(value);
            if (name == "saturate")
            {
                return new XElement("feColorMatrix", new XAttribute("type", "saturate"), new XAttribute("values", n));
            }

            if (CssTransferNames.Contains(name))
            {
                float slope = name == "invert" ? 1 - (2 * n) : n;
                float intercept = name == "contrast" ? (0.5f - (0.5f * n)) : name == "invert" ? n : 0;
                return new XElement("feComponentTransfer", (name == "opacity" ? new[] { 'A' } : new[] { 'R', 'G', 'B' }).Select(c => new XElement("feFunc" + c, new XAttribute("type", "linear"), new XAttribute("slope", slope), new XAttribute("intercept", intercept))));
            }

            return CssColorOperation(name, n);
        }

        private static float CssAngle(string value)
        {
            if (value.EndsWith("deg", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^3]);
            }

            if (value.EndsWith("rad", StringComparison.Ordinal))
            {
                return (SvgLength.Parse(value[..^3]) * 180) / MathF.PI;
            }

            if (value.EndsWith("turn", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^4]) * 360;
            }

            return SvgLength.Parse(value);
        }

        private static XElement? CssColorOperation(string name, float n)
        {
            if (name is "grayscale" or "sepia")
            {
                float[] m = name == "sepia" ? new[]
                {
                    .393f,
                    .769f,
                    .189f,
                    .349f,
                    .686f,
                    .168f,
                    .272f,
                    .534f,
                    .131f
                }

                : new[]
                {
                    .2126f,
                    .7152f,
                    .0722f,
                    .2126f,
                    .7152f,
                    .0722f,
                    .2126f,
                    .7152f,
                    .0722f
                };
                var values = new float[20];
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        values[(i * 5) + j] = (m[(i * 3) + j] * n) + (i == j ? 1 - n : 0);
                    }
                }

                values[18] = 1;
                return new XElement("feColorMatrix", new XAttribute("values", string.Join(" ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))));
            }

            return null;
        }
    }
}
