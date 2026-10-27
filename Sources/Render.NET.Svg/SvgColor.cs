using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Render.NET.Svg
{
    internal static class SvgColor
    {
        private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = 0x000000,
            ["white"] = 0xffffff,
            ["red"] = 0xff0000,
            ["green"] = 0x008000,
            ["blue"] = 0x0000ff,
            ["yellow"] = 0xffff00,
            ["cyan"] = 0x00ffff,
            ["aqua"] = 0x00ffff,
            ["magenta"] = 0xff00ff,
            ["fuchsia"] = 0xff00ff,
            ["gray"] = 0x808080,
            ["grey"] = 0x808080,
            ["silver"] = 0xc0c0c0,
            ["maroon"] = 0x800000,
            ["olive"] = 0x808000,
            ["lime"] = 0x00ff00,
            ["teal"] = 0x008080,
            ["navy"] = 0x000080,
            ["purple"] = 0x800080,
            ["orange"] = 0xffa500,
            ["rebeccapurple"] = 0x663399,
            ["pink"] = 0xffc0cb,
            ["brown"] = 0xa52a2a
        };
        internal static Rgba32 Parse(string value)
        {
            value = value.Trim();
            if (value == "transparent")
            {
                return default;
            }

            if (value.StartsWith('#'))
            {
                return Hex(value[1..]);
            }

            if (Named.TryGetValue(value, out uint rgb))
            {
                return new Rgba32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }

            if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                return Functional(value);
            }

            System.Drawing.Color known = System.Drawing.Color.FromName(value);
            if (known.IsKnownColor && !known.IsSystemColor)
            {
                return new Rgba32(known.R, known.G, known.B, known.A);
            }

            throw new NotSupportedException("SVG color: " + value);
        }

        private static Rgba32 Hex(string text)
        {
            if (text.Length == 3 || text.Length == 4)
            {
                string expanded = "";
                foreach (char c in text)
                {
                    expanded += new string (c, 2);
                }

                text = expanded;
            }

            if ((text.Length != 6 && text.Length != 8) || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint n))
            {
                throw new InvalidDataException("Invalid SVG hexadecimal color.");
            }

            if (text.Length == 6)
            {
                n = (n << 8) | 255;
            }

            return new Rgba32((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n);
        }

        private static Rgba32 Functional(string value)
        {
            int first = value.IndexOf('(');
            if (first < 0 || !value.EndsWith(')'))
            {
                throw new InvalidDataException("Invalid SVG color.");
            }

            string[] p = value[(first + 1)..^1].Split(new[] { ',', ' ', '/', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3 || p.Length > 4)
            {
                throw new InvalidDataException("Invalid SVG RGB color.");
            }

            return new Rgba32(Channel(p[0], 255), Channel(p[1], 255), Channel(p[2], 255), p.Length == 4 ? Channel(p[3], 1) : (byte)255);
        }

        private static byte Channel(string text, float maximum) => Byte(SvgLength.Parse(text, maximum) / maximum);
        internal static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
        internal static Rgba32 Alpha(Rgba32 color, float opacity) => color with
        {
            A = Byte((color.A / 255f) * opacity)
        };
    }

    internal static class SvgLength
    {
        internal static float Parse(string text, float percent = 1)
        {
            text = text.Trim();
            if (text.EndsWith('%'))
            {
                return (Number(text[..^1]) * percent) / 100;
            }

            if (text.Length >= 2 && char.IsAsciiLetter(text[^1]))
            {
                float factor = text[^2..] switch
                {
                    "px" => 1,
                    "pt" => 96f / 72,
                    "pc" => 16,
                    "in" => 96,
                    "cm" => 96f / 2.54f,
                    "mm" => 96f / 25.4f,
                    "em" or "ex" => throw new NotSupportedException("Font-relative SVG lengths"),
                    _ => throw new NotSupportedException("SVG length unit")};
                return Number(text[..^2]) * factor;
            }

            return Number(text);
        }

        private static float Number(string text)
        {
            var reader = new SvgNumbers(text);
            float value = reader.Read();
            if (!reader.End)
            {
                throw new InvalidDataException("Invalid SVG length.");
            }

            return value;
        }
    }
}
