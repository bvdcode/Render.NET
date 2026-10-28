using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Render.NET.Svg
{
    internal partial class SvgTrueType
    {
        private record GlyphShape(List<FontPoint> Points, List<int> Ends);
        private readonly record struct FontPoint(Vector2 Position, bool OnCurve);
        private GlyphShape ReadGlyph(int glyph, HashSet<int> active, int depth)
        {
            if ((uint)glyph >= _glyphs || depth >= 32 || !active.Add(glyph))
            {
                throw new InvalidDataException("Invalid or cyclic TrueType composite glyph.");
            }

            try
            {
                int table = Table("loca", 0), unit = _locaFormat == 0 ? 2 : 4;
                int start = _locaFormat == 0 ? U16(table + (glyph * unit)) * 2 : Offset(table + (glyph * unit));
                int end = _locaFormat == 0 ? U16(table + ((glyph + 1) * unit)) * 2 : Offset(table + ((glyph + 1) * unit));
                (int Start, int Length) data = _tables["glyf"];
                if (end < start || end > data.Length)
                {
                    throw new InvalidDataException("Invalid TrueType glyph location.");
                }

                if (start == end)
                {
                    return new GlyphShape(new List<FontPoint>(), new List<int>());
                }

                int position = data.Start + start, limit = data.Start + end;
                int contours = S16(position);
                position += 10;
                GlyphShape result = contours >= 0 ? Simple(contours, ref position) : Composite(ref position, active, depth);
                if (position > limit)
                {
                    throw new InvalidDataException("TrueType outline exceeds its glyph data.");
                }

                return result;
            }
            finally
            {
                active.Remove(glyph);
            }
        }

        private GlyphShape Simple(int contours, ref int position)
        {
            var ends = new List<int>(contours);
            for (int i = 0; i < contours; i++)
            {
                int end = U16(position);
                position += 2;
                if (i > 0 && end <= ends[i - 1])
                {
                    throw new InvalidDataException("Invalid TrueType contour endpoints.");
                }

                ends.Add(end);
            }

            int instructions = U16(position);
            position = checked(position + 2 + instructions);
            int count = contours == 0 ? 0 : ends[^1] + 1;
            byte[]? flags = new byte[count];
            for (int i = 0; i < count; i++)
            {
                byte flag = Byte(position++);
                flags[i] = flag;
                if ((flag & 8) != 0)
                {
                    int repeat = Byte(position++);
                    if (repeat > count - i - 1)
                    {
                        throw new InvalidDataException("Invalid repeated TrueType flags.");
                    }

                    while (repeat-- > 0)
                    {
                        flags[++i] = flag;
                    }
                }
            }

            var points = new List<FontPoint>(count);
            int x = 0;
            for (int i = 0; i < count; i++)
            {
                x = checked(x + Coordinate(flags[i], 2, 16, ref position));
                points.Add(new FontPoint(new Vector2(x, 0), (flags[i] & 1) != 0));
            }

            int y = 0;
            for (int i = 0; i < count; i++)
            {
                y = checked(y + Coordinate(flags[i], 4, 32, ref position));
                points[i] = points[i] with
                {
                    Position = new Vector2(points[i].Position.X, y)
                };
            }

            return new GlyphShape(points, ends);
        }

        private int Coordinate(byte flags, int shortMask, int positiveMask, ref int position)
        {
            if ((flags & shortMask) != 0)
            {
                int value = Byte(position++);
                return (flags & positiveMask) != 0 ? value : -value;
            }

            if ((flags & positiveMask) != 0)
            {
                return 0;
            }

            int result = S16(position);
            position += 2;
            return result;
        }

        private GlyphShape Composite(ref int position, HashSet<int> active, int depth)
        {
            GlyphShape? result = new GlyphShape(new List<FontPoint>(), new List<int>());
            int flags;
            do
            {
                flags = U16(position);
                int glyph = U16(position + 2);
                position += 4;
                bool xy = (flags & 2) != 0;
                int a = CompositeArgument(flags, ref position), b = CompositeArgument(flags, ref position);
                Matrix3x2 transform = CompositeTransform(flags, ref position);
                GlyphShape component = ReadGlyph(glyph, active, depth + 1);
                Vector2 offset;
                if (xy)
                {
                    offset = new Vector2(a, b);
                    if ((flags & 2048) != 0)
                    {
                        offset = Vector2.TransformNormal(offset, transform);
                    }

                    if ((flags & 4) != 0)
                    {
                        offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));
                    }
                }
                else
                {
                    if ((uint)a >= result.Points.Count || (uint)b >= component.Points.Count)
                    {
                        throw new InvalidDataException("Invalid composite glyph attachment points.");
                    }

                    offset = result.Points[a].Position - Vector2.Transform(component.Points[b].Position, transform);
                }

                int previous = result.Points.Count;
                if (previous + component.Points.Count > 100_000)
                {
                    throw new InvalidDataException("TrueType composite exceeds 100,000 points.");
                }

                foreach (FontPoint point in component.Points)
                {
                    result.Points.Add(point with { Position = Vector2.Transform(point.Position, transform) + offset });
                }

                foreach (int end in component.Ends)
                {
                    result.Ends.Add(previous + end);
                }
            }
            while ((flags & 32) != 0);
            if ((flags & 256) != 0)
            {
                position += 2 + U16(position);
            }

            return result;
        }

        private int CompositeArgument(int flags, ref int position)
        {
            bool xy = (flags & 2) != 0, words = (flags & 1) != 0;
            int value = words ? xy ? S16(position) : U16(position) : xy ? (sbyte)Byte(position) : Byte(position);
            position += words ? 2 : 1;
            return value;
        }

        private Matrix3x2 CompositeTransform(int flags, ref int position)
        {
            Matrix3x2 transform = Matrix3x2.Identity;
            if ((flags & 8) != 0)
            {
                transform = Matrix3x2.CreateScale(S16(position) / 16384f);
                position += 2;
            }
            else if ((flags & 64) != 0)
            {
                transform = Matrix3x2.CreateScale(S16(position) / 16384f, S16(position + 2) / 16384f);
                position += 4;
            }
            else if ((flags & 128) != 0)
            {
                transform = new Matrix3x2(S16(position) / 16384f, S16(position + 2) / 16384f, S16(position + 4) / 16384f, S16(position + 6) / 16384f, 0, 0);
                position += 8;
            }

            return transform;
        }

        private static VectorPath Path(GlyphShape shape)
        {
            VectorPath? path = new VectorPath();
            int first = 0;
            foreach (int end in shape.Ends)
            {
                FontPoint a = shape.Points[first], z = shape.Points[end];
                Vector2 start = a.OnCurve ? a.Position : z.OnCurve ? z.Position : (a.Position + z.Position) / 2;
                path.MoveTo(start.X, start.Y);
                int position = a.OnCurve ? first + 1 : first, stop = !a.OnCurve && z.OnCurve ? end : end + 1;
                while (position < stop)
                {
                    FontPoint point = shape.Points[position];
                    if (point.OnCurve)
                    {
                        path.LineTo(point.Position.X, point.Position.Y);
                        position++;
                    }
                    else
                    {
                        FontPoint next = position + 1 < stop ? shape.Points[position + 1] : new FontPoint(start, true);
                        Vector2 target = next.OnCurve ? next.Position : (point.Position + next.Position) / 2;
                        path.QuadraticTo(point.Position.X, point.Position.Y, target.X, target.Y);
                        position += next.OnCurve ? 2 : 1;
                    }
                }

                path.Close();
                first = end + 1;
            }

            return path;
        }
    }
}
