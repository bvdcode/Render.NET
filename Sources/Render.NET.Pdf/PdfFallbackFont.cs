using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;

namespace Render.NET.Pdf
{
    internal class PdfFallbackFont
    {
        private readonly TrueTypeFont _font;
        private readonly string _name;
        private TrueTypeFont? _unicodeFont;

        internal PdfFallbackFont(string name, Func<string, ReadOnlyMemory<byte>>? resolver)
        {
            _name = name;
            ReadOnlyMemory<byte> bytes = resolver?.Invoke(name) ?? default;
            if (bytes.IsEmpty)
            {
                bytes = ReadBundled(name, "Aileron");
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
            TrueTypeFont font = _font;
            if (!HasCharacter(font, character))
            {
                _unicodeFont ??= TrueTypeFontParser.Parse(new TrueTypeDataBytes(ReadBundled(_name, "Canada1500")));
                font = _unicodeFont;
            }

            if (!HasCharacter(font, character) || !font.TryGetPath(character, out IReadOnlyList<PdfSubpath>? glyph))
            {
                return false;
            }

            double scale = 1d / font.GetUnitsPerEm();
            double horizontal = font.TryGetAdvanceWidth(character, out double advance) && advance > 0 && pdfAdvance > 0
                ? pdfAdvance / advance : scale;
            paths = TransformationMatrix.GetScaleMatrix(horizontal, scale).Transform(glyph).ToArray();
            return true;
        }

        private static bool HasCharacter(TrueTypeFont font, int character)
            => font.TableRegister.CMapTable is not null
                && font.TableRegister.CMapTable.TryGetGlyphIndex(character, out int index) && index != 0;

        private static byte[] ReadBundled(string name, string family)
        {
            bool bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            bool italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase) || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
            string style = bold ? italic ? "BoldItalic" : "Bold" : italic ? "Italic" : "Regular";
            return Resources.BundledResources.Read(family + "-" + style);
        }
    }
}
