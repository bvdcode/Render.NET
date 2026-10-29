using System;
using System.Text.Json;
using System.Xml.Linq;

namespace Render.NET.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private void RoughenEdges(XElement filter, Func<int, double, double> scalar, JsonElement[] values, double frame)
        {
            double border = scalar(2, 0);
            if (border == 0)
            {
                return;
            }

            if (!SupportedRoughenMode(scalar, values, frame))
            {
                _warnings.Add("Roughen Edges requires Roughen mode, zero stretch/offset/evolution and no evolution cycle");
                return;
            }

            double complexity = scalar(8, 2), scale = scalar(5, 100), influence = scalar(4, 1), sharpness = scalar(3, 1);
            ValidateRoughenParameters(border, complexity, scale, influence, sharpness);

            string original = Id(), alpha = Id(), blurred = Id(), noise = Id(), matte = Id();
            double contrast = Math.Max(.25, sharpness) * 4;
            filter.Add(
                new XElement("feColorMatrix", new XAttribute("result", original)),
                new XElement("feColorMatrix", new XAttribute("in", original), new XAttribute("values", "0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 1 0"), new XAttribute("result", alpha)),
                new XElement("feGaussianBlur", new XAttribute("in", alpha), new XAttribute("stdDeviation", F(border / 2)), new XAttribute("result", blurred)),
                new XElement("feTurbulence", new XAttribute("type", "fractalNoise"), new XAttribute("baseFrequency", F(1 / scale)), new XAttribute("numOctaves", F(complexity)), new XAttribute("seed", F(scalar(13, 0))), new XAttribute("result", noise)),
                new XElement("feComposite", new XAttribute("in", blurred), new XAttribute("in2", noise), new XAttribute("operator", "arithmetic"), new XAttribute("k2", F(contrast)), new XAttribute("k3", F((-contrast * influence) / 2)), new XAttribute("k4", F(.5 - (contrast * .75) + ((contrast * influence) / 4))), new XAttribute("result", matte)),
                new XElement("feComposite", new XAttribute("in", original), new XAttribute("in2", matte), new XAttribute("operator", "in")));
        }

        private static bool SupportedRoughenMode(Func<int, double, double> scalar, JsonElement[] values, double frame)
        {
            double[] offset = values.Length > 7 ? TgsValues.Vector(TgsJson.Get(values[7], "v"), frame, 0, 0) : new[] { 0d, 0d };
            return scalar(0, 1) == 1 && scalar(6, 0) == 0 && scalar(9, 0) == 0 && scalar(11, 0) == 0 && offset.Length >= 2 && offset[0] == 0 && offset[1] == 0;
        }

        private static void ValidateRoughenParameters(double border, double complexity, double scale, double influence, double sharpness)
        {
            if (border < 0 || border > 512 || complexity < 1 || complexity > 12 || complexity != Math.Floor(complexity) || scale <= 0 || influence < 0 || influence > 2 || sharpness < 0 || sharpness > 10)
            {
                throw new System.IO.InvalidDataException("Roughen Edges parameters exceed supported limits.");
            }
        }
    }
}
