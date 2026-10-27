using System;
using System.Numerics;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.XObjects;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfStreamProcessor
    {
        protected override void RenderXObjectImage(XObjectContentRecord record)
        {
            AddImage(XObjectFactory.ReadImage(record, PdfScanner, FilterProvider, ResourceStore), Matrix(record.AppliedTransformation));
        }

        protected override void RenderInlineImage(InlineImage inlineImage)
            => AddImage(inlineImage, Matrix(CurrentTransformationMatrix));

        private void AddImage(IPdfImage image, Matrix3x2 placement)
        {
            CurrentGraphicsState state = GetCurrentState();
            CheckState(state);
            try
            {
                PixelBuffer decoded = _images.Decode(image, Color(state.CurrentNonStrokingColor));
                Matrix3x2 pixelsToUnit = new Matrix3x2(1f / decoded.Width, 0, 0, -1f / decoded.Height, 0, 1);
                AddPaint(new ImageCommand(decoded, pixelsToUnit * placement, (float)state.AlphaConstantNonStroking, Blend(state.BlendMode)), state);
            }
            catch (NotSupportedException error)
            {
                Warn(error.Message);
            }
        }
    }
}
