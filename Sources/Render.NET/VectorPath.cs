using System;
using System.Collections.Generic;
using System.Numerics;

namespace Render.NET
{
    /// <summary>The rule used to determine the inside of a path.</summary>
    public enum FillRule
    {
        /// <summary>Use the signed number of edge crossings.</summary>
        NonZero,
        /// <summary>Alternate between outside and inside at each edge crossing.</summary>
        EvenOdd
    }

    /// <summary>A mutable vector path. Coordinates may use any consistent unit.</summary>
    public sealed partial class VectorPath
    {
        internal List<PathElement> Elements { get; } = new List<PathElement>();

        /// <summary>Gets the number of stored path commands.</summary>
        public int CommandCount => Elements.Count;

        /// <summary>Starts a new contour.</summary>
        public VectorPath MoveTo(float x, float y) => Add(0, new Vector2(x, y));

        /// <summary>Adds a straight segment.</summary>
        public VectorPath LineTo(float x, float y) => Add(1, new Vector2(x, y));

        /// <summary>Adds a quadratic Bezier segment.</summary>
        public VectorPath QuadraticTo(float x1, float y1, float x2, float y2)
        {
            return Add(2, new Vector2(x1, y1), new Vector2(x2, y2));
        }

        /// <summary>Adds a cubic Bezier segment.</summary>
        public VectorPath CubicTo(float x1, float y1, float x2, float y2, float x3, float y3)
        {
            return Add(3, new Vector2(x1, y1), new Vector2(x2, y2), new Vector2(x3, y3));
        }

        /// <summary>Closes the current contour.</summary>
        public VectorPath Close() => Add(4, Vector2.Zero);

        /// <summary>Adds a closed rectangle.</summary>
        public VectorPath Rectangle(float x, float y, float width, float height)
        {
            return MoveTo(x, y).LineTo(x + width, y).LineTo(x + width, y + height).LineTo(x, y + height).Close();
        }

        /// <summary>Appends a copy of another path.</summary>
        public VectorPath Append(VectorPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            Elements.AddRange(path.Elements);
            return this;
        }

        /// <summary>Returns an independently transformed copy.</summary>
        public VectorPath Transform(Matrix3x2 matrix)
        {
            var result = new VectorPath();
            foreach (PathElement element in Elements)
            {
                result.Elements.Add(new PathElement(element.Kind,
                    Vector2.Transform(element.First, matrix),
                    Vector2.Transform(element.Second, matrix),
                    Vector2.Transform(element.Third, matrix)));
            }

            return result;
        }

        private VectorPath Add(int kind, Vector2 first, Vector2 second = default, Vector2 third = default)
        {
            Check(first);
            Check(second);
            Check(third);
            Elements.Add(new PathElement(kind, first, second, third));
            return this;
        }

        private static void Check(Vector2 point)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(point), "Path coordinates must be finite.");
            }
        }

        internal readonly record struct PathElement(int Kind, Vector2 First, Vector2 Second, Vector2 Third);
    }
}
