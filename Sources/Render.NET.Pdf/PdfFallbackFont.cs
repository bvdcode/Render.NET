using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;

namespace Render.NET.Pdf
{
    internal sealed class PdfFallbackFont
    {
        private readonly TrueTypeFont _font;

        internal PdfFallbackFont(string name, Func<string, ReadOnlyMemory<byte>>? resolver)
        {
            ReadOnlyMemory<byte> bytes = resolver?.Invoke(name) ?? default;
            if (bytes.IsEmpty)
            {
                bytes = ReadBundled(name);
            }

            _font = TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes.ToArray()));
        }

        internal bool TryGetPath(string unicode, double pdfAdvance, out IReadOnlyList<PdfSubpath> paths)
        {
            paths = Array.Empty<PdfSubpath>();
            if (string.IsNullOrEmpty(unicode))
            {
                return false;
            }

            int character = char.ConvertToUtf32(unicode, 0);
            if (!_font.TryGetPath(character, out var glyph))
            {
                return false;
            }

            double scale = 1d / _font.GetUnitsPerEm();
            double horizontal = _font.TryGetAdvanceWidth(character, out double advance) && advance > 0 && pdfAdvance > 0
                ? pdfAdvance / advance : scale;
            paths = TransformationMatrix.GetScaleMatrix(horizontal, scale).Transform(glyph).ToArray();
            return true;
        }

        private static byte[] ReadBundled(string name)
        {
            bool bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            bool italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase) || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
            string style = bold ? italic ? "BoldItalic" : "Bold" : italic ? "Italic" : "Regular";
            using Stream input = typeof(PdfFallbackFont).Assembly.GetManifestResourceStream("Render.NET.Pdf.Fonts.Aileron-" + style + ".ttf")
                ?? throw new InvalidOperationException("Bundled font resource is missing.");
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
