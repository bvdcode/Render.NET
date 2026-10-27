using System;
using System.IO;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal static class TgsJson
    {
        internal static JsonElement Get(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out JsonElement p) ? p : default;
        internal static string Text(JsonElement e, string key, string fallback = "") => Get(e, key).ValueKind == JsonValueKind.String ? Get(e, key).GetString()! : fallback;
        internal static double Number(JsonElement e, string key, double fallback = 0) => Number(Get(e, key), fallback);
        internal static double Number(JsonElement e, double fallback = 0)
        {
            if (e.ValueKind != JsonValueKind.Number)
            {
                return fallback;
            }

            double value = e.GetDouble();
            if (!double.IsFinite(value))
            {
                throw new InvalidDataException("Lottie numbers must be finite.");
            }

            return value;
        }

        internal static bool Flag(JsonElement e, string key) => Get(e, key).ValueKind == JsonValueKind.True || Number(e, key) != 0;
        internal static JsonElement[] Array(JsonElement e) => e.ValueKind == JsonValueKind.Array ? System.Linq.Enumerable.ToArray(e.EnumerateArray()) : System.Array.Empty<JsonElement>();
    }
}
