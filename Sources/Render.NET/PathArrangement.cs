using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    internal class PathArrangement
    {
        private record Edge(Vector2 A, Vector2 B, int Operand)
        {
            internal List<double> Splits { get; } = new()
            {
                0,
                1
            };
            internal float Left => Math.Min(A.X, B.X);
            internal float Right => Math.Max(A.X, B.X);
            internal float Top => Math.Min(A.Y, B.Y);
            internal float Bottom => Math.Max(A.Y, B.Y);
        }

        private readonly List<Edge> _edges = new();
        private readonly PathBooleanOperation _operation;
        private readonly FillRule _rule;
        private readonly CancellationToken _token;
        private readonly float _epsilon;
        private readonly List<Vector2> _vertices = new();
        private readonly Dictionary<(long, long), List<int>> _buckets = new();
        internal PathArrangement(VectorPath first, VectorPath second, PathBooleanOperation operation, FillRule rule, float tolerance, CancellationToken token)
        {
            _operation = operation;
            _rule = rule;
            _token = token;
            Add(first, 0, tolerance);
            Add(second, 1, tolerance);
            float scale = _edges.Count == 0 ? 1 : _edges.Max(e => Math.Max(Math.Max(Math.Abs(e.A.X), Math.Abs(e.A.Y)), Math.Max(Math.Abs(e.B.X), Math.Abs(e.B.Y))));
            _epsilon = Math.Max(.00001f, scale * .0000002f);
        }

        private void Add(VectorPath path, int operand, float tolerance)
        {
            foreach (PathContour contour in PathFlattener.Flatten(path, Matrix3x2.Identity, tolerance, _token))
            {
                for (int i = 0; i < contour.Points.Count; i++)
                {
                    Vector2 a = contour.Points[i], b = contour.Points[(i + 1) % contour.Points.Count];
                    if (Vector2.DistanceSquared(a, b) > 1e-16)
                    {
                        if (_edges.Count >= 16_384)
                        {
                            throw new InvalidDataException("Path boolean operation exceeds 16,384 edges.");
                        }

                        _edges.Add(new Edge(a, b, operand));
                    }
                }
            }
        }

        internal VectorPath Build()
        {
            SplitEdges();
            var boundary = new HashSet<(int A, int B)>();
            foreach (Edge edge in _edges)
            {
                _token.ThrowIfCancellationRequested();
                edge.Splits.Sort();
                for (int i = 1; i < edge.Splits.Count; i++)
                {
                    double a = edge.Splits[i - 1], b = edge.Splits[i];
                    if (b - a <= 1e-10)
                    {
                        continue;
                    }

                    Vector2 start = Vector2.Lerp(edge.A, edge.B, (float)a), end = Vector2.Lerp(edge.A, edge.B, (float)b), delta = end - start;
                    float length = delta.Length();
                    if (length <= _epsilon)
                    {
                        continue;
                    }

                    Vector2 normal = new Vector2(-delta.Y, delta.X) * (Math.Min(length * .001f, _epsilon * 2) / length), midpoint = (start + end) / 2;
                    bool left = Filled(midpoint + normal), right = Filled(midpoint - normal);
                    if (left == right)
                    {
                        continue;
                    }

                    int first = Vertex(left ? start : end), second = Vertex(left ? end : start);
                    if (first != second)
                    {
                        boundary.Add((first, second));
                    }
                }
            }

            return Trace(boundary);
        }

        private void SplitEdges()
        {
            Edge[] sorted = _edges.OrderBy(e => e.Left).ToArray();
            var active = new List<Edge>();
            int work = 0;
            foreach (Edge next in sorted)
            {
                _token.ThrowIfCancellationRequested();
                active.RemoveAll(edge => edge.Right < next.Left - _epsilon);
                foreach (Edge previous in active)
                {
                    if (previous.Bottom < next.Top - _epsilon || previous.Top > next.Bottom + _epsilon)
                    {
                        continue;
                    }

                    if (++work > 2_000_000)
                    {
                        throw new InvalidDataException("Path boolean operation exceeds its intersection budget.");
                    }

                    Intersect(previous, next);
                }

                active.Add(next);
            }
        }

        private static double Cross(Vector2 a, Vector2 b) => ((double)a.X * b.Y) - ((double)a.Y * b.X);
        private void Intersect(Edge a, Edge b)
        {
            Vector2 p = a.B - a.A, q = b.B - b.A, delta = b.A - a.A;
            double denominator = Cross(p, q);
            if (Math.Abs(denominator) > 1e-12 * Math.Max(1, p.Length() * q.Length()))
            {
                double first = Cross(delta, q) / denominator, second = Cross(delta, p) / denominator;
                if (first >= -1e-8 && first <= 1 + 1e-8 && second >= -1e-8 && second <= 1 + 1e-8)
                {
                    a.Splits.Add(Math.Clamp(first, 0, 1));
                    b.Splits.Add(Math.Clamp(second, 0, 1));
                }

                return;
            }

            if (Math.Abs(Cross(delta, p)) > _epsilon * p.Length())
            {
                return;
            }

            void Project(Edge target, Vector2 point)
            {
                Vector2 direction = target.B - target.A;
                double t = Vector2.Dot(point - target.A, direction) / direction.LengthSquared();
                if (t >= -1e-8 && t <= 1 + 1e-8)
                {
                    target.Splits.Add(Math.Clamp(t, 0, 1));
                }
            }

            Project(a, b.A);
            Project(a, b.B);
            Project(b, a.A);
            Project(b, a.B);
        }

        private bool Filled(Vector2 point)
        {
            int first = 0, second = 0;
            foreach (Edge edge in _edges)
            {
                if ((edge.A.Y > point.Y) == (edge.B.Y > point.Y))
                {
                    continue;
                }

                double x = edge.A.X + (((double)(point.Y - edge.A.Y) * (edge.B.X - edge.A.X)) / (edge.B.Y - edge.A.Y));
                if (x > point.X)
                {
                    int change = edge.B.Y > edge.A.Y ? 1 : -1;
                    if (edge.Operand == 0)
                    {
                        first += change;
                    }
                    else
                    {
                        second += change;
                    }
                }
            }

            bool a = _rule == FillRule.EvenOdd ? (first & 1) != 0 : first != 0, b = _rule == FillRule.EvenOdd ? (second & 1) != 0 : second != 0;
            return _operation switch
            {
                PathBooleanOperation.Union => a || b,
                PathBooleanOperation.Intersection => a && b,
                PathBooleanOperation.Difference => a && !b,
                _ => a != b
            };
        }

        private int Vertex(Vector2 point)
        {
            long x = checked((long)Math.Floor(point.X / _epsilon)), y = checked((long)Math.Floor(point.Y / _epsilon));
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (_buckets.TryGetValue((x + dx, y + dy), out List<int>? nearby))
                    {
                        foreach (int index in nearby)
                        {
                            if (Vector2.DistanceSquared(_vertices[index], point) <= _epsilon * _epsilon)
                            {
                                return index;
                            }
                        }
                    }
                }
            }

            int result = _vertices.Count;
            _vertices.Add(point);
            if (!_buckets.TryGetValue((x, y), out List<int>? bucket))
            {
                bucket = new List<int>();
                _buckets.Add((x, y), bucket);
            }

            bucket.Add(result);
            return result;
        }

        private VectorPath Trace(HashSet<(int A, int B)> boundary)
        {
            var outgoing = boundary.GroupBy(e => e.A).ToDictionary(g => g.Key, g => g.Select(e => e.B).ToArray());
            VectorPath? result = new VectorPath();
            while (boundary.Count > 0)
            {
                _token.ThrowIfCancellationRequested();
                var edge = boundary.First();
                int start = edge.A;
                Vector2 first = _vertices[start];
                result.MoveTo(first.X, first.Y);
                while (boundary.Remove(edge))
                {
                    Vector2 point = _vertices[edge.B];
                    if (edge.B == start)
                    {
                        result.Close();
                        break;
                    }

                    result.LineTo(point.X, point.Y);
                    Vector2 incoming = point - _vertices[edge.A];
                    int next = -1;
                    double best = double.NegativeInfinity;
                    foreach (int candidate in outgoing.GetValueOrDefault(edge.B, Array.Empty<int>()))
                    {
                        if (boundary.Contains((edge.B, candidate)))
                        {
                            Vector2 direction = _vertices[candidate] - point;
                            double turn = Math.Atan2(Cross(incoming, direction), Vector2.Dot(incoming, direction));
                            if (turn > best)
                            {
                                best = turn;
                                next = candidate;
                            }
                        }
                    }

                    if (next < 0)
                    {
                        throw new InvalidDataException("Numerically unstable path boolean boundary.");
                    }

                    edge = (edge.B, next);
                }
            }

            return result;
        }
    }
}
