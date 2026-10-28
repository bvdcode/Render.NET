using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class Precinct
    {
        internal long X { get; init; }
        internal long Y { get; init; }
        internal List<PrecinctBand> Bands { get; } = new();
    }
}
