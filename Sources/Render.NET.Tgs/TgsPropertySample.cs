using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal static class TgsPropertySample
    {
        internal static JsonElement Read(JsonElement property, double frame)
        {
            TgsKeyPair pair = TgsValues.Keys(property, frame);
            JsonElement start = Unwrap(pair.Start), end = Unwrap(pair.End);
            if (start.ValueKind == JsonValueKind.Object)
            {
                return Path(start, end, TgsValues.Ease(pair.Key, pair.Progress));
            }

            double[] values = TgsValues.Vector(property, frame);
            if (values.Length == 0)
            {
                throw new InvalidDataException("Lottie property has no numeric value.");
            }

            if (start.ValueKind == JsonValueKind.Number || values.Length == 1)
            {
                return JsonSerializer.SerializeToElement(values[0]);
            }

            return JsonSerializer.SerializeToElement(values);
        }

        internal static JsonElement Path(JsonElement start, JsonElement end, double progress)
        {
            int count = TgsJson.Array(TgsJson.Get(start, "v")).Length;
            if (TgsJson.Array(TgsJson.Get(end, "v")).Length != count)
            {
                throw new NotSupportedException("Lottie morph paths with different vertex counts.");
            }
            using MemoryStream bytes = new();
            using (Utf8JsonWriter writer = new(bytes))
            {
                writer.WriteStartObject();
                foreach (string component in new[] { "v", "i", "o" })
                {
                    JsonElement[] a = TgsJson.Array(TgsJson.Get(start, component)), b = TgsJson.Array(TgsJson.Get(end, component));
                    if (a.Length != count || b.Length != count)
                    {
                        throw new InvalidDataException("Lottie path vertex and tangent counts must match.");
                    }

                    writer.WriteStartArray(component);
                    for (int i = 0; i < count; i++)
                    {
                        double[] x = TgsValues.Numbers(a[i]), y = TgsValues.Numbers(b[i]);
                        if (x.Length < 2 || y.Length < 2)
                        {
                            throw new InvalidDataException("Invalid Lottie path point.");
                        }

                        writer.WriteStartArray();
                        writer.WriteNumberValue(x[0] + ((y[0] - x[0]) * progress));
                        writer.WriteNumberValue(x[1] + ((y[1] - x[1]) * progress));
                        writer.WriteEndArray();
                    }

                    writer.WriteEndArray();
                }

                writer.WriteBoolean("c", TgsJson.Flag(start, "c"));
                writer.WriteEndObject();
            }

            using JsonDocument json = JsonDocument.Parse(bytes.ToArray());
            return json.RootElement.Clone();
        }

        internal static void Validate(JsonElement result, JsonElement original, int maximumItems)
        {
            int count = 0;
            Check(result, ref count, maximumItems);
            if (result.ValueKind != original.ValueKind || (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() != original.GetArrayLength()))
            {
                throw new InvalidDataException("Expression result does not match its property type.");
            }

            if (result.ValueKind == JsonValueKind.Object)
            {
                if (TgsJson.Get(result, "c").ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw new InvalidDataException("Expression path must specify its closed state.");
                }

                foreach (string name in new[] { "v", "i", "o" })
                {
                    if (TgsJson.Get(result, name).ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException("Expression path must contain vertices and tangent arrays.");
                    }
                }

                Path(result, result, 0);
            }
        }

        private static void Check(JsonElement value, ref int count, int maximumItems)
        {
            if (++count > maximumItems)
            {
                throw new InvalidDataException("Expression result exceeds MaximumItems.");
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    TgsJson.Number(value);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        Check(item, ref count, maximumItems);
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        Check(property.Value, ref count, maximumItems);
                    }

                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    break;
                default:
                    throw new InvalidDataException("Expression result must contain finite numbers or path data.");
            }
        }

        private static JsonElement Unwrap(JsonElement value) => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 && value[0].ValueKind == JsonValueKind.Object ? value[0] : value;
    }
}
