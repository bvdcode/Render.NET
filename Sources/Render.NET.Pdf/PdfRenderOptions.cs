namespace Render.NET.Pdf
{
    /// <summary>Output resolution, background, and antialiasing settings.</summary>
    public record PdfRenderOptions
    {
        /// <summary>Gets output dots per inch. PDF points are 1/72 inch. Defaults to 96.</summary>
        public double Dpi { get; init; } = 96;
        /// <summary>Gets an optional maximum width. The page is reduced proportionally to fit.</summary>
        public int? MaximumWidth { get; init; }
        /// <summary>Gets an optional maximum height. The page is reduced proportionally to fit.</summary>
        public int? MaximumHeight { get; init; }
        /// <summary>Gets the supersampling factor, from 1 to 4. Defaults to 2.</summary>
        public int Supersampling { get; init; } = 2;
        /// <summary>Gets the page background. Set alpha to zero for transparent output.</summary>
        public Rgba32 Background { get; init; } = new Rgba32(255, 255, 255);
        /// <summary>Gets whether to render despite the page's explicitly reported unsupported features.</summary>
        public bool AllowPartialRendering { get; init; }
        /// <summary>Gets the maximum number of samples, including supersampling. Defaults to 16,777,216.</summary>
        public int MaximumSamples { get; init; } = 16_777_216;
    }
}
