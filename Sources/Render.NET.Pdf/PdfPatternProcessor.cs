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
    internal sealed partial class PdfStreamProcessor
    {
        private readonly HashSet<StreamToken> _activePatterns = new(ReferenceEqualityComparer.Instance);
        private static readonly HashSet<string> ColorOperators = new(StringComparer.Ordinal)
        {
            "CS", "cs", "SC", "sc", "SCN", "scn", "G", "g", "RG", "rg", "K", "k", "sh", "ri"
        };
        private bool _uncoloredPattern;

        private void AddPatternPaint(VectorPath path, FillRule rule, CurrentGraphicsState state, bool stroke)
        {
            IColor? color = stroke ? state.CurrentStrokingColor : state.CurrentNonStrokingColor;
            double opacity = stroke ? state.AlphaConstantStroking : state.AlphaConstantNonStroking;
            IPdfDrawCommand? paint = CreatePatternPaint(color, opacity, Blend(state.BlendMode));
            if (paint != null)
            {
                AddPaint(new PatternPaintCommand(path, rule, paint, stroke ? Stroke(state) : null,
                    Matrix(state.CurrentTransformationMatrix)), state);
            }
        }

        private IPdfDrawCommand? CreatePatternPaint(IColor? color, double opacity, ColorBlendMode blend)
        {
            if (color is PatternOrigin origin)
            {
                TransformationMatrix savedMatrix = _patternMatrix;
                CurrentGraphicsState savedState = _patternState;
                try
                {
                    _patternMatrix = origin.Matrix;
                    _patternState = origin.State;
                    return CreatePatternPaint(origin.Color, opacity, blend);
                }
                finally
                {
                    _patternMatrix = savedMatrix;
                    _patternState = savedState;
                }
            }

            IColor? ink = color is PatternInk stencil ? stencil.Ink : null;
            if (color is PatternInk selected)
            {
                color = selected.Pattern;
            }

            IPdfDrawCommand? paint = color switch
            {
                TilingPatternColor tile => CreateTilingPattern(tile, ink, opacity, blend),
                ShadingPatternColor shading => CreatePatternShading(shading, opacity, blend),
                _ => null
            };
            if (color is not TilingPatternColor && color is not ShadingPatternColor)
            {
                Warn("Unspecified pattern color");
            }

            return paint;
        }

        private ShadingCommand? CreatePatternShading(ShadingPatternColor pattern, double opacity, ColorBlendMode blend)
        {
            if (pattern.ExtGState?.Data.Count > 0)
            {
                Warn("Shading pattern graphics-state overrides");
            }

            return CreateShading(pattern.Shading, Matrix(pattern.Matrix.Multiply(_patternMatrix)), opacity, blend);
        }

        private TilingPatternCommand CreateTilingPattern(TilingPatternColor pattern, IColor? ink, double opacity, ColorBlendMode blend)
        {
            ValidatePattern(pattern);
            if (++_captureDepth > 8 || !_activePatterns.Add(pattern.PatternStream))
            {
                throw new InvalidDataException("Recursive tiling pattern or pattern/mask nesting exceeds eight.");
            }

            var savedStack = GraphicsStack;
            bool savedUncolored = _uncoloredPattern;
            bool savedType3 = _uncoloredType3;
            int start = Commands.Count;
            Matrix3x2 placement = Matrix(pattern.Matrix.Multiply(_patternMatrix));
            try
            {
                var state = _patternState.DeepClone();
                state.CurrentTransformationMatrix = TransformationMatrix.Identity;
                state.AlphaConstantNonStroking = 1;
                state.AlphaConstantStroking = 1;
                state.BlendMode = UglyToad.PdfPig.Graphics.Core.BlendMode.Normal;
                state.SoftMask = null;
                if ((int)pattern.PaintType == 2)
                {
                    state.CurrentNonStrokingColor = ink ?? throw new InvalidDataException("Uncolored pattern requires an underlying color.");
                    state.CurrentStrokingColor = ink;
                }

                GraphicsStack = new Stack<CurrentGraphicsState>();
                GraphicsStack.Push(state);
                _uncoloredPattern = (int)pattern.PaintType == 2;
                _uncoloredType3 = false;
                // Data is already decoded by PdfPig. A form supplies the same resource and BBox scope,
                // while placement is applied later, independently of the paint-time CTM.
                var dictionary = new Dictionary<NameToken, IToken>
                {
                    [NameToken.Bbox] = pattern.PatternDictionary.Data[NameToken.Bbox],
                    [NameToken.Resources] = pattern.Resources
                };
                ProcessFormXObject(new StreamToken(new DictionaryToken(dictionary), pattern.Data.ToArray()), NameToken.Create("RenderPattern"));
                var commands = Commands.GetRange(start, Commands.Count - start).ToArray();
                return new TilingPatternCommand(commands, pattern.BBox, Math.Abs(pattern.XStep), Math.Abs(pattern.YStep),
                    placement, (float)opacity, blend, (int)pattern.TilingType);
            }
            finally
            {
                Commands.RemoveRange(start, Commands.Count - start);
                GraphicsStack = savedStack;
                _uncoloredPattern = savedUncolored;
                _uncoloredType3 = savedType3;
                _activePatterns.Remove(pattern.PatternStream);
                _captureDepth--;
            }
        }

        private static void ValidatePattern(TilingPatternColor pattern)
        {
            PdfRectangle box = pattern.BBox;
            if (!double.IsFinite(pattern.XStep) || !double.IsFinite(pattern.YStep) || pattern.XStep == 0 || pattern.YStep == 0
                || !double.IsFinite(box.Left + box.Bottom + box.Width + box.Height) || box.Width <= 0 || box.Height <= 0
                || (int)pattern.PaintType < 1 || (int)pattern.PaintType > 2 || (int)pattern.TilingType < 1 || (int)pattern.TilingType > 3)
            {
                throw new InvalidDataException("Invalid tiling pattern bounds, spacing, paint type or tiling type.");
            }

            Matrix3x2 matrix = Matrix(pattern.Matrix);
            if (!Matrix3x2.Invert(matrix, out Matrix3x2 inverse) || !float.IsFinite(inverse.M11 + inverse.M12 + inverse.M21 + inverse.M22 + inverse.M31 + inverse.M32))
            {
                throw new InvalidDataException("Invalid tiling pattern matrix.");
            }
        }

        private void PreservePatternInk(IGraphicsStateOperation operation)
        {
            CurrentGraphicsState state = GetCurrentState();
            if (operation is SetNonStrokeColorAdvanced fill && state.CurrentNonStrokingColor is TilingPatternColor pattern && (int)pattern.PaintType == 2)
            {
                state.CurrentNonStrokingColor = PatternColorWithInk(pattern, state.ColorSpaceContext.CurrentNonStrokingColorSpace, fill.Operands);
            }
            else if (operation is SetStrokeColorAdvanced stroke && state.CurrentStrokingColor is TilingPatternColor tile && (int)tile.PaintType == 2)
            {
                state.CurrentStrokingColor = PatternColorWithInk(tile, state.ColorSpaceContext.CurrentStrokingColorSpace, stroke.Operands);
            }
        }

        private static PatternInk PatternColorWithInk(TilingPatternColor pattern, ColorSpaceDetails space, ReadOnlySpan<double> components)
        {
            if (space is not PatternColorSpaceDetails { UnderlyingColourSpace: { } underlying }
                || components.Length != underlying.NumberOfColorComponents)
            {
                throw new InvalidDataException("Uncolored pattern requires a base color space and matching color components.");
            }

            // PdfPig 0.1.16 retains the selected pattern but discards SCN/scn base components.
            // Keep the ink in the graphics state's color so q/Q and nested forms preserve it.
            return new PatternInk(pattern, underlying.GetColor(components));
        }

        private void CheckPatternOperation(IGraphicsStateOperation operation)
        {
            if (_uncoloredPattern && ColorOperators.Contains(operation.Operator))
            {
                throw new InvalidDataException("Uncolored tiling pattern contains a color operator.");
            }
        }

        private void CheckPatternGraphicsState(NameToken name)
        {
            if (!_uncoloredPattern)
            {
                return;
            }

            DictionaryToken dictionary = ResourceStore.GetExtendedGraphicsStateDictionary(name)
                ?? throw new InvalidDataException("Missing graphics state in uncolored tiling pattern.");
            foreach (string key in dictionary.Data.Keys)
            {
                if (key is "TR" or "TR2" or "BG" or "BG2" or "UCR" or "UCR2" or "HT")
                {
                    throw new InvalidDataException("Uncolored tiling pattern changes a color-related graphics parameter.");
                }
            }
        }
    }

    internal sealed record PatternInk(TilingPatternColor Pattern, IColor Ink) : IColor
    {
        public ColorSpace ColorSpace => ColorSpace.Pattern;
        public (double r, double g, double b) ToRGBValues() => Ink.ToRGBValues();
    }
}
