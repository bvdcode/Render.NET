using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal partial class SvgSceneBuilder
    {
        private SvgPaint? Paint(string value, Dictionary<string, string> style)
        {
            if (value == "none")
            {
                return null;
            }

            if (value == "currentColor")
            {
                value = Value(style, "color", "black");
            }

            try
            {
                if (value.StartsWith("url(", StringComparison.Ordinal))
                {
                    XElement? e = Reference(Url(value));
                    if (e is null)
                    {
                        return null;
                    }

                    if (!_gradients.TryGetValue(e, out SvgGradient? gradient))
                    {
                        gradient = Gradient(e);
                        _gradients.Add(e, gradient);
                    }

                    return gradient;
                }

                return new SvgSolid(SvgColor.Parse(value));
            }
            catch (NotSupportedException ex)
            {
                Warn(ex.Message);
                return null;
            }
        }

        private SvgGradient? Gradient(XElement element)
        {
            bool radial = element.Name.LocalName == "radialGradient";
            if (!radial && element.Name.LocalName != "linearGradient")
            {
                Warn("SVG paint server: " + element.Name.LocalName);
                return null;
            }

            var attrs = new Dictionary<string, string>();
            XElement? stopsElement = GradientAttributes(element, attrs);
            (float Offset, Rgba32 Color)[]? stops = GradientStops(stopsElement);
            bool box = Value(attrs, "gradientUnits", "objectBoundingBox") != "userSpaceOnUse";
            float x = box ? 1 : _width, y = box ? 1 : _height;
            float diagonal = box ? 1 : Diagonal;
            float Get(string key, string fallback, float basis) => SvgLength.Parse(Value(attrs, key, fallback), basis);
            Vector2 start, end;
            if (radial)
            {
                end = new Vector2(Get("cx", "50%", x), Get("cy", "50%", y));
                start = new Vector2(Get("fx", Value(attrs, "cx", "50%"), x), Get("fy", Value(attrs, "cy", "50%"), y));
            }
            else
            {
                start = new Vector2(Get("x1", "0%", x), Get("y1", "0%", y));
                end = new Vector2(Get("x2", "100%", x), Get("y2", "0%", y));
            }

            float radius = Get("r", "50%", diagonal), focalRadius = Get("fr", "0", diagonal);
            if (radius < 0 || focalRadius < 0)
            {
                throw new InvalidDataException("Negative SVG gradient radius.");
            }

            if (radial)
            {
                focalRadius = Math.Min(focalRadius, radius);
                float max = Math.Max(0, radius - focalRadius) * 0.99999f;
                if (Vector2.Distance(start, end) > max)
                {
                    start = end + (Vector2.Normalize(start - end) * max);
                }
            }

            return new SvgGradient(radial, box, SvgNumbers.Transform(Value(attrs, "gradientTransform", "")), Value(attrs, "spreadMethod", "pad"), start, end, radius, focalRadius, stops)
            {
                Linear = Value(attrs, "color-interpolation", "sRGB") == "linearRGB"
            };
        }

        private XElement? GradientAttributes(XElement element, Dictionary<string, string> attrs)
        {
            XElement? stopsElement = null;
            var chain = new HashSet<XElement>();
            XElement? current = element;
            while (current is not null)
            {
                _token.ThrowIfCancellationRequested();
                if (!chain.Add(current) || chain.Count > _limits.MaximumDepth)
                {
                    throw new InvalidDataException("Cyclic/deep SVG gradient reference.");
                }

                foreach (XAttribute a in current.Attributes().Where(a => a.Name.NamespaceName.Length == 0))
                {
                    attrs.TryAdd(a.Name.LocalName, a.Value);
                }

                if (stopsElement is null && current.Elements().Any(x => x.Name.LocalName == "stop"))
                {
                    stopsElement = current;
                }

                string? href = Href(current);
                current = href is null ? null : Reference(href);
            }

            return stopsElement;
        }

        private (float Offset, Rgba32 Color)[] GradientStops(XElement? stopsElement)
        {
            var stops = new List<(float Offset, Rgba32 Color)>();
            if (stopsElement is not null)
            {
                var inherited = new Dictionary<string, string>();
                foreach (XElement ancestor in stopsElement.AncestorsAndSelf().Reverse())
                {
                    inherited = Style(ancestor, inherited);
                }

                foreach (XElement stop in stopsElement.Elements().Where(x => x.Name.LocalName == "stop"))
                {
                    Dictionary<string, string> style = Style(stop, inherited);
                    float offset = Math.Clamp(SvgLength.Parse((string?)stop.Attribute("offset") ?? "0"), stops.Count == 0 ? 0 : stops[^1].Offset, 1);
                    string ink = Value(style, "stop-color", "black");
                    Rgba32 color = SvgColor.Parse(ink == "currentColor" ? Value(style, "color", "black") : ink);
                    stops.Add((offset, SvgColor.Alpha(color, Opacity(style, "stop-opacity"))));
                }
            }

            return stops.ToArray();
        }
    }
}
