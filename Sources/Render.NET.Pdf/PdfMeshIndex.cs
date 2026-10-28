using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET.Pdf
{
    internal class PdfMeshIndex
    {
        private readonly MeshTriangle[] _triangles;
        private readonly int[] _order;
        private readonly Node _root;

        internal PdfMeshIndex(List<MeshTriangle> triangles, CancellationToken token)
        {
            _triangles = triangles.ToArray();
            _order = new int[_triangles.Length];
            for (int i = 0; i < _order.Length; i++)
            {
                _order[i] = i;
            }

            _root = Build(0, _order.Length, token);
        }

        internal MeshTriangle? Find(Vector2 point, out Vector3 weights)
        {
            int found = -1;
            weights = default;
            Query(_root, point, ref found, ref weights);
            return found < 0 ? null : _triangles[found];
        }

        private Node Build(int start, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Vector2 min = new(float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity);
            for (int i = start; i < start + count; i++)
            {
                MeshTriangle t = _triangles[_order[i]];
                min = Vector2.Min(min, Vector2.Min(t.A.Position, Vector2.Min(t.B.Position, t.C.Position)));
                max = Vector2.Max(max, Vector2.Max(t.A.Position, Vector2.Max(t.B.Position, t.C.Position)));
            }

            Node? node = new Node(min, max, start, count);
            if (count > 8)
            {
                bool horizontal = max.X - min.X >= max.Y - min.Y;
                Array.Sort(_order, start, count, Comparer<int>.Create((a, b) => Center(_triangles[a], horizontal).CompareTo(Center(_triangles[b], horizontal))));
                int half = count / 2;
                node.Left = Build(start, half, token);
                node.Right = Build(start + half, count - half, token);
            }

            return node;
        }

        private void Query(Node node, Vector2 point, ref int found, ref Vector3 weights)
        {
            const float tolerance = 0.00001f;
            if (point.X < node.Min.X - tolerance || point.X > node.Max.X + tolerance
                || point.Y < node.Min.Y - tolerance || point.Y > node.Max.Y + tolerance)
            {
                return;
            }

            if (node.Left is not null)
            {
                Query(node.Left, point, ref found, ref weights);
                Query(node.Right!, point, ref found, ref weights);
                return;
            }

            for (int i = node.Start; i < node.Start + node.Count; i++)
            {
                int index = _order[i];
                if (index > found && Contains(_triangles[index], point, out Vector3 barycentric))
                {
                    found = index;
                    weights = barycentric;
                }
            }
        }

        private static bool Contains(MeshTriangle t, Vector2 point, out Vector3 weights)
        {
            Vector2 first = t.B.Position - t.A.Position;
            Vector2 second = t.C.Position - t.A.Position;
            Vector2 delta = point - t.A.Position;
            double determinant = Cross(first, second);
            weights = default;
            if (Math.Abs(determinant) < 1e-20)
            {
                return false;
            }

            double b = Cross(delta, second) / determinant;
            double c = Cross(first, delta) / determinant;
            double a = 1 - b - c;
            if (a < -1e-6 || b < -1e-6 || c < -1e-6)
            {
                return false;
            }

            weights = new Vector3((float)a, (float)b, (float)c);
            return true;
        }

        private static double Cross(Vector2 a, Vector2 b) => ((double)a.X * b.Y) - ((double)a.Y * b.X);
        private static float Center(MeshTriangle t, bool x)
            => x ? t.A.Position.X + t.B.Position.X + t.C.Position.X : t.A.Position.Y + t.B.Position.Y + t.C.Position.Y;

        private record Node(Vector2 Min, Vector2 Max, int Start, int Count)
        {
            internal Node? Left { get; set; }
            internal Node? Right { get; set; }
        }
    }
}
