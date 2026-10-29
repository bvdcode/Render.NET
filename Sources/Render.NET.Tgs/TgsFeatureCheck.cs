using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Render.NET.Tgs
{
    internal class TgsFeatureCheck
    {
        private static readonly HashSet<string> ShapeTypes = new("gr tr sh rc el sr fl st gf gs tm rp mm rd op pb zz no".Split(' '), StringComparer.Ordinal);
        private readonly TgsReaderOptions _limits;
        private readonly CancellationToken _token;
        private readonly HashSet<string> _warnings = new();
        private int _items;
        internal TgsFeatureCheck(TgsReaderOptions limits, CancellationToken token)
        {
            _limits = limits;
            _token = token;
        }

        internal string[] Check(JsonElement root)
        {
            Visit(root);
            return _warnings.Order().ToArray();
        }

        private void Visit(JsonElement e, bool layer = false, bool visible = true)
        {
            _token.ThrowIfCancellationRequested();
            if (++_items > _limits.MaximumItems)
            {
                throw new InvalidDataException("Lottie exceeds MaximumItems.");
            }

            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    visible = visible && !(layer && TgsJson.Flag(e, "hd")) && !TgsEffectInfo.IsParameterGroup(e);
                    if (visible)
                    {
                        Features(e, layer);
                    }

                    foreach (JsonProperty property in e.EnumerateObject())
                    {
                        Visit(property.Value, property.Name == "layers", visible);
                    }

                    break;
                case JsonValueKind.Array:
                    ValidateKeys(e);
                    foreach (JsonElement child in e.EnumerateArray())
                    {
                        Visit(child, layer, visible);
                    }

                    break;
                case JsonValueKind.Number:
                    TgsJson.Number(e);
                    break;
            }
        }

        private static void ValidateKeys(JsonElement array)
        {
            if (array.GetArrayLength() == 0 || TgsJson.Get(array[0], "ty").ValueKind != JsonValueKind.Undefined || TgsJson.Get(array[0], "t").ValueKind != JsonValueKind.Number)
            {
                return;
            }

            double previous = double.NegativeInfinity;
            foreach (JsonElement key in array.EnumerateArray())
            {
                double next = TgsJson.Number(key, "t", double.NaN);
                if (!double.IsFinite(next) || next < previous)
                {
                    throw new InvalidDataException("Lottie keyframes must have ascending times.");
                }

                previous = next;
            }
        }

        private void Features(JsonElement e, bool layer)
        {
            JsonElement type = TgsJson.Get(e, "ty");
            if (type.ValueKind == JsonValueKind.String)
            {
                ShapeFeatures(e, type.GetString()!);
            }
            else if (layer)
            {
                LayerType(type);
            }

            if (TgsJson.Flag(e, "ddd"))
            {
                _warnings.Add("Lottie 3D layers");
            }

            if (TgsJson.Number(e, "bm") is < 0 or > 17)
            {
                _warnings.Add("Lottie blend modes");
            }

            if (TgsJson.Number(e, "tt") is < 0 or > 4)
            {
                _warnings.Add("Lottie track mattes");
            }

            PropertyFeatures(e, layer);
        }

        private void LayerType(JsonElement type)
        {
            if (type.ValueKind == JsonValueKind.Number && TgsJson.Number(type) is not 0 and not 1 and not 3 and not 4)
            {
                _warnings.Add("Lottie layer type " + type.GetRawText());
            }
        }

        private void PropertyFeatures(JsonElement e, bool layer)
        {
            if (layer)
            {
                CheckEffects(TgsJson.Get(e, "ef"));
            }

            if (_limits.Expressions is null && TgsJson.Get(e, "x").ValueKind == JsonValueKind.String)
            {
                _warnings.Add("Lottie expressions");
            }

            if (TgsJson.Get(e, "sid").ValueKind == JsonValueKind.String)
            {
                _warnings.Add("Lottie property slots");
            }

            if (TgsJson.Get(e, "masksProperties").ValueKind == JsonValueKind.Array)
            {
                MaskFeatures(TgsJson.Get(e, "masksProperties"));
            }
        }

        private void CheckEffects(JsonElement array)
        {
            foreach (JsonElement effect in TgsJson.Array(array))
            {
                if (TgsJson.Number(effect, "en", 1) == 0)
                {
                    continue;
                }

                if (TgsEffectInfo.IsParameterGroup(effect) || TgsEffectInfo.IsIdentity(effect))
                {
                    continue;
                }

                int type = TgsEffectInfo.Type(effect);
                if (TgsEffectInfo.IsContainer(effect))
                {
                    CheckEffects(TgsJson.Get(effect, "ef"));
                }
                else if (!new[]
                {
                    20,
                    21,
                    22,
                    23,
                    24,
                    25,
                    28,
                    29,
                    35
                }.Contains(type))
                {
                    _warnings.Add("Lottie effect type " + type);
                }
            }
        }

        private void ShapeFeatures(JsonElement e, string type)
        {
            if (!ShapeTypes.Contains(type))
            {
                _warnings.Add("Lottie shape/modifier: " + type);
            }
        }

        private void MaskFeatures(JsonElement masks)
        {
            foreach (JsonElement mask in masks.EnumerateArray())
            {
                if (!new[]
                {
                    "a",
                    "s",
                    "i",
                    "n",
                    "f",
                    "l",
                    "d"
                }.Contains(TgsJson.Text(mask, "mode", "a")))
                {
                    _warnings.Add("Lottie mask boolean operations");
                }
            }
        }
    }
}
