using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    internal record PathContour(List<Vector2> Points, bool Closed);
}
