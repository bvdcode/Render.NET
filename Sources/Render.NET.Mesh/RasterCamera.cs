using System;
using System.Numerics;

namespace Render.NET.Mesh
{
    internal class RasterCamera
    {
        private readonly Vector3 _right;
        private readonly Vector3 _up;
        private readonly Vector3 _towardCamera;
        private readonly float _centerX;
        private readonly float _centerY;
        internal int Width { get; }
        internal int Height { get; }
        internal float Scale { get; }
        internal Vector3 ViewDirection => _towardCamera;

        internal RasterCamera(PreparedMesh mesh, Vector3 direction, Vector3 up, int width, int height, float padding)
        {
            Width = width;
            Height = height;
            _towardCamera = Vector3.Normalize(direction);
            _right = Vector3.Normalize(Vector3.Cross(up, _towardCamera));
            _up = Vector3.Cross(_towardCamera, _right);
            Vector2 minimum = new(float.PositiveInfinity);
            Vector2 maximum = new(float.NegativeInfinity);
            foreach (int index in mesh.Indices)
            {
                Vector3 vertex = mesh.Positions[index];
                Vector2 position = new(Vector3.Dot(vertex, _right), Vector3.Dot(vertex, _up));
                minimum = Vector2.Min(minimum, position);
                maximum = Vector2.Max(maximum, position);
            }

            _centerX = (minimum.X + maximum.X) * 0.5f;
            _centerY = (minimum.Y + maximum.Y) * 0.5f;
            Scale = MathF.Min(width / MathF.Max(maximum.X - minimum.X, 0.000001f),
                height / MathF.Max(maximum.Y - minimum.Y, 0.000001f)) * (1 - 2 * padding);
        }

        internal Vector3 Project(Vector3 point) => new(
            (Vector3.Dot(point, _right) - _centerX) * Scale + Width * 0.5f,
            Height * 0.5f - (Vector3.Dot(point, _up) - _centerY) * Scale,
            -Vector3.Dot(point, _towardCamera));

        internal Vector3 Unproject(float x, float y, float depth) =>
            _right * ((x - Width * 0.5f) / Scale + _centerX) +
            _up * ((Height * 0.5f - y) / Scale + _centerY) - _towardCamera * depth;
    }
}
