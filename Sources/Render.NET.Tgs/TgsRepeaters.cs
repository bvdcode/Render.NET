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
        private XElement RepeatShapes(JsonElement[] items, int index, double frame, int depth)
        {
            JsonElement repeater = items[index];
            int count = RepeaterCount(repeater, frame);
            JsonElement transform = items.LastOrDefault(x => TgsJson.Text(x, "ty") == "tr" && !TgsJson.Flag(x, "hd"));
            XElement? group = new XElement("g", new XAttribute("transform", Matrix(Transform(transform, frame))), new XAttribute("opacity", F(Math.Clamp(TgsValues.Scalar(TgsJson.Get(transform, "o"), frame, 100) / 100, 0, 1))));
            JsonElement[] prefix = items.Take(index).Where(p => TgsJson.Text(p, "ty") != "tr").Concat(items.Skip(index + 1).Where(p => IsStyle(TgsJson.Text(p, "ty")))).ToArray();
            XElement original = ShapesArray(prefix, frame, depth + 1);
            int elements = original.DescendantsAndSelf().Count();
            JsonElement repeatTransform = TgsJson.Get(repeater, "tr");
            double startOpacity = TgsValues.Scalar(TgsJson.Get(repeatTransform, "so"), frame, 100) / 100;
            double endOpacity = TgsValues.Scalar(TgsJson.Get(repeatTransform, "eo"), frame, 100) / 100;
            double offset = TgsValues.Scalar(TgsJson.Get(repeater, "o"), frame);
            for (int step = 0; step < count; step++)
            {
                int i = TgsJson.Number(repeater, "m", 1) == 1 ? count - step - 1 : step;
                for (int element = 0; element < elements; element++)
                {
                    Count(depth);
                }

                double opacity = count <= 1 ? startOpacity : startOpacity + (((endOpacity - startOpacity) * i) / (count - 1));
                group.Add(new XElement("g", new XAttribute("transform", Matrix(RepeaterTransform(repeatTransform, i + offset, frame))), new XAttribute("opacity", F(Math.Clamp(opacity, 0, 1))), new XElement(original)));
            }

            JsonElement[] suffix = items.Skip(index + 1).Where(p => TgsJson.Text(p, "ty") != "tr").ToArray();
            if (suffix.Any(p => TgsJson.Text(p, "ty") is "gr" or "sh" or "el" or "rc" or "sr"))
            {
                group.Add(ShapesArray(suffix, frame, depth + 1));
            }

            return group;
        }

        private XElement ShapesArray(JsonElement[] items, double frame, int depth)
        {
            using JsonDocument array = JsonDocument.Parse("[" + string.Join(",", items.Select(item => item.GetRawText())) + "]");
            return Shapes(array.RootElement, frame, depth);
        }

        private static bool IsStyle(string type) => type is "fl" or "st" or "gf" or "gs";
        private static int RepeaterCount(JsonElement modifier, double frame)
        {
            double copies = TgsValues.Scalar(TgsJson.Get(modifier, "c"), frame);
            if (copies < 0 || copies > 4096)
            {
                throw new InvalidDataException("Lottie repeater exceeds 4096 copies.");
            }

            return (int)Math.Ceiling(copies);
        }

        private static Matrix3x2 RepeaterTransform(JsonElement transform, double power, double frame)
        {
            Vector2 position = TgsValues.Point(TgsJson.Get(transform, "p"), frame), anchor = TgsValues.Point(TgsJson.Get(transform, "a"), frame);
            Vector2 scale = TgsValues.Point(TgsJson.Get(transform, "s"), frame, 100, 100) / 100;
            double angle = (TgsValues.Scalar(TgsJson.Get(transform, "r"), frame) * Math.PI) / 180;
            float Power(float value)
            {
                if ((value == 0 && power < 0) || (value < 0 && power != Math.Floor(power)))
                {
                    throw new NotSupportedException("Lottie repeater with a fractional negative scale or inverse zero scale.");
                }

                double result = Math.Pow(value, power);
                if (!double.IsFinite(result) || Math.Abs(result) > float.MaxValue)
                {
                    throw new InvalidDataException("Lottie repeater transform overflow.");
                }

                return (float)result;
            }

            return Matrix3x2.CreateTranslation(-anchor) * Matrix3x2.CreateScale(Power(scale.X), Power(scale.Y)) * Matrix3x2.CreateRotation((float)(angle * power)) * Matrix3x2.CreateTranslation(anchor + (position * (float)power));
        }

        private void RepeatPaths(List<(int Index, VectorPath Path)> paths, JsonElement modifier, int index, double frame)
        {
            int count = RepeaterCount(modifier, frame);
            JsonElement transform = TgsJson.Get(modifier, "tr");
            double offset = TgsValues.Scalar(TgsJson.Get(modifier, "o"), frame);
            var affected = paths.Where(p => p.Index < index).ToArray();
            paths.RemoveAll(p => p.Index < index);
            for (int i = count - 1; i >= 0; i--)
            {
                Matrix3x2 matrix = RepeaterTransform(transform, i + offset, frame);
                foreach ((int Index, VectorPath Path) source in affected)
                {
                    Count(0);
                    VectorPath path = source.Path.Transform(matrix);
                    Geometry(path);
                    paths.Add((source.Index, path));
                }
            }
        }
    }
}
