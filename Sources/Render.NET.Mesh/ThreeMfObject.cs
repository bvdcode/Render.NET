using System.Numerics;

namespace Render.NET.Mesh
{
    /// <summary>An object resource. Its identity is the pair of model part path and object ID.</summary>
    public class ThreeMfObject
    {
        /// <summary>Gets the ID within the owning model part.</summary>
        public int Id { get; }
        /// <summary>Gets the normalized package-relative model part path.</summary>
        public string ModelPart { get; }
        /// <summary>Gets the optional source object name.</summary>
        public string? Name { get; }
        /// <summary>Gets the source object type, usually model.</summary>
        public string Type { get; }
        /// <summary>Gets whether this resource is an assembly of component objects.</summary>
        public bool IsAssembly { get; }
        /// <summary>Gets the direct triangle count; assemblies hold triangles in their children.</summary>
        public int TriangleCount { get; }

        internal ThreeMfObject(ObjectKey key, string? name, string type, bool assembly, int triangleCount)
        {
            Id = key.Id;
            ModelPart = key.Part;
            Name = name;
            Type = type;
            IsAssembly = assembly;
            TriangleCount = triangleCount;
        }
    }
}
