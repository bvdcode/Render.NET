using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Render.NET.Mesh
{
    /// <summary>Reads core 3MF mesh/build geometry and Production Extension model-part references.</summary>
    public static class ThreeMfReader
    {
        internal static readonly XNamespace Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
        internal static readonly XNamespace Production = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
        internal static readonly XNamespace Materials = "http://schemas.microsoft.com/3dmanufacturing/material/2015/02";
        internal static readonly XNamespace Alternatives = "http://schemas.microsoft.com/3dmanufacturing/production/alternatives/2021/04";

        /// <summary>Loads geometry and build instances without using an embedded thumbnail. Leaves the source open.</summary>
        public static async Task<ThreeMfDocument> ReadAsync(Stream source, ThreeMfReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new ThreeMfReaderOptions();
            using ThreeMfPackage package = await ThreeMfPackage.OpenAsync(source, options, cancellationToken).ConfigureAwait(false);
            string rootPart = (await package.FindRootRelationshipAsync(ThreeMfPackage.ModelRelationship, true, cancellationToken).ConfigureAwait(false))!;
            ThreeMfModelParser parser = new(package, options);
            return await parser.ReadAsync(rootPart, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reads the package thumbnail independently of geometry. Returns null if absent; bytes retain their original encoding and metadata.</summary>
        /// <remarks>This image describes the whole package and is never automatically used for a selected object.</remarks>
        public static async Task<ThreeMfThumbnail?> ReadThumbnailAsync(Stream source, ThreeMfReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new ThreeMfReaderOptions();
            using ThreeMfPackage package = await ThreeMfPackage.OpenAsync(source, options, cancellationToken).ConfigureAwait(false);
            string? part = await package.FindRootRelationshipAsync(ThreeMfPackage.ThumbnailRelationship, false, cancellationToken).ConfigureAwait(false);
            if (part == null)
            {
                return null;
            }

            byte[] bytes = await package.ReadBytesAsync(part, options.MaxThumbnailBytes, cancellationToken).ConfigureAwait(false);
            string contentType = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
                : bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg"
                : throw new InvalidDataException("3MF thumbnail must be a PNG or JPEG image.");
            return new ThreeMfThumbnail(part, contentType, bytes);
        }

        internal static int PositiveId(XElement element, string name)
        {
            if (!int.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
            {
                throw new InvalidDataException($"3MF {name} must be a positive integer.");
            }

            return value;
        }

        internal static float Number(string? text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            {
                throw new InvalidDataException("3MF coordinates and transforms must be finite numbers.");
            }

            return value;
        }

        internal static Matrix4x4 Transform(XElement element, float unitScale)
        {
            string? text = (string?)element.Attribute("transform");
            if (text == null)
            {
                return Matrix4x4.Identity;
            }

            string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 12)
            {
                throw new InvalidDataException("3MF transforms require twelve matrix components.");
            }

            float[] m = parts.Select(Number).ToArray();
            Matrix4x4 result = new(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0,
                m[9] * unitScale, m[10] * unitScale, m[11] * unitScale, 1);
            float determinant = result.GetDeterminant();
            if (!float.IsFinite(determinant) || determinant == 0 || !float.IsFinite(result.M41) || !float.IsFinite(result.M42) || !float.IsFinite(result.M43))
            {
                throw new InvalidDataException("3MF transforms must be finite and nonsingular.");
            }

            return result;
        }
    }
}
