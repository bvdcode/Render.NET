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
    internal sealed partial class PdfStreamProcessor
    {
        private readonly Dictionary<(IType3Font Font, int Code), Type3Glyph?> _type3Glyphs = new();
        private readonly HashSet<(IType3Font Font, int Code)> _activeType3Glyphs = new();
        private readonly HashSet<IReadOnlyList<IGraphicsStateOperation>> _type3Programs = new(ReferenceEqualityComparer.Instance);
        private readonly DictionaryToken _pageDictionary;
        private DictionaryToken? _type3PageResources;
        private bool _uncoloredType3;

        private void RenderType3Glyph(IType3Font font, int code, TransformationMatrix textToPage)
        {
            Type3Glyph? glyph = Type3GlyphProgram(font, code);
            if (glyph == null)
            {
                return;
            }

            if (++_captureDepth > 8 || !_activeType3Glyphs.Add((font, code)))
            {
                throw new InvalidDataException("Recursive Type 3 glyph or glyph/pattern/mask nesting exceeds eight.");
            }

            var savedStack = GraphicsStack;
            bool savedUncolored = _uncoloredType3;
            try
            {
                var state = GetCurrentState().DeepClone();
                state.CurrentTransformationMatrix = font.GetFontMatrix().Multiply(textToPage);
                state.CurrentNonStrokingColor = PreservePatternOrigin(state.CurrentNonStrokingColor);
                state.CurrentStrokingColor = PreservePatternOrigin(state.CurrentStrokingColor);
                GraphicsStack = new Stack<CurrentGraphicsState>();
                GraphicsStack.Push(state);
                _uncoloredType3 |= glyph.Stencil;
                ProcessFormXObject(glyph.Form, NameToken.Create("RenderType3Glyph"));
            }
            finally
            {
                GraphicsStack = savedStack;
                _uncoloredType3 = savedUncolored;
                _activeType3Glyphs.Remove((font, code));
                _captureDepth--;
            }
        }

        private Type3Glyph? Type3GlyphProgram(IType3Font font, int code)
        {
            if (_type3Glyphs.TryGetValue((font, code), out Type3Glyph? glyph))
            {
                return glyph;
            }

            if (font.TryGetCharProc(code, out StreamToken? stream))
            {
                IReadOnlyList<IGraphicsStateOperation> operations = GetFormOperations(stream);
                bool stencil = ValidateType3Program(operations);
                var dictionary = new Dictionary<NameToken, IToken>();
                foreach (NameToken key in new[] { NameToken.Filter, NameToken.DecodeParms })
                {
                    if (stream.StreamDictionary.Data.TryGetValue(key, out IToken? value))
                    {
                        dictionary.Add(key, value);
                    }
                }

                dictionary.Add(NameToken.Resources, font.Type3Resources ?? Type3PageResources());

                if (operations[0] is Type3SetGlyphWidthAndBoundingBox bounds)
                {
                    dictionary.Add(NameToken.Bbox, new ArrayToken(new IToken[]
                    {
                        new NumericToken(bounds.LowerLeftX), new NumericToken(bounds.LowerLeftY),
                        new NumericToken(bounds.UpperRightX), new NumericToken(bounds.UpperRightY)
                    }));
                }

                // Reuse form resource/state scoping, with a cached stream per glyph. CharProc
                // dictionaries do not supply a form Matrix or the font-wide clipping rectangle.
                var form = new StreamToken(new DictionaryToken(dictionary), stream.Data);
                _type3Programs.Add(GetFormOperations(form));
                glyph = new Type3Glyph(form, stencil);
            }

            _type3Glyphs.Add((font, code), glyph);
            return glyph;
        }

        private static bool ValidateType3Program(IReadOnlyList<IGraphicsStateOperation> operations)
        {
            bool stencil;
            switch (operations.Count == 0 ? null : operations[0])
            {
                case Type3SetGlyphWidth width when double.IsFinite(width.HorizontalDisplacement) && width.VerticalDisplacement == 0:
                    stencil = false;
                    break;
                case Type3SetGlyphWidthAndBoundingBox box when double.IsFinite(box.HorizontalDisplacement) && box.VerticalDisplacement == 0
                    && double.IsFinite(box.LowerLeftX + box.LowerLeftY + box.UpperRightX + box.UpperRightY)
                    && box.LowerLeftX <= box.UpperRightX && box.LowerLeftY <= box.UpperRightY:
                    stencil = true;
                    break;
                default:
                    throw new InvalidDataException("Type 3 glyph must begin with valid d0 or d1 metrics.");
            }

            for (int i = 1; i < operations.Count; i++)
            {
                if (operations[i].Operator is "d0" or "d1")
                {
                    throw new InvalidDataException("Type 3 glyph contains repeated metrics.");
                }
            }

            return stencil;
        }

        private DictionaryToken Type3PageResources()
        {
            if (_type3PageResources != null)
            {
                return _type3PageResources;
            }

            var visited = new HashSet<DictionaryToken>(ReferenceEqualityComparer.Instance);
            DictionaryToken page = _pageDictionary;
            while (visited.Add(page))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (Resolve(page, NameToken.Resources, out DictionaryToken resources))
                {
                    _type3PageResources = resources;
                    return resources;
                }

                if (!Resolve(page, NameToken.Parent, out page))
                {
                    _type3PageResources = new DictionaryToken(new Dictionary<NameToken, IToken>());
                    return _type3PageResources;
                }
            }

            throw new InvalidDataException("Cyclic page resources for Type 3 font.");
        }

        private bool SkipType3Operation(IGraphicsStateOperation operation, bool glyphProgram)
        {
            if (operation.Operator is "d0" or "d1")
            {
                if (!glyphProgram)
                {
                    throw new InvalidDataException("Type 3 metrics outside a glyph program.");
                }

                return true;
            }

            // ISO 32000-1, 9.6.5: color operators in a d1 CharProc are ignored.
            return _uncoloredType3 && ColorOperators.Contains(operation.Operator);
        }

        private IColor? PreservePatternOrigin(IColor? color)
            => color?.ColorSpace == ColorSpace.Pattern && color is not PatternOrigin
                ? new PatternOrigin(color, _patternMatrix, _patternState) : color;

        private sealed record Type3Glyph(StreamToken Form, bool Stencil);
    }

    internal sealed record PatternOrigin(IColor Color, TransformationMatrix Matrix, CurrentGraphicsState State) : IColor
    {
        public ColorSpace ColorSpace => ColorSpace.Pattern;
        public (double r, double g, double b) ToRGBValues() => Color.ToRGBValues();
    }
}
