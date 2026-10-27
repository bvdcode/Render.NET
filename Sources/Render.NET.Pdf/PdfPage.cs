using System.Collections.Generic;

namespace Render.NET.Pdf
{
    /// <summary>A detached, interpreted page. It can be rendered repeatedly after its document is disposed.</summary>
    public sealed class PdfPage
    {
        /// <summary>Gets the one-based page number.</summary>
        public int Number { get; }
        /// <summary>Gets the visible, rotated page width in PDF points.</summary>
        public double Width { get; }
        /// <summary>Gets the visible, rotated page height in PDF points.</summary>
        public double Height { get; }
        /// <summary>Gets features that cannot be rendered faithfully. Rendering rejects these by default.</summary>
        public IReadOnlyList<string> UnsupportedFeatures { get; }
        /// <summary>Gets font names whose missing outlines were replaced by a caller-provided or bundled font.</summary>
        public IReadOnlyList<string> SubstitutedFonts { get; }
        internal IReadOnlyList<IPdfDrawCommand> Commands { get; }

        internal PdfPage(int number, double width, double height, List<IPdfDrawCommand> commands, List<string> unsupported, List<string> substitutedFonts)
        {
            Number = number;
            Width = width;
            Height = height;
            Commands = commands.AsReadOnly();
            UnsupportedFeatures = unsupported.AsReadOnly();
            SubstitutedFonts = substitutedFonts.AsReadOnly();
        }
    }
}
