using System.Numerics;

namespace Render.NET.Mesh
{
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
}
