using System.Collections.Generic;
using System.Text.Json;

namespace Render.NET.Tgs
{
    /// <summary>An immutable Lottie composition read from JSON or a gzip-compressed Telegram TGS.</summary>
    public sealed class TgsDocument
    {
        internal JsonElement Root { get; }
        internal TgsReaderOptions Limits { get; }
        /// <summary>Gets the composition width in pixels.</summary>
        public int Width { get; }
        /// <summary>Gets the composition height in pixels.</summary>
        public int Height { get; }
        /// <summary>Gets frames per second.</summary>
        public double FrameRate { get; }
        /// <summary>Gets the inclusive first frame number.</summary>
        public double InPoint { get; }
        /// <summary>Gets the exclusive final frame number.</summary>
        public double OutPoint { get; }
        /// <summary>Gets animation duration in seconds.</summary>
        public double Duration => (OutPoint - InPoint) / FrameRate;
        /// <summary>Gets explicitly detected unsupported features. Rendering rejects them by default.</summary>
        public IReadOnlyList<string> UnsupportedFeatures { get; }

        internal TgsDocument(JsonElement root, TgsReaderOptions limits, string[] features)
        {
            Root = root;
            Limits = limits;
            Width = (int)TgsJson.Number(root, "w");
            Height = (int)TgsJson.Number(root, "h");
            FrameRate = TgsJson.Number(root, "fr");
            InPoint = TgsJson.Number(root, "ip");
            OutPoint = TgsJson.Number(root, "op");
            UnsupportedFeatures = System.Array.AsReadOnly(features);
        }
    }

    /// <summary>Bounds for TGS input, decompression and selected-frame geometry.</summary>
    public sealed record TgsReaderOptions
    {
        /// <summary>Gets the maximum encoded JSON/TGS input size, default 16 MiB.</summary>
        public int MaximumInputBytes { get; init; } = 16 * 1024 * 1024;
        /// <summary>Gets the maximum uncompressed JSON size, default 32 MiB.</summary>
        public int MaximumJsonBytes { get; init; } = 32 * 1024 * 1024;
        /// <summary>Gets the maximum JSON value count and expanded scene items.</summary>
        public int MaximumItems { get; init; } = 500_000;
        /// <summary>Gets the maximum JSON/precomposition nesting depth, from 1 to 64.</summary>
        public int MaximumDepth { get; init; } = 64;
        /// <summary>Gets the maximum generated path commands per selected frame.</summary>
        public int MaximumPathCommands { get; init; } = 1_000_000;
    }
}
