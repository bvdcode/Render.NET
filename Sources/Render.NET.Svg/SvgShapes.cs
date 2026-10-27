using System;
using System.IO;
using System.Numerics;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal sealed partial class SvgSceneBuilder
    {
        private VectorPath? Shape(XElement e) => e.Name.LocalName switch
        {
            "path" => SvgPathParser.Parse((string?)e.Attribute("d") ?? "", _limits.MaximumPathCommands, _token),
            "rect" => Rectangle(e),
            "circle" => Ellipse(Length(e, "cx", 0, _width), Length(e, "cy", 0, _height), Length(e, "r", 0, Diagonal), Length(e, "r", 0, Diagonal)),
            "ellipse" => Ellipse(Length(e, "cx", 0, _width), Length(e, "cy", 0, _height), Length(e, "rx", 0, _width), Length(e, "ry", 0, _height)),
            "line" => new VectorPath().MoveTo(Length(e, "x1", 0, _width), Length(e, "y1", 0, _height)).LineTo(Length(e, "x2", 0, _width), Length(e, "y2", 0, _height)),
            "polygon" => Poly(e, true),
            "polyline" => Poly(e, false),
            _ => null
        };
        private float Diagonal => MathF.Sqrt(((_width * _width) + (_height * _height)) / 2);

        private VectorPath Rectangle(XElement e)
        {
            float x = Length(e, "x", 0, _width), y = Length(e, "y", 0, _height);
            float w = Length(e, "width", 0, _width), h = Length(e, "height", 0, _height);
            if (w < 0 || h < 0)
            {
                throw new InvalidDataException("Negative SVG rectangle size.");
            }

            if (w == 0 || h == 0)
            {
                return new VectorPath();
            }

            float rx = Length(e, "rx", -1, _width), ry = Length(e, "ry", -1, _height);
            if (rx < 0)
            {
                rx = Math.Max(0, ry);
            }

            if (ry < 0)
            {
                ry = rx;
            }

            rx = Math.Min(rx, w / 2);
            ry = Math.Min(ry, h / 2);
            if (rx == 0 || ry == 0)
            {
                return new VectorPath().Rectangle(x, y, w, h);
            }

            var path = new VectorPath().MoveTo(x + rx, y).LineTo((x + w) - rx, y);
            Arc(new Vector2((x + w) - rx, y), new Vector2(x + w, y + ry));
            path.LineTo(x + w, (y + h) - ry);
            Arc(new Vector2(x + w, (y + h) - ry), new Vector2((x + w) - rx, y + h));
            path.LineTo(x + rx, y + h);
            Arc(new Vector2(x + rx, y + h), new Vector2(x, (y + h) - ry));
            path.LineTo(x, y + ry);
            Arc(new Vector2(x, y + ry), new Vector2(x + rx, y));
            return path.Close();
            void Arc(Vector2 a, Vector2 b) => SvgArc.Append(path, a, b, rx, ry, 0, false, true);
        }

        private static VectorPath Ellipse(float cx, float cy, float rx, float ry)
        {
            if (rx < 0 || ry < 0)
            {
                throw new InvalidDataException("Negative SVG ellipse radius.");
            }

            var path = new VectorPath();
            if (rx == 0 || ry == 0)
            {
                return path;
            }

            path.MoveTo(cx + rx, cy);
            SvgArc.Append(path, new Vector2(cx + rx, cy), new Vector2(cx - rx, cy), rx, ry, 0, false, true);
            SvgArc.Append(path, new Vector2(cx - rx, cy), new Vector2(cx + rx, cy), rx, ry, 0, false, true);
            return path.Close();
        }

        private static VectorPath Poly(XElement e, bool close)
        {
            float[] values = SvgNumbers.List((string?)e.Attribute("points") ?? "");
            if (values.Length % 2 != 0)
            {
                throw new InvalidDataException("Odd SVG polygon coordinate count.");
            }

            var result = new VectorPath();
            if (values.Length == 0)
            {
                return result;
            }

            result.MoveTo(values[0], values[1]);
            for (int i = 2; i < values.Length; i += 2)
            {
                result.LineTo(values[i], values[i + 1]);
            }

            return close ? result.Close() : result;
        }
    }
}
