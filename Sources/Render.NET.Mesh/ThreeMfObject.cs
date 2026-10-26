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

    /// <summary>A positioned object instance in the root model's build list.</summary>
    public class ThreeMfBuildItem
    {
        /// <summary>Gets the referenced object ID.</summary>
        public int ObjectId { get; }
        /// <summary>Gets the referenced model part.</summary>
        public string ModelPart { get; }
        /// <summary>Gets the instance transform, with translation expressed in millimeters.</summary>
        public Matrix4x4 Transform { get; }

        internal ThreeMfBuildItem(ObjectKey key, Matrix4x4 transform)
        {
            ObjectId = key.Id;
            ModelPart = key.Part;
            Transform = transform;
        }
    }

    /// <summary>An original package-level thumbnail; it represents the package, not a selected object.</summary>
    public class ThreeMfThumbnail
    {
        /// <summary>Gets the package-relative image path.</summary>
        public string PartPath { get; }
        /// <summary>Gets image/png or image/jpeg.</summary>
        public string ContentType { get; }
        /// <summary>Gets the original encoded image bytes.</summary>
        public System.ReadOnlyMemory<byte> Data { get; }

        internal ThreeMfThumbnail(string path, string contentType, byte[] data)
        {
            PartPath = path;
            ContentType = contentType;
            Data = data;
        }
    }

    internal readonly record struct ObjectKey(string Part, int Id);
    internal readonly record struct ComponentReference(ObjectKey Key, Matrix4x4 Transform);
    internal record ObjectResource(ThreeMfObject Info, Vector3[] Vertices, int[] Indices, ComponentReference[] Components);
}
