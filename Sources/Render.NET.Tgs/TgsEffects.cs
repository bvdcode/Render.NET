using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace Render.NET.Tgs
{
    internal sealed partial class TgsSceneBuilder
    {
        private void Effects(JsonElement layer, XElement content, double frame)
        {
            XElement? maskedSource = content.Elements().FirstOrDefault();
            XElement filter = FullFilter(Id());
            foreach (JsonElement effect in ExpandEffects(TgsJson.Get(layer, "ef")))
            {
                int type = (int)TgsJson.Number(effect, "ty", -1);
                if (type is 22 or 35)
                {
                    FlushEffect(content, filter);
                    filter = FullFilter(Id());
                    if (type == 22)
                    {
                        StrokeEffect(layer, content, maskedSource, effect, frame);
                    }
                    else
                    {
                        TransformEffect(content, effect, frame);
                    }
                }
                else
                {
                    Effect(effect, filter, frame);
                }
            }

            FlushEffect(content, filter);
        }

        private void Effect(JsonElement effect, XElement filter, double frame)
        {
            if (TgsJson.Number(effect, "en", 1) == 0)
            {
                return;
            }

            int type = (int)TgsJson.Number(effect, "ty", -1);
            JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
            double Scalar(int index, double fallback = 0) => index < values.Length ? TgsValues.Scalar(TgsJson.Get(values[index], "v"), frame, fallback) : fallback;
            double[] Color(int index, double fallback = 0) => index < values.Length ? TgsValues.Vector(TgsJson.Get(values[index], "v"), frame, new[] { fallback, fallback, fallback }) : new[]
            {
                fallback,
                fallback,
                fallback
            };
            if (type == 24)
            {
                LevelsEffect(filter, Scalar);
                return;
            }

            if (type is 20 or 21 or 23)
            {
                ColorEffect(filter, type, Color, Scalar);
                return;
            }

            if (type == 25)
            {
                double[] color = Color(0);
                double angle = ((Scalar(2) - 90) * Math.PI) / 180, distance = Scalar(3);
                filter.Add(new XElement("feDropShadow", new XAttribute("dx", F(Math.Cos(angle) * distance)), new XAttribute("dy", F(Math.Sin(angle) * distance)), new XAttribute("stdDeviation", F(Math.Max(0, Scalar(4)) / 4)), new XAttribute("flood-color", TgsSceneBuilder.Color(color)), new XAttribute("flood-opacity", F(Math.Clamp(Scalar(1, 255) / 255, 0, 1)))));
                return;
            }

            if (type == 29)
            {
                double blur = Math.Max(0, Scalar(0)) * .3;
                int dimensions = (int)Scalar(1, 1);
                filter.Add(new XElement("feGaussianBlur", new XAttribute("stdDeviation", F(dimensions == 3 ? 0 : blur) + " " + F(dimensions == 2 ? 0 : blur)), new XAttribute("edgeMode", Scalar(2) == 1 ? "duplicate" : "none")));
                return;
            }

            _warnings.Add("Lottie effect type " + type);
        }

        private static void ColorEffect(XElement filter, int type, Func<int, double, double[]> color, Func<int, double, double> scalar)
        {
            if (type == 21)
            {
                double[] ink = color(2, 0);
                double opacity = Math.Clamp(scalar(6, 1), 0, 1);
                filter.Add(new XElement("feColorMatrix", new XAttribute("values", "0 0 0 0 " + F(ink[0]) + " 0 0 0 0 " + F(ink[1]) + " 0 0 0 0 " + F(ink[2]) + " 0 0 0 " + F(opacity) + " 0")));
                return;
            }

            double[] low = color(type == 20 ? 0 : 2, 0), high = color(type == 20 ? 1 : 0, 1);
            double[] middle = type == 23 ? color(1, .5) : new[]
            {
                (low[0] + high[0]) / 2,
                (low[1] + high[1]) / 2,
                (low[2] + high[2]) / 2
            };
            string original = "original" + filter.Elements().Count();
            filter.Add(new XElement("feColorMatrix", new XAttribute("result", original)));
            var gray = new float[20];
            foreach (int row in new[]
            {
                0,
                5,
                10
            }

            )
            {
                for (int column = 0; column < 3; column++)
                {
                    gray[row + column] = 1f / 3;
                }
            }

            gray[18] = 1;
            filter.Add(new XElement("feColorMatrix", new XAttribute("color-interpolation-filters", "linearRGB"), new XAttribute("values", string.Join(" ", gray.Select(v => F(v))))));
            filter.Add(new XElement("feComponentTransfer", Enumerable.Range(0, 3).Select(i => new XElement("feFunc" + "RGB"[i], new XAttribute("type", "table"), new XAttribute("tableValues", F(low[i]) + " " + F(middle[i]) + " " + F(high[i]))))));
            if (type == 20)
            {
                double amount = Math.Clamp(scalar(2, 100) / 100, 0, 1);
                filter.Add(new XElement("feComposite", new XAttribute("in2", original), new XAttribute("operator", "arithmetic"), new XAttribute("k2", F(amount)), new XAttribute("k3", F(1 - amount))));
            }
        }
    }
}
