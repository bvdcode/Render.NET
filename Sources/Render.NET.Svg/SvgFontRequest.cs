using System;
using System.Collections.Generic;

namespace Render.NET.Svg
{
    /// <summary>A requested CSS font face.</summary>
    public record SvgFontRequest(string Family, int Weight = 400, bool Italic = false);
}
