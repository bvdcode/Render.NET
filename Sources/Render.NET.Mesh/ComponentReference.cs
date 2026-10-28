using System.Numerics;

namespace Render.NET.Mesh
{
    internal readonly record struct ComponentReference(ObjectKey Key, Matrix4x4 Transform);
}
