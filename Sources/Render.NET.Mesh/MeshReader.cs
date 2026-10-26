using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Mesh
{
    /// <summary>Loads preview geometry from STL, OBJ, or a 3MF root build.</summary>
    public static class MeshReader
    {
        /// <summary>Loads a file using its extension. For explicit limits or 3MF selection, use the format-specific readers.</summary>
        public static async Task<TriangleMesh> ReadFileAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            using FileStream source = File.OpenRead(path);
            return await ReadAsync(source, Path.GetExtension(path), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Loads a stream using an explicit extension (stl, obj, or 3mf), leaving it open.</summary>
        public static async Task<TriangleMesh> ReadAsync(Stream source, string extension, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(extension);
            switch (extension.TrimStart('.').ToLowerInvariant())
            {
                case "stl": return await StlReader.ReadAsync(source, cancellationToken: cancellationToken).ConfigureAwait(false);
                case "obj": return await ObjReader.ReadAsync(source, cancellationToken: cancellationToken).ConfigureAwait(false);
                case "3mf":
                    ThreeMfDocument document = await ThreeMfReader.ReadAsync(source, cancellationToken: cancellationToken).ConfigureAwait(false);
                    return document.CreateBuildMesh(cancellationToken);
                default: throw new NotSupportedException("Supported mesh formats are STL, OBJ, and 3MF.");
            }
        }
    }
}
