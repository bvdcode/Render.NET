using System;
using System.Collections.Generic;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.PdfFonts;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfStreamProcessor
    {
        private VectorPath? _textClip;
        private readonly Dictionary<(IFont Font, int Code), VectorPath?> _glyphs = new Dictionary<(IFont, int), VectorPath?>();
        private readonly Dictionary<string, PdfFallbackFont> _fallbackFonts = new Dictionary<string, PdfFallbackFont>();
        internal List<string> SubstitutedFonts { get; } = new List<string>();

        public override void RenderGlyph(IFont font, CurrentGraphicsState currentState, double fontSize, double pointSize,
            int code, string unicode, long currentOffset, in TransformationMatrix renderingMatrix,
            in TransformationMatrix textMatrix, in TransformationMatrix transformationMatrix, CharacterBoundingBox characterBoundingBox)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            int mode = (int)currentState.FontState.TextRenderingMode;
            if (mode == 3)
            {
                return;
            }

            if (font is IType3Font)
            {
                Warn("Type 3 glyph programs");
                return;
            }

            VectorPath? glyph = Glyph(font, code, unicode);
            if (glyph == null)
            {
                if (!string.IsNullOrWhiteSpace(unicode))
                {
                    Warn("Missing glyph outline in font " + font.Name?.Data);
                }

                return;
            }

            TransformationMatrix transform = renderingMatrix.Multiply(textMatrix).Multiply(transformationMatrix);
            VectorPath path = glyph.Transform(Matrix(transform));
            AddPainting(path, mode % 4 == 0 || mode % 4 == 2, mode % 4 == 1 || mode % 4 == 2, FillRule.NonZero);
            if (mode >= 4)
            {
                _textClip ??= new VectorPath();
                _textClip.Append(path);
            }
        }

        public override void EndText()
        {
            if (_textClip != null)
            {
                StorePath(_textClip);
                Commands.Add(new ClipCommand(_textClip, FillRule.NonZero));
                _textClip = null;
            }

            base.EndText();
        }

        private VectorPath? Glyph(IFont font, int code, string unicode)
        {
            if (_glyphs.TryGetValue((font, code), out VectorPath? cached))
            {
                return cached;
            }

            VectorPath? result = null;
            if (font.TryGetNormalisedPath(code, out IReadOnlyList<PdfSubpath>? paths))
            {
                result = new VectorPath();
                foreach (PdfSubpath path in paths)
                {
                    AppendGlyph(result, path);
                }
            }
            else if (font is not IType3Font)
            {
                result = FallbackGlyph(font, code, unicode);
            }

            _glyphs.Add((font, code), result);
            return result;
        }

        private VectorPath? FallbackGlyph(IFont font, int code, string unicode)
        {
            string name = font.Name?.Data ?? "Unknown";
            if (!_fallbackFonts.TryGetValue(name, out PdfFallbackFont? fallback))
            {
                fallback = new PdfFallbackFont(name, _limits.FontResolver);
                _fallbackFonts.Add(name, fallback);
                SubstitutedFonts.Add(name);
            }

            if (!fallback.TryGetPath(unicode, font.GetBoundingBox(code).Width, out IReadOnlyList<PdfSubpath>? paths))
            {
                return null;
            }

            var result = new VectorPath();
            foreach (PdfSubpath path in paths)
            {
                AppendGlyph(result, path);
            }

            return result;
        }

        private static void AppendGlyph(VectorPath output, PdfSubpath path)
        {
            foreach (PdfSubpath.IPathCommand command in path.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        output.MoveTo((float)move.Location.X, (float)move.Location.Y);
                        break;
                    case PdfSubpath.Line line:
                        output.LineTo((float)line.To.X, (float)line.To.Y);
                        break;
                    case PdfSubpath.CubicBezierCurve curve:
                        output.CubicTo((float)curve.FirstControlPoint.X, (float)curve.FirstControlPoint.Y,
                            (float)curve.SecondControlPoint.X, (float)curve.SecondControlPoint.Y, (float)curve.EndPoint.X, (float)curve.EndPoint.Y);
                        break;
                    case PdfSubpath.QuadraticBezierCurve quadratic:
                        output.QuadraticTo((float)quadratic.ControlPoint.X, (float)quadratic.ControlPoint.Y,
                            (float)quadratic.EndPoint.X, (float)quadratic.EndPoint.Y);
                        break;
                    case PdfSubpath.Close:
                        output.Close();
                        break;
                }
            }
        }
    }
}
