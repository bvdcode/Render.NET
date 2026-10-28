using System.Collections.Generic;
using System.Text.Json;

namespace Render.NET.Tgs
{
    /// <summary>An immutable Lottie composition read from JSON or a gzip-compressed Telegram TGS.</summary>
    public class TgsDocument
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
}
