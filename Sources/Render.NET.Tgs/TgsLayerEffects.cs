using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;

namespace Render.NET.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private static IEnumerable<JsonElement> ExpandEffects(JsonElement array)
        {
            foreach (JsonElement effect in TgsJson.Array(array))
            {
                if (TgsJson.Number(effect, "en", 1) == 0 || TgsEffectInfo.IsParameterGroup(effect) || TgsEffectInfo.IsIdentity(effect))
                {
                    continue;
                }

                if (TgsEffectInfo.IsContainer(effect))
                {
                    foreach (JsonElement nested in ExpandEffects(TgsJson.Get(effect, "ef")))
                    {
                        yield return nested;
                    }
                }
                else
                {
                    yield return effect;
                }
            }
        }

        private void FlushEffect(XElement content, XElement filter)
        {
            if (!filter.HasElements)
            {
                return;
            }

            _defs.Add(filter);
            XElement? group = new XElement("g", new XAttribute("filter", "url(#" + filter.Attribute("id")!.Value + ")"), content.Nodes().ToArray());
            content.ReplaceNodes(group);
        }

        private static void LevelsEffect(XElement filter, Func<int, double, double> scalar)
        {
            string Table(int offset)
            {
                double black = scalar(offset, 0), white = scalar(offset + 1, 1), gamma = Math.Max(.0001, scalar(offset + 2, 1));
                double low = scalar(offset + 3, 0), high = scalar(offset + 4, 1);
                return string.Join(" ", Enumerable.Range(0, 256).Select(i =>
                {
                    double t = white == black ? (i / 255d >= white ? 1 : 0) : Math.Clamp(((i / 255d) - black) / (white - black), 0, 1);
                    return F(low + ((high - low) * Math.Pow(t, 1 / gamma)));
                }));
            }

            filter.Add(new XElement("feComponentTransfer", Enumerable.Range(0, 4).Select(i => new XElement("feFunc" + "RGBA"[i], new XAttribute("type", "table"), new XAttribute("tableValues", Table(10 + (i * 7)))))));
            string combined = Table(3);
            filter.Add(new XElement("feComponentTransfer", "RGB".Select(c => new XElement("feFunc" + c, new XAttribute("type", "table"), new XAttribute("tableValues", combined)))));
        }

        private static void TransformEffect(XElement content, JsonElement effect, double frame)
        {
            JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
            JsonElement Value(int index) => index < values.Length ? TgsJson.Get(values[index], "v") : default;
            double Scalar(int index, double fallback = 0) => TgsValues.Scalar(Value(index), frame, fallback);
            Vector2 anchor = TgsValues.Point(Value(0), frame), position = TgsValues.Point(Value(1), frame);
            float height = (float)Scalar(3, 100) / 100, width = Scalar(2, 1) == 1 ? height : (float)Scalar(4, 100) / 100;
            float skew = (float)((Scalar(5) * Math.PI) / 180), axis = (float)(((Scalar(6) + 90) * Math.PI) / 180);
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(-anchor) * Matrix3x2.CreateScale(width, height) * Matrix3x2.CreateRotation(-axis) * new Matrix3x2(1, 0, -MathF.Tan(skew), 1, 0, 0) * Matrix3x2.CreateRotation(axis) * Matrix3x2.CreateRotation((float)((Scalar(7) * Math.PI) / 180)) * Matrix3x2.CreateTranslation(position);
            XElement? group = new XElement("g", new XAttribute("transform", Matrix(matrix)), new XAttribute("opacity", F(Math.Clamp(Scalar(8, 100) / 100, 0, 1))), content.Nodes().ToArray());
            content.ReplaceNodes(group);
        }

        private void StrokeEffect(JsonElement layer, XElement content, XElement? maskedSource, JsonElement effect, double frame)
        {
            JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
            double Scalar(int index, double fallback = 0) => index < values.Length ? TgsValues.Scalar(TgsJson.Get(values[index], "v"), frame, fallback) : fallback;
            JsonElement[] masks = TgsJson.Array(TgsJson.Get(layer, "masksProperties"));
            int paint = (int)Scalar(10, 1), selected = (int)Scalar(0, 1) - 1;
            bool all = Scalar(1) == 1;
            XElement? strokes = new XElement("g", new XAttribute("fill", "none"), new XAttribute("stroke-linecap", "round"), new XAttribute("stroke-linejoin", "round"), new XAttribute("stroke-width", F(Math.Max(0, Scalar(4, 2)) * 2)), new XAttribute("opacity", F(Math.Clamp(Scalar(6, 1), 0, 1))));
            double[] ink = values.Length > 3 ? TgsValues.Vector(TgsJson.Get(values[3], "v"), frame, new[] { 1d, 1d, 1d }) : new[]
            {
                1d,
                1d,
                1d
            };
            strokes.SetAttributeValue("stroke", paint == 3 ? "white" : Color(ink));
            double gap = Math.Max(0, (Scalar(4, 2) * 2 * Scalar(9)) / 100);
            if (gap > 0)
            {
                strokes.SetAttributeValue("stroke-dasharray", "1 " + F(gap));
            }

            for (int i = 0; i < masks.Length; i++)
            {
                if (!all && i != selected)
                {
                    continue;
                }

                VectorPath path = Bezier(TgsJson.Get(masks[i], "pt"), frame);
                float start = (float)Math.Clamp(Scalar(7) / 100, 0, 1), end = (float)Math.Clamp(Scalar(8, 100) / 100, 0, 1);
                path = path.Trim(Math.Min(start, end), Math.Max(start, end), cancellationToken: _token);
                Geometry(path);
                strokes.Add(PathElement(path));
            }

            ApplyStrokeEffect(content, maskedSource, strokes, paint);
        }

        private void ApplyStrokeEffect(XElement content, XElement? maskedSource, XElement strokes, int paint)
        {
            if (paint == 3)
            {
                string id = Id();
                XElement mask = FullMask(id, "alpha");
                mask.Add(strokes);
                _defs.Add(mask);
                XElement? group = new XElement("g", new XAttribute("mask", "url(#" + id + ")"), content.Nodes().ToArray());
                content.ReplaceNodes(group);
            }
            else if (paint is 1 or 2)
            {
                maskedSource?.Attribute("mask")?.Remove();
                if (paint == 2)
                {
                    content.RemoveNodes();
                }

                content.Add(strokes);
            }
            else
            {
                throw new InvalidDataException("Invalid Lottie Stroke paint style.");
            }
        }
    }
}
