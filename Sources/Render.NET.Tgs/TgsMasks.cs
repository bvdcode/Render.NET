using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;

namespace Render.NET.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private void Mask(JsonElement layer, XElement content, double frame)
        {
            JsonElement[] masks = TgsJson.Array(TgsJson.Get(layer, "masksProperties")).Where(m => TgsJson.Text(m, "mode", "a") != "n").ToArray();
            if (masks.Length == 0)
            {
                return;
            }

            string filterId = Id();
            XElement filter = FullFilter(filterId);
            string? current = null;
            for (int i = 0; i < masks.Length; i++)
            {
                Count(0);
                JsonElement mask = masks[i];
                VectorPath path = Bezier(TgsJson.Get(mask, "pt"), frame);
                Geometry(path);
                string pathId = Id(), next = "mask" + i;
                XElement drawing = PathElement(path);
                drawing.SetAttributeValue("id", pathId);
                drawing.SetAttributeValue("fill", "white");
                drawing.SetAttributeValue("opacity", F(Math.Clamp(TgsValues.Scalar(TgsJson.Get(mask, "o"), frame, 100) / 100, 0, 1)));
                _defs.Add(drawing);
                filter.Add(new XElement("feImage", new XAttribute("href", "#" + pathId)));
                float expansion = (float)TgsValues.Scalar(TgsJson.Get(mask, "x"), frame);
                if (expansion != 0)
                {
                    filter.Add(new XElement("feMorphology", new XAttribute("operator", expansion > 0 ? "dilate" : "erode"), new XAttribute("radius", F(Math.Abs(expansion)))));
                }

                Vector2 feather = TgsValues.Point(TgsJson.Get(mask, "f"), frame);
                if (feather != Vector2.Zero)
                {
                    filter.Add(new XElement("feGaussianBlur", new XAttribute("stdDeviation", F(Math.Max(0, feather.X) / 2) + " " + F(Math.Max(0, feather.Y) / 2))));
                }

                filter.Add(new XElement("feColorMatrix", new XAttribute("values", "0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 0 1"), new XAttribute("result", next)));
                bool inverse = TgsJson.Flag(mask, "inv");
                string mode = TgsJson.Text(mask, "mode", "a");
                if (inverse ^ (mode == "s"))
                {
                    filter.Add(InvertRgb(next));
                }

                if (current is null)
                {
                    current = next;
                    continue;
                }

                string operation = mode switch
                {
                    "s" or "i" => "multiply",
                    "f" => "difference",
                    "l" => "lighten",
                    "d" => "darken",
                    _ => "screen"
                };
                filter.Add(new XElement("feBlend", new XAttribute("in", current), new XAttribute("in2", next), new XAttribute("mode", operation), new XAttribute("result", next)));
                current = next;
            }

            _defs.Add(filter);
            string maskId = Id();
            XElement result = FullMask(maskId, "luminance");
            result.Add(new XElement("g", new XAttribute("filter", "url(#" + filterId + ")")));
            _defs.Add(result);
            content.SetAttributeValue("mask", "url(#" + maskId + ")");
        }

        private string Matte(XElement drawing, int type)
        {
            string mask = Id();
            XElement normal = FullMask(mask, type is 3 or 4 ? "luminance" : "alpha");
            normal.Add(drawing);
            _defs.Add(normal);
            if (type is not 2 and not 4)
            {
                return mask;
            }

            string inverted = Id(), effect = Id();
            XElement filter = FullFilter(effect);
            filter.Add(new XElement("feComponentTransfer", new XElement("feFuncA", new XAttribute("type", "linear"), new XAttribute("slope", "-1"), new XAttribute("intercept", "1"))));
            _defs.Add(filter);
            XElement alpha = FullMask(inverted, "alpha");
            alpha.Add(new XElement("g", new XAttribute("filter", "url(#" + effect + ")"), new XElement("rect", new XAttribute("x", "0"), new XAttribute("y", "0"), new XAttribute("width", _document.Width), new XAttribute("height", _document.Height), new XAttribute("fill", "white"), new XAttribute("mask", "url(#" + mask + ")"))));
            _defs.Add(alpha);
            return inverted;
        }

        private XElement FullMask(string id, string type) => new("mask", new XAttribute("id", id), new XAttribute("mask-type", type), new XAttribute("maskUnits", "userSpaceOnUse"), new XAttribute("x", -_document.Width * 8), new XAttribute("y", -_document.Height * 8), new XAttribute("width", _document.Width * 17), new XAttribute("height", _document.Height * 17));
        private XElement FullFilter(string id) => new("filter", new XAttribute("id", id), new XAttribute("filterUnits", "userSpaceOnUse"), new XAttribute("color-interpolation-filters", "sRGB"), new XAttribute("x", -_document.Width * 8), new XAttribute("y", -_document.Height * 8), new XAttribute("width", _document.Width * 17), new XAttribute("height", _document.Height * 17));
        private static XElement InvertRgb(string result) => new("feComponentTransfer", new XAttribute("result", result), "RGB".Select(c => new XElement("feFunc" + c, new XAttribute("type", "linear"), new XAttribute("slope", "-1"), new XAttribute("intercept", "1"))));
        private static readonly string?[] BlendNames =
        {
            null,
            "multiply",
            "screen",
            "overlay",
            "darken",
            "lighten",
            "color-dodge",
            "color-burn",
            "hard-light",
            "soft-light",
            "difference",
            "exclusion",
            "hue",
            "saturation",
            "color",
            "luminosity",
            "add",
            "hard-mix"
        };
        private static string? BlendMode(int mode) => (uint)mode < BlendNames.Length ? BlendNames[mode] : null;
    }
}
