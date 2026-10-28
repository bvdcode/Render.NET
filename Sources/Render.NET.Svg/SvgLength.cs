using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Render.NET.Svg
{
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
                    _ => throw new NotSupportedException("SVG length unit")
                };
                return Number(text[..^2]) * factor;
            }

            return Number(text);
        }

        private static float Number(string text)
        {
            SvgNumbers? reader = new SvgNumbers(text);
            float value = reader.Read();
            if (!reader.End)
            {
                throw new InvalidDataException("Invalid SVG length.");
            }

            return value;
        }
    }
}
