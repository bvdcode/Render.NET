using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal partial class TgsExpressionScope
    {
        private void Register(JsonElement value, int layer)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (TgsJson.Get(value, "k").ValueKind != JsonValueKind.Undefined || (TgsJson.Flag(value, "s") && TgsJson.Get(value, "x").ValueKind == JsonValueKind.Object))
                {
                    _ids.Add(value, _properties.Count);
                    _properties.Add((value, layer));
                }

                foreach (JsonProperty property in value.EnumerateObject())
                {
                    Register(property.Value, layer);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in value.EnumerateArray())
                {
                    Register(child, layer);
                }
            }
        }

        private string Metadata(double width, double height)
        {
            object[] layers = _layers.Select((layer, index) => (object)new
            {
                name = TgsJson.Text(layer, "nm"),
                index,
                parent = FindLayer(TgsJson.Number(layer, "parent", double.NaN)),
                transform = TgsJson.Get(layer, "ks").ValueKind == JsonValueKind.Object
                    ? TgsJson.Get(layer, "ks").EnumerateObject().ToDictionary(p => p.Name, p => Id(p.Value))
                    : new Dictionary<string, int>(),
                effects = TgsJson.Array(TgsJson.Get(layer, "ef")).Select(effect => new
                {
                    name = TgsJson.Text(effect, "nm"),
                    matchName = TgsJson.Text(effect, "mn"),
                    properties = TgsJson.Array(TgsJson.Get(effect, "ef")).Select(p => new { name = TgsJson.Text(p, "nm"), matchName = TgsJson.Text(p, "mn"), id = Id(TgsJson.Get(p, "v")) }).ToArray()
                }).ToArray()
            }).ToArray();
            return JsonSerializer.Serialize(new { layers, rate = _document.FrameRate, width, height });
        }

        private int Id(JsonElement property) => _ids.TryGetValue(property, out int id) ? id : -1;
        private int FindLayer(double index) => Array.FindIndex(_layers, layer => TgsJson.Number(layer, "ind", double.NaN) == index);
        private static double[] KeyTimes(JsonElement property) => TgsJson.Array(TgsJson.Get(property, "k")).Where(k => TgsJson.Get(k, "t").ValueKind == JsonValueKind.Number).Select(k => TgsJson.Number(k, "t")).ToArray();

        private void RequireLayer(int index, HashSet<int> needed)
        {
            if (index < 0 || index >= _layers.Length || !needed.Add(index))
            {
                return;
            }

            JsonElement layer = _layers[index];
            RequireLayer(FindLayer(TgsJson.Number(layer, "parent", double.NaN)), needed);
            if (TgsJson.Number(layer, "tt") is >= 1 and <= 4)
            {
                JsonElement explicitMatte = TgsJson.Get(layer, "tp");
                RequireLayer(explicitMatte.ValueKind == JsonValueKind.Number ? FindLayer(TgsJson.Number(explicitMatte)) : index - 1, needed);
            }

            foreach (JsonElement effect in TgsJson.Array(TgsJson.Get(layer, "ef")))
            {
                if (TgsEffectInfo.Type(effect) == 28 && TgsJson.Number(effect, "en", 1) != 0)
                {
                    JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
                    if (values.Length > 0)
                    {
                        RequireLayer(FindLayer(TgsValues.Scalar(TgsJson.Get(values[0], "v"), _frame)), needed);
                    }
                }
            }
        }

        private Matrix3x2 LayerMatrix(int index, double frame, HashSet<int> active)
        {
            if (!active.Add(index) || active.Count > _document.Limits.MaximumDepth)
            {
                throw new InvalidDataException("Cyclic/deep expression layer transform.");
            }

            JsonElement layer = _layers[index];
            JsonElement transform = TgsJson.Get(layer, "ks");
            Matrix3x2 matrix = transform.ValueKind == JsonValueKind.Object ? TgsTransforms.Transform(Rewrite(transform, frame), frame) : Matrix3x2.Identity;
            JsonElement parent = TgsJson.Get(layer, "parent");
            if (parent.ValueKind == JsonValueKind.Number)
            {
                int ancestor = FindLayer(TgsJson.Number(parent));
                if (ancestor < 0)
                {
                    throw new InvalidDataException("Missing expression parent layer.");
                }

                matrix *= LayerMatrix(ancestor, frame, active);
            }

            return matrix;
        }
    }
}
