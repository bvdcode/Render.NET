using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal partial class SvgSceneBuilder
    {
        private SvgRegion Region(XElement e, string units, string x = "-10%", string y = "-10%", string width = "120%", string height = "120%") => new((string?)e.Attribute("x") ?? x, (string?)e.Attribute("y") ?? y, (string?)e.Attribute("width") ?? width, (string?)e.Attribute("height") ?? height, (string?)e.Attribute(units) != "userSpaceOnUse", _width, _height);
        private SvgMask? Mask(string value, int depth)
        {
            if (value == "none")
            {
                return null;
            }

            XElement? target = Reference(Url(value));
            if (target is null)
            {
                return new SvgMask(Empty, new SvgRegion("0", "0", "0", "0", true, _width, _height), false, false, false);
            }

            if (target.Name.LocalName != "mask")
            {
                Warn("Non-mask masking reference");
                return null;
            }

            Dictionary<string, string> style = Style(target, new Dictionary<string, string>());
            return new SvgMask(Node(target, new Dictionary<string, string>(), depth + 1, true), Region(target, "maskUnits"), (string?)target.Attribute("maskContentUnits") == "objectBoundingBox", Value(style, "mask-type", "luminance") != "alpha", Value(style, "color-interpolation", "sRGB") == "linearRGB");
        }

        private SvgFilter? Filter(string value, Dictionary<string, string> inherited, int depth)
        {
            if (value == "none")
            {
                return null;
            }

            if (!value.StartsWith("url(", StringComparison.Ordinal))
            {
                return CssFilter(value);
            }

            XElement? target = Reference(Url(value));
            if (target is null)
            {
                return null;
            }

            if (target.Name.LocalName != "filter")
            {
                Warn("Non-filter effect reference");
                return null;
            }

            XElement[] operations = target.Elements().Select(p => new XElement(p)).ToArray();
            if (operations.Length > 128)
            {
                throw new InvalidDataException("SVG filter exceeds 128 primitives.");
            }

            Dictionary<string, string> style = Style(target, inherited);
            var images = new Dictionary<XElement, SvgNode>();
            foreach (XElement operation in operations)
            {
                if (!SvgFilter.Operations.Contains(operation.Name.LocalName))
                {
                    Warn("SVG filter primitive: " + operation.Name.LocalName);
                }

                Dictionary<string, string> properties = Style(operation, style);
                if (operation.Name.LocalName == "feImage")
                {
                    if (FilterImage(operation, depth) is { } image)
                    {
                        images[operation] = image;
                    }
                }

                ApplyFilterProperties(operation, properties);
            }

            return new SvgFilter(Region(target, "filterUnits"), (string?)target.Attribute("primitiveUnits") == "objectBoundingBox", operations, Value(style, "color-interpolation-filters", "linearRGB") != "sRGB")
            {
                Images = images
            };
        }

        private static void ApplyFilterProperties(XElement operation, Dictionary<string, string> properties)
        {
            foreach (string name in new[]
            {
                "flood-color",
                "flood-opacity",
                "lighting-color",
                "color-interpolation-filters"
            })
            {
                if (properties.TryGetValue(name, out string? property))
                {
                    operation.SetAttributeValue(name, property == "currentColor" ? Value(properties, "color", "black") : property);
                }
            }
        }

        private SvgNode? FilterImage(XElement operation, int depth)
        {
            string? href = Href(operation);
            if (href?.StartsWith('#') == true)
            {
                XElement? referenced = Reference(href);
                return referenced is null ? null : Node(referenced, new Dictionary<string, string>(), depth + 1, true);
            }

            return Image(href) is { } image ? Empty with
            {
                Image = image
            }

            : null;
        }

        private ColorBlendMode Blend(string name)
        {
            if (Enum.TryParse(name.Replace("-", ""), true, out ColorBlendMode mode))
            {
                return mode;
            }

            Warn("SVG blend mode: " + name);
            return ColorBlendMode.Normal;
        }

        private SvgFilter? CssFilter(string value)
        {
            var operations = new List<XElement>();
            int position = 0;
            while (position < value.Length)
            {
                if (operations.Count >= 128)
                {
                    throw new InvalidDataException("CSS filter exceeds 128 operations.");
                }

                int start = value.IndexOf('(', position), end = start < 0 ? -1 : value.IndexOf(')', start);
                if (end < 0)
                {
                    Warn("SVG CSS filter syntax");
                    return null;
                }

                string name = value[position..start].Trim(), argument = value[(start + 1)..end].Trim();
                XElement? operation = SvgFilter.CssOperation(name, argument);
                if (operation is null)
                {
                    Warn("SVG CSS filter: " + name);
                }
                else
                {
                    operations.Add(operation);
                }

                position = end + 1;
            }

            return new SvgFilter(new SvgRegion("-100%", "-100%", "300%", "300%", true, _width, _height), false, operations.ToArray(), false)
            {
                Unbounded = true
            };
        }
    }
}
