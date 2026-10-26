using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Render.NET.Mesh
{
    /// <summary>3MF object resources and positioned build instances. Geometry is expressed in millimeters.</summary>
    public class ThreeMfDocument
    {
        private readonly Dictionary<ObjectKey, ObjectResource> _resources;
        private readonly ThreeMfReaderOptions _options;
        /// <summary>Gets the package's root model part.</summary>
        public string RootModelPart { get; }
        /// <summary>Gets object resources from all referenced model parts, including resources not used by the build.</summary>
        public IReadOnlyList<ThreeMfObject> Objects { get; }
        /// <summary>Gets positioned instances from the root build, in document order.</summary>
        public IReadOnlyList<ThreeMfBuildItem> BuildItems { get; }

        internal ThreeMfDocument(string rootPart, Dictionary<ObjectKey, ObjectResource> resources,
            List<ThreeMfBuildItem> items, ThreeMfReaderOptions options, CancellationToken token)
        {
            RootModelPart = rootPart;
            _resources = resources;
            _options = options;
            Objects = Array.AsReadOnly(resources.Values.Select(item => item.Info).ToArray());
            BuildItems = items.AsReadOnly();
            Dictionary<ObjectKey, int> depths = [];
            foreach (ObjectKey key in resources.Keys)
            {
                ValidateGraph(key, [], depths, token);
            }

            foreach (ThreeMfBuildItem item in items)
            {
                if (!resources.ContainsKey(new ObjectKey(item.ModelPart, item.ObjectId)))
                {
                    throw new InvalidDataException("3MF build references an unknown object.");
                }
            }
        }

        /// <summary>Combines every root build instance, applying nested component and build transforms. Unused resources are excluded.</summary>
        public TriangleMesh CreateBuildMesh(CancellationToken cancellationToken = default) => CreateMesh(BuildItems, cancellationToken);

        /// <summary>Expands one zero-based build instance, preserving its transform.</summary>
        public TriangleMesh CreateBuildItemMesh(int index, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, BuildItems.Count);
            return CreateMesh([BuildItems[index]], cancellationToken);
        }

        /// <summary>Expands an object resource without any root build transform. Specify its model part when using cross-part IDs.</summary>
        public TriangleMesh CreateObjectMesh(int objectId, string? modelPart = null, CancellationToken cancellationToken = default)
        {
            string path = ThreeMfPackage.Resolve("", modelPart ?? RootModelPart);
            foreach (ObjectKey key in _resources.Keys)
            {
                if (key.Id == objectId && string.Equals(key.Part, path, StringComparison.OrdinalIgnoreCase))
                {
                    return CreateMesh([new ThreeMfBuildItem(key, Matrix4x4.Identity)], cancellationToken);
                }
            }

            throw new ArgumentException("No object with that ID exists in the requested model part.", nameof(objectId));
        }

        private TriangleMesh CreateMesh(IEnumerable<ThreeMfBuildItem> items, CancellationToken token)
        {
            List<Vector3> vertices = [];
            List<int> indices = [];
            int visits = 0;
            foreach (ThreeMfBuildItem item in items)
            {
                Expand(new ObjectKey(item.ModelPart, item.ObjectId), item.Transform, vertices, indices, ref visits, token);
            }

            if (indices.Count == 0)
            {
                throw new InvalidDataException("The selected 3MF build or object contains no triangles.");
            }

            return new TriangleMesh(vertices.ToArray(), indices.ToArray());
        }

        private void Expand(ObjectKey key, Matrix4x4 transform, List<Vector3> vertices, List<int> indices, ref int visits, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (++visits > _options.MaxInstanceCount)
            {
                throw new InvalidDataException("3MF assembly expansion exceeds MaxInstanceCount.");
            }

            ObjectResource resource = _resources[key];
            if ((long)vertices.Count + resource.Vertices.Length > _options.MaxVertexCount ||
                (long)indices.Count + resource.Indices.Length > (long)_options.MaxTriangleCount * 3)
            {
                throw new InvalidDataException("Expanded 3MF geometry exceeds its vertex or triangle limit.");
            }

            int start = vertices.Count;
            foreach (Vector3 vertex in resource.Vertices)
            {
                if ((vertices.Count & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                Vector3 transformed = Vector3.Transform(vertex, transform);
                if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y) || !float.IsFinite(transformed.Z))
                {
                    throw new InvalidDataException("3MF transformation produced non-finite coordinates.");
                }

                vertices.Add(transformed);
            }

            bool mirrored = transform.GetDeterminant() < 0;
            for (int corner = 0; corner < resource.Indices.Length; corner += 3)
            {
                indices.Add(start + resource.Indices[corner]);
                indices.Add(start + resource.Indices[corner + (mirrored ? 2 : 1)]);
                indices.Add(start + resource.Indices[corner + (mirrored ? 1 : 2)]);
            }

            foreach (ComponentReference component in resource.Components)
            {
                Expand(component.Key, component.Transform * transform, vertices, indices, ref visits, token);
            }
        }

        private int ValidateGraph(ObjectKey key, HashSet<ObjectKey> ancestors, Dictionary<ObjectKey, int> depths, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (depths.TryGetValue(key, out int known))
            {
                return known;
            }

            if (ancestors.Count >= _options.MaxComponentDepth || !ancestors.Add(key) || !_resources.TryGetValue(key, out ObjectResource? resource))
            {
                throw new InvalidDataException("3MF contains a missing, cyclic, or excessively nested component reference.");
            }

            int depth = 1;
            foreach (ComponentReference component in resource.Components)
            {
                depth = Math.Max(depth, 1 + ValidateGraph(component.Key, ancestors, depths, token));
            }

            ancestors.Remove(key);
            if (depth > _options.MaxComponentDepth)
            {
                throw new InvalidDataException("3MF exceeds MaxComponentDepth.");
            }

            depths.Add(key, depth);
            return depth;
        }
    }
}
