using System;
using System.Collections.Generic;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal class PdfPageFactory(IPdfTokenScanner scanner, IResourceStore resources, ILookupFilterProvider filters,
        IPageContentParser parser, ParsingOptions options) : BasePageFactory<PdfPage>(scanner, resources, filters, parser, options)
    {
        protected override PdfPage ProcessPage(int pageNumber, DictionaryToken dictionary, NamedDestinations namedDestinations,
            MediaBox mediaBox, CropBox cropBox, UserSpaceUnit userSpaceUnit, PageRotationDegrees rotation,
            TransformationMatrix initialMatrix, IReadOnlyList<IGraphicsStateOperation> operations)
        {
            double units = userSpaceUnit.PointMultiples;
            PdfRectangle visible = cropBox.GetVisibleBounds(rotation);
            double width = visible.Width * units;
            double height = visible.Height * units;
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("The PDF page has invalid dimensions.");
            }

            TransformationMatrix matrix = initialMatrix.Multiply(TransformationMatrix.GetScaleMatrix(units, units));
            PdfStreamProcessor? processor = new PdfStreamProcessor(pageNumber, ResourceStore, PdfScanner, PageContentParser, FilterProvider,
                cropBox, userSpaceUnit, rotation, matrix, (RenderParsingOptions)ParsingOptions, dictionary);
            processor.PushState();
            processor.Process(pageNumber, operations);
            processor.PopState();
            processor.ProcessAnnotations(dictionary);
            return new PdfPage(pageNumber, width, height, processor.Commands, processor.Unsupported, processor.SubstitutedFonts);
        }
    }
}
