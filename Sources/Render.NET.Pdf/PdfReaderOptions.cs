using System;

namespace Render.NET.Pdf
{
    /// <summary>PDF input and page interpretation limits. These do not replace process isolation for hostile documents.</summary>
    public sealed record PdfReaderOptions
    {
        /// <summary>Gets the document password, if required.</summary>
        public string Password { get; init; } = string.Empty;
        /// <summary>Gets an optional TrueType fallback provider, called with a PDF font name when no embedded outline is available.
        /// Return empty memory to use the bundled public-domain substitutes, including Canada 1500 for Cyrillic and Greek.</summary>
        public Func<string, ReadOnlyMemory<byte>>? FontResolver { get; init; }
        /// <summary>Gets the maximum encoded input length; defaults to 128 MiB.</summary>
        public int MaximumInputBytes { get; init; } = 128 * 1024 * 1024;
        /// <summary>Gets the maximum executed graphics operations per page, including nested forms.</summary>
        public int MaximumOperations { get; init; } = 1_000_000;
        /// <summary>Gets the maximum stored vector commands per page, including glyphs.</summary>
        public int MaximumPathCommands { get; init; } = 1_000_000;
        /// <summary>Gets the maximum pixels in a decoded image.</summary>
        public int MaximumImagePixels { get; init; } = 16_777_216;
        /// <summary>Gets the total decoded image byte budget per page.</summary>
        public int MaximumImageBytes { get; init; } = 128 * 1024 * 1024;
        /// <summary>Gets the decoded stream and stored geometry budget for mesh shadings per page; defaults to 64 MiB.
        /// Patch tessellation is separately bounded by this budget for each shading invocation.</summary>
        public int MaximumShadingBytes { get; init; } = 64 * 1024 * 1024;
    }
}
