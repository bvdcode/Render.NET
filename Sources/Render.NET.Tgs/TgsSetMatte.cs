using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace Render.NET.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private void SetMatte(JsonElement layer, XElement content, JsonElement[] layers,
            Dictionary<double, JsonElement> byIndex, double frame, int depth, HashSet<int> active)
        {
            foreach (JsonElement effect in ExpandEffects(TgsJson.Get(layer, "ef")).Where(effect => TgsEffectInfo.Type(effect) == 28))
            {
                JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
                double Scalar(int index, double fallback = 0) => index < values.Length
                    ? TgsValues.Scalar(TgsJson.Get(values[index], "v"), frame, fallback) : fallback;
                double source = Scalar(0);
                if (source == 0)
                {
                    continue;
                }

                if (Scalar(1, 4) != 4 || Scalar(4, 1) != 1)
                {
                    _warnings.Add("Lottie Set Matte channel or replacement alpha");
                    continue;
                }

                int index = Array.FindIndex(layers, candidate => TgsJson.Number(candidate, "ind", double.NaN) == source);
                if (index < 0)
                {
                    throw new InvalidDataException("Missing Lottie Set Matte source layer.");
                }

                XElement drawing = PaintLayer(layers, index, byIndex, frame, depth + 1, active, includeHidden: true);
                string mask = Matte(drawing, Scalar(2) == 0 ? 1 : 2);
                XElement group = new("g", new XAttribute("mask", "url(#" + mask + ")"), content.Nodes().ToArray());
                content.ReplaceNodes(group);
            }
        }
    }
}
