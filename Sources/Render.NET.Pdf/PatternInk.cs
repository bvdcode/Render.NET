using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal record PatternInk(TilingPatternColor Pattern, IColor Ink) : IColor
    {
        public ColorSpace ColorSpace => ColorSpace.Pattern;

        public (double r, double g, double b) ToRGBValues() => Ink.ToRGBValues();
    }
}
