using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal readonly record struct Packet(int Layer, int Resolution, int Component, int Precinct, long X, long Y);
}
