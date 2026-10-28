using System;
using System.Collections.Generic;
using System.IO;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal record PatternOrigin(IColor Color, TransformationMatrix Matrix, CurrentGraphicsState State) : IColor
    {
        public ColorSpace ColorSpace => ColorSpace.Pattern;

        public (double r, double g, double b) ToRGBValues() => Color.ToRGBValues();
    }
}
