using System;
using System.Linq;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal static class TgsEffectInfo
    {
        internal static bool IsRoughenEdges(JsonElement effect) => TgsJson.Text(effect, "mn") == "ADBE Roughen Edges";
        internal static int Type(JsonElement effect) => TgsJson.Text(effect, "mn") switch
        {
            "ADBE Gaussian Blur" => 29,
            _ => (int)TgsJson.Number(effect, "ty", -1)
        };

        internal static bool IsParameterGroup(JsonElement effect)
        {
            string name = TgsJson.Text(effect, "mn");
            return Type(effect) == 5 && (name.StartsWith("Pseudo/", StringComparison.Ordinal) || name is
                "ADBE Layer Control" or "ADBE Slider Control" or "ADBE Angle Control" or "ADBE Color Control"
                or "ADBE Point Control" or "ADBE Checkbox Control" or "ADBE Point3D Control");
        }

        internal static bool IsIdentity(JsonElement effect)
        {
            if (TgsJson.Text(effect, "mn") == "ADBE HUE SATURATION")
            {
                JsonElement[] values = TgsJson.Array(TgsJson.Get(effect, "ef"));
                return values.Length >= 6 && values.Skip(2).Take(4).All(value =>
                {
                    JsonElement property = TgsJson.Get(value, "v");
                    JsonElement number = TgsJson.Get(property, "k");
                    return !TgsJson.Flag(property, "a") && number.ValueKind == JsonValueKind.Number && TgsJson.Number(number) == 0;
                });
            }

            if (Type(effect) == 34)
            {
                JsonElement[] meshes = TgsJson.Array(TgsJson.Get(effect, "ef"))
                    .SelectMany(group => TgsJson.Array(TgsJson.Get(group, "ef")))
                    .Where(group => TgsJson.Text(group, "mn") == "ADBE FreePin3 Mesh Group").ToArray();
                return meshes.Length > 0 && meshes.All(mesh => TgsJson.Array(TgsJson.Get(mesh, "ef")).Length == 0);
            }

            return false;
        }

        internal static bool IsContainer(JsonElement effect) => Type(effect) == 5 && TgsJson.Text(effect, "mn").Length == 0;
    }
}
