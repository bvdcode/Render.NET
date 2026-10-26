using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Mesh
{
    /// <summary>Loads OBJ polygon geometry, normals and smoothing groups without opening material or texture files.</summary>
    public static class ObjReader
    {
        /// <summary>Combines all OBJ objects and groups into one mesh and leaves the source open.</summary>
        public static async Task<TriangleMesh> ReadAsync(Stream source, ObjReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new ObjReaderOptions();
            options.Validate();
            using MemoryStream input = await BoundedInput.ReadAsync(source, options.MaxInputBytes, cancellationToken).ConfigureAwait(false);
            using StreamReader reader = new(input);
            return await new ObjParser(options).ReadAsync(reader, cancellationToken).ConfigureAwait(false);
        }
    }
}
