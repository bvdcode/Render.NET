using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal sealed partial class TgsSceneBuilder
    {
        private void Modify(List<(int Index, VectorPath Path)> paths, JsonElement modifier, int index, double frame)
        {
            string type = TgsJson.Text(modifier, "ty");
            if (type == "tm")
            {
                Trim(paths, modifier, index, frame);
                return;
            }

            if (type == "mm")
            {
                MergePaths(paths, modifier, index);
                return;
            }

            if (type == "rp")
            {
                RepeatPaths(paths, modifier, index, frame);
                return;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (paths[i].Index >= index)
                {
                    continue;
                }

                VectorPath path = paths[i].Path;
                VectorPath changed = type switch
                {
                    "rd" => path.RoundCorners((float)Math.Max(0, TgsValues.Scalar(TgsJson.Get(modifier, "r"), frame)), _token),
                    "op" => path.Offset((float)TgsValues.Scalar(TgsJson.Get(modifier, "a"), frame), TgsJson.Number(modifier, "lj", 1) switch
                    {
                        2 => StrokeJoin.Round,
                        3 => StrokeJoin.Bevel,
                        _ => StrokeJoin.Miter
                    }, (float)Math.Max(1, TgsValues.Scalar(TgsJson.Get(modifier, "ml"), frame, 4)), _token),
                    "pb" => Pucker(path, (float)TgsValues.Scalar(TgsJson.Get(modifier, "a"), frame) / 100),
                    "zz" => ZigZag(path, modifier, frame),
                    _ => path
                };
                if (!ReferenceEquals(path, changed))
                {
                    Geometry(changed);
                    paths[i] = (paths[i].Index, changed);
                }
            }
        }

        private void MergePaths(List<(int Index, VectorPath Path)> paths, JsonElement modifier, int index)
        {
            var affected = paths.Where(p => p.Index < index).ToArray();
            if (affected.Length == 0)
            {
                return;
            }

            int mode = (int)TgsJson.Number(modifier, "mm", 1);
            VectorPath merged = new VectorPath().Append(affected[^1].Path);
            for (int i = affected.Length - 2; i >= 0; i--)
            {
                _token.ThrowIfCancellationRequested();
                if (mode == 1)
                {
                    merged.Append(affected[i].Path);
                }
                else
                {
                    PathBooleanOperation operation = mode switch
                    {
                        3 => PathBooleanOperation.Difference,
                        4 => PathBooleanOperation.Intersection,
                        5 => PathBooleanOperation.ExclusiveOr,
                        _ => PathBooleanOperation.Union
                    };
                    merged = merged.Combine(affected[i].Path, operation, cancellationToken: _token);
                }
            }

            paths.RemoveAll(p => p.Index < index);
            Geometry(merged);
            paths.Insert(0, (index - 1, merged));
        }

        private static Vector2 ElementEnd(VectorPath.PathElement element) => element.Kind switch
        {
            3 => element.Third,
            2 => element.Second,
            _ => element.First
        };
        private VectorPath Pucker(VectorPath path, float amount)
        {
            var result = new VectorPath();
            int start = 0;
            while (start < path.Elements.Count)
            {
                int end = start + 1;
                while (end < path.Elements.Count && path.Elements[end].Kind != 0)
                {
                    end++;
                }

                var elements = path.Elements.GetRange(start, end - start);
                PuckerContour(result, elements, amount);
                start = end;
            }

            return result;
        }

        private void PuckerContour(VectorPath result, List<VectorPath.PathElement> elements, float amount)
        {
            var vertices = elements.Where(e => e.Kind != 4).Select(e => ElementEnd(e)).ToList();
            if (vertices.Count > 1 && vertices[0] == vertices[^1])
            {
                vertices.RemoveAt(vertices.Count - 1);
            }

            Vector2 center = vertices.Aggregate(Vector2.Zero, (sum, p) => sum + p) / Math.Max(1, vertices.Count);
            Vector2 Vertex(Vector2 point) => center + ((point - center) * (1 - amount));
            Vector2 Tangent(Vector2 point) => center + ((point - center) * (1 + amount));
            Vector2 previous = elements[0].First, first = previous;
            foreach (var element in elements)
            {
                _token.ThrowIfCancellationRequested();
                if (element.Kind == 0)
                {
                    Vector2 point = Vertex(element.First);
                    result.MoveTo(point.X, point.Y);
                    continue;
                }

                if (element.Kind == 4)
                {
                    if (previous != first)
                    {
                        Cubic(previous, first, first);
                    }

                    result.Close();
                    continue;
                }

                Vector2 last = ElementEnd(element);
                Vector2 a = element.Kind == 1 ? previous : element.First;
                Vector2 b = element.Kind == 3 ? element.Second : element.Kind == 2 ? last + ((element.First - last) * (2f / 3)) : last;
                if (element.Kind == 2)
                {
                    a = previous + ((element.First - previous) * (2f / 3));
                }

                Cubic(a, b, last);
                previous = last;
            }

            void Cubic(Vector2 a, Vector2 b, Vector2 endPoint)
            {
                a = Tangent(a);
                b = Tangent(b);
                endPoint = Vertex(endPoint);
                result.CubicTo(a.X, a.Y, b.X, b.Y, endPoint.X, endPoint.Y);
            }
        }

        private sealed record ZigCurve(Vector2 A, Vector2 B, Vector2 C, Vector2 D);
        private sealed record ZigVertex(Vector2 Point, Vector2 In, Vector2 Out);
        private VectorPath ZigZag(VectorPath path, JsonElement modifier, double frame)
        {
            float size = (float)TgsValues.Scalar(TgsJson.Get(modifier, "s"), frame);
            if (size == 0)
            {
                return new VectorPath().Append(path);
            }

            int ridges = (int)Math.Round(TgsValues.Scalar(TgsJson.Get(modifier, "r"), frame));
            if (ridges < 0 || ridges > 4096)
            {
                throw new InvalidDataException("Lottie zigzag exceeds 4096 ridges per segment.");
            }

            JsonElement pointType = TgsJson.Get(modifier, "pt");
            bool smooth = (pointType.ValueKind == JsonValueKind.Object ? TgsValues.Scalar(pointType, frame, 1) : TgsJson.Number(pointType, 1)) == 2;
            var result = new VectorPath();
            var curves = new List<ZigCurve>();
            Vector2 first = default, previous = default;
            foreach (var element in path.Elements)
            {
                _token.ThrowIfCancellationRequested();
                if (element.Kind == 0)
                {
                    Emit(false);
                    first = previous = element.First;
                }
                else if (element.Kind == 4)
                {
                    if (previous != first)
                    {
                        curves.Add(new ZigCurve(previous, previous, first, first));
                    }

                    Emit(true);
                }
                else
                {
                    Vector2 end = ElementEnd(element);
                    Vector2 a = element.Kind == 1 ? previous : element.Kind == 2 ? previous + ((element.First - previous) * (2f / 3)) : element.First;
                    Vector2 b = element.Kind == 1 ? end : element.Kind == 2 ? end + ((element.First - end) * (2f / 3)) : element.Second;
                    curves.Add(new ZigCurve(previous, a, b, end));
                    previous = end;
                }
            }

            Emit(false);
            return result;
            void Emit(bool closed)
            {
                if (curves.Count == 0)
                {
                    return;
                }

                ZigContour(result, curves, closed, size, ridges, smooth);
                curves.Clear();
            }
        }

        private void ZigContour(VectorPath output, List<ZigCurve> curves, bool closed, float size, int ridges, bool smooth)
        {
            var vertices = new List<ZigVertex>();
            float direction = 1;
            Vector2 Unit(Vector2 v) => v.LengthSquared() < 1e-12 ? Vector2.UnitY : Vector2.Normalize(v);
            ZigVertex Vertex(Vector2 point, Vector2 tangent, float sign, float before, float after)
            {
                tangent = Unit(tangent);
                point += new Vector2(-tangent.Y, tangent.X) * size * sign;
                return new ZigVertex(point, point - (tangent * before), point + (tangent * after));
            }

            Vector2 startTangent = ZigCorner(curves, 0, closed, ridges, smooth, out float incoming, out float outgoing);
            vertices.Add(Vertex(curves[0].A, startTangent, direction, incoming, outgoing));
            for (int i = 0; i < curves.Count; i++)
            {
                ZigCurve c = curves[i];
                for (int step = 1; step <= ridges; step++)
                {
                    _token.ThrowIfCancellationRequested();
                    direction = -direction;
                    float t = step / (float)(ridges + 1), u = 1 - t;
                    Vector2 point = (c.A * u * u * u) + (c.B * 3 * u * u * t) + (c.C * 3 * u * t * t) + (c.D * t * t * t);
                    Vector2 tangent = ((c.B - c.A) * u * u) + ((c.C - c.B) * 2 * u * t) + ((c.D - c.C) * t * t);
                    float handle = smooth ? Vector2.Distance(c.A, c.D) / ((ridges + 1) * 2) : 0;
                    vertices.Add(Vertex(point, tangent, direction, handle, handle));
                    if (vertices.Count > _document.Limits.MaximumPathCommands)
                    {
                        throw new InvalidDataException("Lottie zigzag exceeds MaximumPathCommands.");
                    }
                }

                direction = -direction;
                Vector2 corner = ZigCorner(curves, i + 1, closed, ridges, smooth, out incoming, out outgoing);
                vertices.Add(Vertex(c.D, corner, direction, incoming, outgoing));
            }

            output.MoveTo(vertices[0].Point.X, vertices[0].Point.Y);
            for (int i = 1; i < vertices.Count; i++)
            {
                ZigVertex a = vertices[i - 1], b = vertices[i];
                if (smooth)
                {
                    output.CubicTo(a.Out.X, a.Out.Y, b.In.X, b.In.Y, b.Point.X, b.Point.Y);
                }
                else
                {
                    output.LineTo(b.Point.X, b.Point.Y);
                }
            }

            if (closed)
            {
                output.Close();
            }
        }

        private static Vector2 ZigCorner(List<ZigCurve> curves, int index, bool closed, int ridges, bool smooth, out float before, out float after)
        {
            Vector2 current = index == curves.Count ? curves[^1].D : curves[index].A;
            Vector2 previous = index == 0 ? closed ? curves[^1].A : curves[0].D : curves[index - 1].A;
            Vector2 next = index == curves.Count ? closed ? curves[0].D : curves[^1].A : curves[index].D;
            before = smooth ? Vector2.Distance(previous, current) / ((ridges + 1) * 2) : 0;
            after = smooth ? Vector2.Distance(next, current) / ((ridges + 1) * 2) : 0;
            return next - previous;
        }
    }
}
