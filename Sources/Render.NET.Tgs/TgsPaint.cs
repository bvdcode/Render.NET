using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;

namespace Render.NET.Tgs
{
    internal sealed partial class TgsSceneBuilder
    {
        private void ApplyStyle(XElement path, JsonElement style, double frame)
        {
            string type = TgsJson.Text(style, "ty");
            bool stroke = type is "st" or "gs";
            string paint = type is "gf" or "gs" ? Gradient(style, frame) : Color(TgsValues.Vector(TgsJson.Get(style, "c"), frame, 0, 0, 0, 1));
            double opacity = Math.Clamp(TgsValues.Scalar(TgsJson.Get(style, "o"), frame, 100) / 100, 0, 1);
            path.SetAttributeValue("fill", stroke ? "none" : paint);
            if (stroke)
            {
                path.SetAttributeValue("stroke", paint);
                path.SetAttributeValue("stroke-opacity", F(opacity));
                path.SetAttributeValue("stroke-width", F(Math.Max(0, TgsValues.Scalar(TgsJson.Get(style, "w"), frame, 1))));
                path.SetAttributeValue("stroke-linecap", TgsJson.Number(style, "lc", 1) switch
                {
                    2 => "round",
                    3 => "square",
                    _ => "butt"
                });
                path.SetAttributeValue("stroke-linejoin", TgsJson.Number(style, "lj", 1) switch
                {
                    2 => "round",
                    3 => "bevel",
                    _ => "miter"
                });
                path.SetAttributeValue("stroke-miterlimit", F(Math.Max(1, TgsJson.Number(style, "ml", 4))));
                Dash(path, style, frame);
            }
            else
            {
                path.SetAttributeValue("fill-opacity", F(opacity));
                path.SetAttributeValue("fill-rule", TgsJson.Number(style, "r", 1) == 2 ? "evenodd" : "nonzero");
            }
        }

        private static void Dash(XElement path, JsonElement style, double frame)
        {
            var lengths = new List<string>();
            foreach (JsonElement item in TgsJson.Array(TgsJson.Get(style, "d")))
            {
                double value = TgsValues.Scalar(TgsJson.Get(item, "v"), frame);
                if (TgsJson.Text(item, "n") == "o")
                {
                    path.SetAttributeValue("stroke-dashoffset", F(value));
                }
                else
                {
                    lengths.Add(F(Math.Max(0, value)));
                }
            }

            if (lengths.Count > 0)
            {
                path.SetAttributeValue("stroke-dasharray", string.Join(" ", lengths));
            }
        }

        private string Gradient(JsonElement style, double frame)
        {
            bool radial = TgsJson.Number(style, "t", 1) == 2;
            Vector2 start = TgsValues.Point(TgsJson.Get(style, "s"), frame), end = TgsValues.Point(TgsJson.Get(style, "e"), frame);
            string id = Id();
            var gradient = new XElement(radial ? "radialGradient" : "linearGradient", new XAttribute("id", id), new XAttribute("gradientUnits", "userSpaceOnUse"));
            if (radial)
            {
                float radius = Vector2.Distance(start, end);
                double highlight = Math.Clamp(TgsValues.Scalar(TgsJson.Get(style, "h"), frame) / 100, -.999, .999);
                double angle = Math.Atan2(end.Y - start.Y, end.X - start.X) + ((TgsValues.Scalar(TgsJson.Get(style, "a"), frame) * Math.PI) / 180);
                gradient.Add(new XAttribute("cx", F(start.X)), new XAttribute("cy", F(start.Y)), new XAttribute("r", F(radius)), new XAttribute("fx", F(start.X + (Math.Cos(angle) * radius * highlight))), new XAttribute("fy", F(start.Y + (Math.Sin(angle) * radius * highlight))));
            }
            else
            {
                gradient.Add(new XAttribute("x1", F(start.X)), new XAttribute("y1", F(start.Y)), new XAttribute("x2", F(end.X)), new XAttribute("y2", F(end.Y)));
            }

            GradientStops(gradient, style, frame);
            _defs.Add(gradient);
            return "url(#" + id + ")";
        }

        private static void GradientStops(XElement gradient, JsonElement style, double frame)
        {
            JsonElement g = TgsJson.Get(style, "g");
            int count = (int)TgsJson.Number(g, "p");
            double[] values = TgsValues.Vector(TgsJson.Get(g, "k"), frame);
            if (count < 0 || count > values.Length / 4 || (values.Length - (count * 4)) % 2 != 0)
            {
                throw new InvalidDataException("Invalid Lottie gradient stop array.");
            }

            var positions = new SortedSet<double>();
            var duplicates = new HashSet<double>();
            for (int i = 0; i < count; i++)
            {
                if (!positions.Add(Math.Clamp(values[i * 4], 0, 1)))
                {
                    duplicates.Add(Math.Clamp(values[i * 4], 0, 1));
                }
            }

            var alphaPositions = new HashSet<double>();
            for (int i = count * 4; i < values.Length; i += 2)
            {
                positions.Add(Math.Clamp(values[i], 0, 1));
                if (!alphaPositions.Add(Math.Clamp(values[i], 0, 1)))
                {
                    duplicates.Add(Math.Clamp(values[i], 0, 1));
                }
            }

            void Stop(double at, double sample)
            {
                string color = Color(new[] { Interpolate(values, count, sample, 0, 4, 1, 0), Interpolate(values, count, sample, 0, 4, 2, 0), Interpolate(values, count, sample, 0, 4, 3, 0) });
                double alpha = Interpolate(values, count, sample, count * 4, 2, 1, 1);
                gradient.Add(new XElement("stop", new XAttribute("offset", F(at)), new XAttribute("stop-color", color), new XAttribute("stop-opacity", F(Math.Clamp(alpha, 0, 1)))));
            }

            foreach (double at in positions)
            {
                if (duplicates.Contains(at))
                {
                    Stop(at, at - 1e-10);
                    Stop(at, at + 1e-10);
                }
                else
                {
                    Stop(at, at);
                }
            }
        }

        private static double Interpolate(double[] values, int count, double at, int start, int stride, int channel, double fallback)
        {
            int n = start == 0 ? count : (values.Length - start) / stride;
            if (n == 0)
            {
                return fallback;
            }

            int next = 0;
            while (next < n && values[start + (next * stride)] <= at)
            {
                next++;
            }

            if (next == 0)
            {
                return values[start + channel];
            }

            if (next == n)
            {
                return values[start + ((n - 1) * stride) + channel];
            }

            int a = start + ((next - 1) * stride), b = start + (next * stride);
            double weight = (at - values[a]) / (values[b] - values[a]);
            return values[a + channel] + ((values[b + channel] - values[a + channel]) * weight);
        }

        private static string Color(double[] values)
        {
            if (values.Length < 3)
            {
                throw new InvalidDataException("Lottie color needs at least three channels.");
            }

            int Channel(int i) => (int)Math.Round(Math.Clamp(values[i], 0, 1) * 255);
            return $"rgb({Channel(0)},{Channel(1)},{Channel(2)})";
        }
    }
}
