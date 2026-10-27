using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Render.NET.Tgs
{
    internal sealed class TgsFeatureCheck
    {
        private static readonly HashSet<string> ShapeTypes = new("gr tr sh rc el sr fl st gf gs tm".Split(' '), StringComparer.Ordinal);
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

        private void Visit(JsonElement e)
        {
            _token.ThrowIfCancellationRequested();
            if (++_items > _limits.MaximumItems)
            {
                throw new InvalidDataException("Lottie exceeds MaximumItems.");
            }

            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    Features(e);
                    foreach (JsonProperty property in e.EnumerateObject())
                    {
                        Visit(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    ValidateKeys(e);
                    foreach (JsonElement child in e.EnumerateArray())
                    {
                        Visit(child);
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

        private void Features(JsonElement e)
        {
            JsonElement type = TgsJson.Get(e, "ty");
            if (type.ValueKind == JsonValueKind.String)
            {
                ShapeFeatures(e, type.GetString()!);
            }
            else if (type.ValueKind == JsonValueKind.Number && TgsJson.Number(type) is not 0 and not 1 and not 3 and not 4)
            {
                _warnings.Add("Lottie layer type " + type.GetRawText());
            }

            if (TgsJson.Flag(e, "ddd"))
            {
                _warnings.Add("Lottie 3D layers");
            }

            if (TgsJson.Number(e, "bm") != 0)
            {
                _warnings.Add("Lottie blend modes");
            }

            if (TgsJson.Number(e, "tt") != 0)
            {
                _warnings.Add("Lottie track mattes");
            }

            if (TgsJson.Flag(e, "ao"))
            {
                _warnings.Add("Lottie automatic orientation");
            }

            PropertyFeatures(e);
        }

        private void PropertyFeatures(JsonElement e)
        {
            if (TgsJson.Get(e, "ef").ValueKind == JsonValueKind.Array && TgsJson.Get(e, "ef").GetArrayLength() > 0)
            {
                _warnings.Add("Lottie effects");
            }

            if (TgsJson.Get(e, "x").ValueKind == JsonValueKind.String)
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

        private void ShapeFeatures(JsonElement e, string type)
        {
            if (!ShapeTypes.Contains(type))
            {
                _warnings.Add("Lottie shape/modifier: " + type);
            }

            if (type == "sr" && (TgsValues.Scalar(TgsJson.Get(e, "os"), 0) != 0 || TgsValues.Scalar(TgsJson.Get(e, "is"), 0) != 0))
            {
                _warnings.Add("Lottie rounded polystars");
            }
        }

        private void MaskFeatures(JsonElement masks)
        {
            if (masks.GetArrayLength() > 1)
            {
                _warnings.Add("Multiple Lottie masks");
            }

            foreach (JsonElement mask in masks.EnumerateArray())
            {
                if (TgsJson.Text(mask, "mode", "a") is not "a" and not "n" || TgsJson.Flag(mask, "inv"))
                {
                    _warnings.Add("Lottie mask boolean operations");
                }

                JsonElement opacity = TgsJson.Get(mask, "o");
                if (TgsJson.Flag(opacity, "a") || TgsValues.Scalar(opacity, 0, 100) != 100)
                {
                    _warnings.Add("Lottie mask opacity");
                }

                if (TgsValues.Scalar(TgsJson.Get(mask, "x"), 0) != 0)
                {
                    _warnings.Add("Lottie mask expansion");
                }
            }
        }
    }
}
