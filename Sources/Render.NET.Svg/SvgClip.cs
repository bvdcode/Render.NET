using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;

namespace Render.NET.Svg
{
    internal record SvgClip(SvgClipShape[] Shapes, bool ObjectBox);
}
