using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Render.NET.Mesh
{
    internal class ThreeMfModelParser(ThreeMfPackage package, ThreeMfReaderOptions options)
    {
        private readonly Dictionary<ObjectKey, ObjectResource> _resources = [];
        private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _pending = [];
        private readonly List<ThreeMfBuildItem> _build = [];
        private int _vertices;
        private int _triangles;
        private int _references;
        private string _rootPart = "";
        private HashSet<string> _modelReferences = [];

        internal async Task<ThreeMfDocument> ReadAsync(string rootPart, CancellationToken token)
        {
            _rootPart = rootPart;
            Enqueue(rootPart);
            while (_pending.TryDequeue(out string? part))
            {
                token.ThrowIfCancellationRequested();
                XDocument document = await package.ReadXmlAsync(part, token).ConfigureAwait(false);
                XElement root = document.Root ?? throw new InvalidDataException("Empty 3MF model part.");
                ValidateModelRoot(root);
                _modelReferences = await package.ModelReferencesAsync(part, token).ConfigureAwait(false);
                ValidateExtensions(root);
                float units = ReadUnits(root);
                ParseResources(root, part, units, token);

                if (part == rootPart)
                {
                    foreach (XElement item in Single(root, "build").Elements(ThreeMfReader.Core + "item"))
                    {
                        ComponentReference reference = Reference(item, part, units);
                        _build.Add(new ThreeMfBuildItem(reference.Key, reference.Transform));
                    }
                }
            }

            return new ThreeMfDocument(rootPart, _resources, _build, options, token);
        }

        private static void ValidateModelRoot(XElement root)
        {
            if (root.Name != ThreeMfReader.Core + "model")
            {
                throw new InvalidDataException("Unsupported 3MF core namespace or model root.");
            }

            if (root.Descendants(ThreeMfReader.Alternatives + "alternatives").Any())
            {
                throw new NotSupportedException("3MF alternative production representations are not supported.");
            }
        }

        private static void ValidateExtensions(XElement root)
        {
            foreach (string prefix in ((string?)root.Attribute("requiredextensions") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                XNamespace? extension = root.GetNamespaceOfPrefix(prefix);
                // Preview materials are intentionally replaced by the caller's BaseColor.
                // The Materials extension does not change the underlying mesh geometry.
                if (extension != ThreeMfReader.Production && extension != ThreeMfReader.Materials)
                {
                    throw new NotSupportedException($"3MF requires unsupported extension '{prefix}'.");
                }
            }
        }

        private static float ReadUnits(XElement root) =>
            ((string?)root.Attribute("unit") ?? "millimeter") switch
            {
                "micron" => 0.001f, "millimeter" => 1, "centimeter" => 10, "inch" => 25.4f,
                "foot" => 304.8f, "meter" => 1000,
                _ => throw new InvalidDataException("Unknown 3MF length unit.")
            };

        private void ParseResources(XElement root, string part, float units, CancellationToken token)
        {
            XElement resources = Single(root, "resources");
            HashSet<int> ids = [];
            foreach (XElement resource in resources.Elements())
            {
                if (resource.Attribute("id") is not null && !ids.Add(ThreeMfReader.PositiveId(resource, "id")))
                {
                    throw new InvalidDataException("Duplicate 3MF resource ID in a model part.");
                }

                if (resource.Name == ThreeMfReader.Core + "object")
                {
                    ParseObject(resource, part, units, token);
                }
            }
        }

        private void ParseObject(XElement element, string part, float units, CancellationToken token)
        {
            if (_resources.Count >= options.MaxObjectCount)
            {
                throw new InvalidDataException("3MF exceeds MaxObjectCount.");
            }

            ObjectKey key = new(part, ThreeMfReader.PositiveId(element, "id"));
            XElement[] meshes = element.Elements(ThreeMfReader.Core + "mesh").ToArray();
            XElement[] components = element.Elements(ThreeMfReader.Core + "components").ToArray();
            if (meshes.Length + components.Length != 1)
            {
                throw new InvalidDataException("3MF object must contain either one mesh or one components list.");
            }

            List<Vector3> vertices = [];
            List<int> indices = [];
            List<ComponentReference> children = [];
            if (meshes.Length == 1)
            {
                vertices = ReadVertices(meshes[0], units, token);
                indices = ReadTriangles(meshes[0], vertices.Count, token);
            }
            else
            {
                children = ReadComponents(components[0], part, units, token);
            }

            ThreeMfObject info = new(key, (string?)element.Attribute("name"), (string?)element.Attribute("type") ?? "model", components.Length > 0, indices.Count / 3);
            _resources.Add(key, new ObjectResource(info, vertices.ToArray(), indices.ToArray(), children.ToArray()));
        }

        private List<Vector3> ReadVertices(XElement mesh, float units, CancellationToken token)
        {
            List<Vector3> vertices = [];
            foreach (XElement vertex in Single(mesh, "vertices").Elements(ThreeMfReader.Core + "vertex"))
            {
                token.ThrowIfCancellationRequested();
                if (++_vertices > options.MaxVertexCount)
                {
                    throw new InvalidDataException("3MF exceeds MaxVertexCount.");
                }

                Vector3 value = new(ThreeMfReader.Number((string?)vertex.Attribute("x")),
                    ThreeMfReader.Number((string?)vertex.Attribute("y")), ThreeMfReader.Number((string?)vertex.Attribute("z")));
                value *= units;
                if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
                {
                    throw new InvalidDataException("3MF coordinates overflow after unit conversion.");
                }

                vertices.Add(value);
            }

            return vertices;
        }

        private List<int> ReadTriangles(XElement mesh, int vertexCount, CancellationToken token)
        {
            List<int> indices = [];
            foreach (XElement triangle in Single(mesh, "triangles").Elements(ThreeMfReader.Core + "triangle"))
            {
                token.ThrowIfCancellationRequested();
                if (++_triangles > options.MaxTriangleCount)
                {
                    throw new InvalidDataException("3MF exceeds MaxTriangleCount.");
                }

                for (int corner = 1; corner <= 3; corner++)
                {
                    if (!int.TryParse((string?)triangle.Attribute("v" + corner), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int index) || index < 0 || index >= vertexCount)
                    {
                        throw new InvalidDataException("3MF triangle index is outside its mesh vertex array.");
                    }

                    indices.Add(index);
                }
            }

            if (indices.Count == 0)
            {
                throw new InvalidDataException("3MF mesh has no triangles.");
            }

            return indices;
        }

        private List<ComponentReference> ReadComponents(XElement components, string part, float units, CancellationToken token)
        {
            List<ComponentReference> children = [];
            foreach (XElement component in components.Elements(ThreeMfReader.Core + "component"))
            {
                token.ThrowIfCancellationRequested();
                children.Add(Reference(component, part, units));
            }

            if (children.Count == 0)
            {
                throw new InvalidDataException("3MF component list is empty.");
            }

            return children;
        }

        private ComponentReference Reference(XElement element, string part, float units)
        {
            if (++_references > options.MaxInstanceCount)
            {
                throw new InvalidDataException("3MF exceeds MaxInstanceCount.");
            }

            string? path = (string?)element.Attribute(ThreeMfReader.Production + "path");
            if (path != null && (part != _rootPart || !path.StartsWith('/')))
            {
                throw new InvalidDataException("3MF Production paths must be absolute and originate in the root model part.");
            }

            string target = path == null ? part : package.CanonicalPart(ThreeMfPackage.Resolve(part, path));
            if (path != null && !_modelReferences.Contains(target))
            {
                throw new InvalidDataException("3MF cross-part reference has no matching model relationship.");
            }
            Enqueue(target);
            return new ComponentReference(new ObjectKey(target, ThreeMfReader.PositiveId(element, "objectid")), ThreeMfReader.Transform(element, units));
        }

        private void Enqueue(string part)
        {
            if (_queued.Add(part))
            {
                _pending.Enqueue(part);
            }
        }

        private static XElement Single(XElement parent, string name)
        {
            XElement[] matches = parent.Elements(ThreeMfReader.Core + name).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidDataException($"Expected one 3MF {name} element.");
        }
    }
}
