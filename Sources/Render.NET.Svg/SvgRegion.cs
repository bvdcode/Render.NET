using System;
using System.Numerics;

namespace Render.NET.Svg
{
    internal record SvgRegion(string X, string Y, string Width, string Height, bool ObjectBox, float ViewWidth, float ViewHeight)
    {
        internal VectorPath Path((float X, float Y, float Width, float Height) box)
        {
            float x = SvgLength.Parse(X, ObjectBox ? 1 : ViewWidth), y = SvgLength.Parse(Y, ObjectBox ? 1 : ViewHeight);
            float width = SvgLength.Parse(Width, ObjectBox ? 1 : ViewWidth), height = SvgLength.Parse(Height, ObjectBox ? 1 : ViewHeight);
            if (ObjectBox)
            {
                x = box.X + (x * box.Width);
                y = box.Y + (y * box.Height);
                width *= box.Width;
                height *= box.Height;
            }

            return width <= 0 || height <= 0 ? new VectorPath() : new VectorPath().Rectangle(x, y, width, height);
        }
    }
}
