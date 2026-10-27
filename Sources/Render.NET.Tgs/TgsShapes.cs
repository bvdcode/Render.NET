using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using Render.NET.Svg;

namespace Render.NET.Tgs
{
    internal sealed partial class TgsSceneBuilder
    {
        private XElement Shapes(JsonElement array, double frame, int depth)
        {
            Count(depth);
            JsonElement[] items = TgsJson.Array(array);
            CheckTrimScopes(items);
            var group = new XElement("g");
            JsonElement transform = items.LastOrDefault(x => TgsJson.Text(x, "ty") == "tr" && !TgsJson.Flag(x, "hd"));
            group.SetAttributeValue("transform", Matrix(Transform(transform, frame)));
            group.SetAttributeValue("opacity", F(Math.Clamp(TgsValues.Scalar(TgsJson.Get(transform, "o"), frame, 100) / 100, 0, 1)));
            for (int i = items.Length - 1; i >= 0; i--)
            {
                Count(depth);
                if (TgsJson.Flag(items[i], "hd"))
                {
                    continue;
                }

                string type = TgsJson.Text(items[i], "ty");
                if (type == "gr")
                {
                    group.Add(Shapes(TgsJson.Get(items[i], "it"), frame, depth + 1));
                }
                else if (type is "fl" or "st" or "gf" or "gs")
                {
                    VectorPath path = Collect(items, i, frame, depth);
                    Geometry(path);
                    XElement paint = PathElement(path);
                    ApplyStyle(paint, items[i], frame);
                    group.Add(paint);
                }
            }

            return group;
        }

        private void CheckTrimScopes(JsonElement[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (TgsJson.Text(items[i], "ty") != "tm" || TgsJson.Flag(items[i], "hd"))
                {
                    continue;
                }

                JsonElement[] before = items[..i];
                if (before.Any(x => TgsJson.Text(x, "ty") == "gr" && ContainsStyle(TgsJson.Get(x, "it"))))
                {
                    _warnings.Add("Lottie trim across separately styled subgroups");
                }

                if (TgsJson.Number(items[i], "m", 1) == 2 && InterleavedStyles(before))
                {
                    _warnings.Add("Lottie sequential trim across interleaved styles");
                }
            }
        }

        private static bool InterleavedStyles(JsonElement[] items)
        {
            bool style = false;
            foreach (JsonElement item in items)
            {
                string type = TgsJson.Text(item, "ty");
                if (type is "fl" or "st" or "gf" or "gs")
                {
                    style = true;
                }
                else if (style && type is "sh" or "el" or "rc" or "sr" or "gr")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsStyle(JsonElement array) => TgsJson.Array(array).Any(x => TgsJson.Text(x, "ty") is "fl" or "st" or "gf" or "gs" || (TgsJson.Text(x, "ty") == "gr" && ContainsStyle(TgsJson.Get(x, "it"))));
        private VectorPath Collect(JsonElement[] items, int end, double frame, int depth)
        {
            var paths = new List<(int Index, VectorPath Path)>();
            for (int i = 0; i < end; i++)
            {
                Count(depth);
                if (TgsJson.Flag(items[i], "hd"))
                {
                    continue;
                }

                string type = TgsJson.Text(items[i], "ty");
                if (type == "gr")
                {
                    JsonElement[] nested = TgsJson.Array(TgsJson.Get(items[i], "it"));
                    JsonElement transform = nested.LastOrDefault(x => TgsJson.Text(x, "ty") == "tr" && !TgsJson.Flag(x, "hd"));
                    if (TgsValues.Scalar(TgsJson.Get(transform, "o"), frame, 100) != 100)
                    {
                        _warnings.Add("Lottie styles spanning translucent subgroups");
                    }

                    paths.Add((i, Collect(nested, nested.Length, frame, depth + 1).Transform(Transform(transform, frame))));
                }
                else
                {
                    VectorPath? path = Shape(items[i], frame);
                    if (path != null)
                    {
                        Geometry(path);
                        paths.Add((i, path));
                    }
                }
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (TgsJson.Text(items[i], "ty") == "tm" && !TgsJson.Flag(items[i], "hd"))
                {
                    Trim(paths, items[i], i, frame);
                }
            }

            var result = new VectorPath();
            for (int i = paths.Count - 1; i >= 0; i--)
            {
                result.Append(paths[i].Path);
            }

            return result;
        }

        private void Trim(List<(int Index, VectorPath Path)> paths, JsonElement modifier, int index, double frame)
        {
            float start = (float)Math.Clamp(TgsValues.Scalar(TgsJson.Get(modifier, "s"), frame) / 100, 0, 1);
            float end = (float)Math.Clamp(TgsValues.Scalar(TgsJson.Get(modifier, "e"), frame, 100) / 100, 0, 1);
            if (start > end)
            {
                (start, end) = (end, start);
            }

            float offset = (float)(TgsValues.Scalar(TgsJson.Get(modifier, "o"), frame) / 360);
            if (TgsJson.Number(modifier, "m", 1) == 1)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (paths[i].Index < index)
                    {
                        paths[i] = (paths[i].Index, paths[i].Path.Trim(start, end, offset, cancellationToken: _token));
                    }
                }
            }
            else
            {
                var combined = new VectorPath();
                for (int i = paths.Count - 1; i >= 0; i--)
                {
                    if (paths[i].Index < index)
                    {
                        combined.Append(paths[i].Path);
                        paths.RemoveAt(i);
                    }
                }

                paths.Insert(0, (index - 1, combined.Trim(start, end, offset, cancellationToken: _token)));
            }
        }

        private VectorPath? Shape(JsonElement item, double frame) => TgsJson.Text(item, "ty") switch
        {
            "sh" => Bezier(TgsJson.Get(item, "ks"), frame),
            "el" => Ellipse(item, frame),
            "rc" => Rectangle(item, frame),
            "sr" => Star(item, frame),
            _ => null
        };
        private VectorPath Bezier(JsonElement property, double frame)
        {
            TgsKeyPair pair = TgsValues.Keys(property, frame);
            JsonElement a = Unwrap(pair.Start), b = Unwrap(pair.End);
            JsonElement[] av = TgsJson.Array(TgsJson.Get(a, "v")), bv = TgsJson.Array(TgsJson.Get(b, "v"));
            if (av.Length != bv.Length)
            {
                throw new NotSupportedException("Lottie morph paths with different vertex counts.");
            }

            JsonElement[] ai = TgsJson.Array(TgsJson.Get(a, "i")), ao = TgsJson.Array(TgsJson.Get(a, "o"));
            JsonElement[] bi = TgsJson.Array(TgsJson.Get(b, "i")), bo = TgsJson.Array(TgsJson.Get(b, "o"));
            if (ai.Length != av.Length || ao.Length != av.Length || bi.Length != av.Length || bo.Length != av.Length)
            {
                throw new InvalidDataException("Lottie path tangent counts do not match vertices.");
            }

            var path = new VectorPath();
            if (av.Length == 0)
            {
                return path;
            }

            double t = TgsValues.Ease(pair.Key, pair.Progress);
            Vector2 Mix(JsonElement x, JsonElement y) => Vector2.Lerp(RawPoint(x), RawPoint(y), (float)t);
            Vector2 Vertex(int i) => Mix(av[i], bv[i]);
            Vector2 start = Vertex(0);
            path.MoveTo(start.X, start.Y);
            int segments = TgsJson.Flag(a, "c") ? av.Length : av.Length - 1;
            for (int i = 0; i < segments; i++)
            {
                _token.ThrowIfCancellationRequested();
                int next = (i + 1) % av.Length;
                Vector2 p = Vertex(i) + Mix(ao[i], bo[i]), q = Vertex(next) + Mix(ai[next], bi[next]), end = Vertex(next);
                path.CubicTo(p.X, p.Y, q.X, q.Y, end.X, end.Y);
            }

            if (TgsJson.Flag(a, "c"))
            {
                path.Close();
            }

            return path;
        }

        private static JsonElement Unwrap(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 ? value[0] : value;
        private static Vector2 RawPoint(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 2)
            {
                throw new InvalidDataException("Invalid Lottie path vertex.");
            }

            double x = TgsJson.Number(value[0]), y = TgsJson.Number(value[1]);
            if (Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue)
            {
                throw new InvalidDataException("Lottie vertex exceeds coordinate range.");
            }

            return new Vector2((float)x, (float)y);
        }

        private static VectorPath Ellipse(JsonElement item, double frame)
        {
            Vector2 p = TgsValues.Point(TgsJson.Get(item, "p"), frame), r = TgsValues.Point(TgsJson.Get(item, "s"), frame) / 2;
            if (r.X <= 0 || r.Y <= 0)
            {
                return new VectorPath();
            }

            bool reverse = TgsJson.Number(item, "d", 1) == 3;
            string sweep = reverse ? "0" : "1";
            return SvgPathParser.Parse($"M{F(p.X)} {F(p.Y - r.Y)}A{F(r.X)} {F(r.Y)} 0 1 {sweep} {F(p.X)} {F(p.Y + r.Y)}A{F(r.X)} {F(r.Y)} 0 1 {sweep} {F(p.X)} {F(p.Y - r.Y)}Z");
        }

        private static VectorPath Rectangle(JsonElement item, double frame)
        {
            Vector2 p = TgsValues.Point(TgsJson.Get(item, "p"), frame), size = TgsValues.Point(TgsJson.Get(item, "s"), frame);
            if (size.X <= 0 || size.Y <= 0)
            {
                return new VectorPath();
            }

            float r = (float)Math.Clamp(TgsValues.Scalar(TgsJson.Get(item, "r"), frame), 0, Math.Min(size.X, size.Y) / 2);
            float left = p.X - (size.X / 2), right = p.X + (size.X / 2), top = p.Y - (size.Y / 2), bottom = p.Y + (size.Y / 2);
            var path = new VectorPath();
            bool reverse = TgsJson.Number(item, "d", 1) == 3;
            Vector2[] corners = reverse ? new[]
            {
                new Vector2(right, top),
                new Vector2(left, top),
                new Vector2(left, bottom),
                new Vector2(right, bottom)
            }

            : new[]
            {
                new Vector2(right, top),
                new Vector2(right, bottom),
                new Vector2(left, bottom),
                new Vector2(left, top)
            };
            for (int i = 0; i < 4; i++)
            {
                Vector2 previous = corners[(i + 3) % 4], current = corners[i], next = corners[(i + 1) % 4];
                Vector2 a = current + (Vector2.Normalize(previous - current) * r), b = current + (Vector2.Normalize(next - current) * r);
                if (i == 0)
                {
                    path.MoveTo(a.X, a.Y);
                }
                else
                {
                    path.LineTo(a.X, a.Y);
                }

                float k = 0.551915f;
                Vector2 c1 = a + ((current - a) * k), c2 = b + ((current - b) * k);
                path.CubicTo(c1.X, c1.Y, c2.X, c2.Y, b.X, b.Y);
            }

            return path.Close();
        }

        private VectorPath Star(JsonElement item, double frame)
        {
            double points = TgsValues.Scalar(TgsJson.Get(item, "pt"), frame, 5);
            if (points < 2 || points > 10_000)
            {
                throw new InvalidDataException("Invalid Lottie polystar point count.");
            }

            if (points != Math.Floor(points))
            {
                _warnings.Add("Lottie fractional polystars");
            }

            if (TgsValues.Scalar(TgsJson.Get(item, "os"), frame) != 0 || TgsValues.Scalar(TgsJson.Get(item, "is"), frame) != 0)
            {
                _warnings.Add("Lottie rounded polystars");
            }

            Vector2 p = TgsValues.Point(TgsJson.Get(item, "p"), frame);
            bool star = TgsJson.Number(item, "sy", 1) == 1;
            int count = (int)points * (star ? 2 : 1);
            double rotation = ((TgsValues.Scalar(TgsJson.Get(item, "r"), frame) - 90) * Math.PI) / 180;
            double outer = TgsValues.Scalar(TgsJson.Get(item, "or"), frame), inner = TgsValues.Scalar(TgsJson.Get(item, "ir"), frame);
            var path = new VectorPath();
            for (int i = 0; i < count; i++)
            {
                _token.ThrowIfCancellationRequested();
                double angle = rotation + (((TgsJson.Number(item, "d", 1) == 3 ? -1 : 1) * 2 * Math.PI * i) / count);
                double radius = star && i % 2 == 1 ? inner : outer;
                float x = p.X + (float)(Math.Cos(angle) * radius), y = p.Y + (float)(Math.Sin(angle) * radius);
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            return path.Close();
        }
    }
}
