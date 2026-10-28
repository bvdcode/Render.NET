using System.Numerics;

namespace Render.NET.Mesh
{
    internal record ObjectResource(ThreeMfObject Info, Vector3[] Vertices, int[] Indices, ComponentReference[] Components);
}
