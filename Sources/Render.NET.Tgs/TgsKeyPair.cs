using System;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal readonly record struct TgsKeyPair(JsonElement Start, JsonElement End, JsonElement Key, double Progress);
}
