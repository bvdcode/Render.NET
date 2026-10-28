using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal partial class SvgSceneBuilder
    {
        private static float TextLength(string value, float size)
        {
            if (value is "normal" or "none")
            {
                return 0;
            }

            if (value.EndsWith("rem", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^3]) * 16;
            }

            if (value.EndsWith("em", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^2]) * size;
            }

            if (value.EndsWith("ex", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^2]) * size * .5f;
            }

            return SvgLength.Parse(value, size);
        }

        private static float[] TextPositions(XElement e, string name, float percent, float size)
        {
            if (e.Attribute(name) is not { } attribute)
            {
                return Array.Empty<float>();
            }

            string[] items = attribute.Value.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return items.Select(item => item.EndsWith('%') ? SvgLength.Parse(item, percent) : TextLength(item, size)).ToArray();
        }

        private static float Baseline(Dictionary<string, string> style, SvgTrueType font, float size)
        {
            string value = Value(style, "dominant-baseline", Value(style, "alignment-baseline", "auto"));
            float offset = value switch
            {
                "middle" => font.XHeight / 2,
                "central" => (font.Ascent + font.Descent) / 2,
                "hanging" => font.Ascent * .8f,
                "text-before-edge" or "text-top" => font.Ascent,
                "text-after-edge" or "text-bottom" => font.Descent,
                _ => 0
            };
            string shift = Value(style, "baseline-shift", "0");
            return ((offset * size) / font.Units) - (shift switch
            {
                "super" => size * .6f,
                "sub" => -size * .3f,
                "baseline" => 0,
                _ => TextLength(shift, size)
            });
        }

        private (SvgTrueType Font, int Glyph) TextFont(Dictionary<string, string> style, int character)
        {
            string family = Value(style, "font-family", "sans-serif");
            string weight = Value(style, "font-weight", "400");
            int numeric = weight is "bold" or "bolder" ? 700 : int.TryParse(weight, out int parsed) ? parsed : 400;
            SvgFontRequest? request = new SvgFontRequest(family, numeric, Value(style, "font-style", "normal") is "italic" or "oblique");
            SvgTrueType[] fonts = TextFonts(request);
            foreach (SvgTrueType font in fonts)
            {
                int glyph = font.Glyph(character);
                if (glyph != 0)
                {
                    return (font, glyph);
                }
            }

            Warn("Missing SVG font glyph U+" + character.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            return (fonts[0], 0);
        }

        private SvgTrueType[] TextFonts(SvgFontRequest request)
        {
            if (!_fonts.TryGetValue(request, out SvgTrueType[]? fonts))
            {
                var list = new List<SvgTrueType>();
                foreach (string candidate in request.Family.Split(','))
                {
                    ReadOnlyMemory<byte> custom = _limits.FontResolver?.Invoke(request with { Family = candidate.Trim().Trim('\'', '"') }) ?? default;
                    if (!custom.IsEmpty)
                    {
                        list.Add(new SvgTrueType(custom));
                    }
                }

                string variant = request.Weight >= 600 ? request.Italic ? "BoldItalic" : "Bold" : request.Italic ? "Italic" : "Regular";
                list.Add(BundledFont("Aileron-" + variant));
                list.Add(BundledFont("Canada1500-" + variant));
                fonts = list.ToArray();
                _fonts.Add(request, fonts);
            }

            return fonts;
        }

        private SvgTrueType BundledFont(string name)
        {
            if (_bundledFonts.TryGetValue(name, out SvgTrueType? cached))
            {
                return cached;
            }

            SvgTrueType font = new(Resources.BundledResources.Read(name));
            _bundledFonts.Add(name, font);
            return font;
        }
    }
}
