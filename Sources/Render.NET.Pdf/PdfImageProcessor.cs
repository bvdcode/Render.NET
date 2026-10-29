using System;
using System.IO;
using System.Numerics;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.XObjects;

namespace Render.NET.Pdf
{
    internal partial class PdfStreamProcessor
    {
        protected override void RenderXObjectImage(XObjectContentRecord record)
        {
            AddImage(XObjectFactory.ReadImage(record, PdfScanner, FilterProvider, ResourceStore), Matrix(record.AppliedTransformation));
        }

        protected override void RenderInlineImage(InlineImage inlineImage)
            => AddImage(inlineImage, Matrix(CurrentTransformationMatrix));

        private void AddImage(IPdfImage image, Matrix3x2 placement)
        {
            if (!ContentVisible)
            {
                return;
            }

            if (_uncoloredPattern && !image.IsImageMask)
            {
                throw new InvalidDataException("Uncolored tiling pattern contains a color image.");
            }

            if (_uncoloredType3 && !image.IsImageMask)
            {
                throw new InvalidDataException("Uncolored Type 3 glyph contains a color image.");
            }

            CurrentGraphicsState state = GetCurrentState();
            try
            {
                bool patternedMask = image.IsImageMask && state.CurrentNonStrokingColor?.ColorSpace == UglyToad.PdfPig.Graphics.Colors.ColorSpace.Pattern;
                PixelBuffer decoded = _images.Decode(image, patternedMask ? new Rgba32(255, 255, 255) : Color(state.CurrentNonStrokingColor));
                Matrix3x2 pixelsToUnit = new Matrix3x2(1f / decoded.Width, 0, 0, -1f / decoded.Height, 0, 1);
                if (patternedMask)
                {
                    AddPatternedImageMask(decoded, pixelsToUnit * placement, placement, state);
                }
                else
                {
                    AddPaint(new ImageCommand(decoded, pixelsToUnit * placement, (float)state.AlphaConstantNonStroking, Blend(state.BlendMode)), state);
                }
            }
            catch (NotSupportedException error)
            {
                Warn(error.Message);
            }
        }

        private void AddPatternedImageMask(PixelBuffer mask, Matrix3x2 pixelsToPage, Matrix3x2 unitToPage, CurrentGraphicsState state)
        {
            IPdfDrawCommand? paint = CreatePatternPaint(state.CurrentNonStrokingColor, state.AlphaConstantNonStroking, Blend(state.BlendMode));
            if (paint is null)
            {
                return;
            }

            byte[]? transfer = new byte[256];
            for (int i = 0; i < transfer.Length; i++)
            {
                transfer[i] = (byte)i;
            }

            IPdfDrawCommand[]? commands = new IPdfDrawCommand[] { new ImageCommand(mask, pixelsToPage, 1, ColorBlendMode.Normal) };
            MaskPlan? maskPlan = new MaskPlan(commands, false, default, transfer);
            VectorPath bounds = new VectorPath().Rectangle(0, 0, 1, 1).Transform(unitToPage);
            AddPaint(new MaskedCommand(new PatternPaintCommand(bounds, FillRule.NonZero, paint), maskPlan), state);
        }
    }
}
