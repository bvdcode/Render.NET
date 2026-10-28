using System;
using System.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal partial class PdfStreamProcessor
    {
        internal void ProcessAnnotations(DictionaryToken page)
        {
            if (!Resolve(page, NameToken.Annots, out ArrayToken annotations))
            {
                return;
            }

            foreach (IToken item in annotations.Data)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (DirectObjectFinder.TryGet<DictionaryToken>(item, PdfScanner, out DictionaryToken? annotation))
                {
                    ProcessAnnotation(annotation);
                }
            }
        }

        private void ProcessAnnotation(DictionaryToken annotation)
        {
            int flags = Resolve(annotation, NameToken.F, out NumericToken flag) ? flag.Int : 0;
            if ((flags & 35) != 0)
            {
                return;
            }

            StreamToken? normal = AnnotationAppearance(annotation);
            if (normal is null)
            {
                return;
            }

            if ((flags & 24) != 0)
            {
                Warn("Annotation NoZoom or NoRotate flags");
            }

            if (!AnnotationPlacement(annotation, normal, out TransformationMatrix placement))
            {
                return;
            }

            PushState();
            try
            {
                ModifyCurrentTransformationMatrix(placement);
                ProcessFormXObject(normal, NameToken.Create("RenderAnnotation"));
            }
            finally
            {
                PopState();
            }
        }

        private StreamToken? AnnotationAppearance(DictionaryToken annotation)
        {
            if (!Resolve(annotation, NameToken.Ap, out DictionaryToken appearances))
            {
                if (Resolve(annotation, NameToken.Subtype, out NameToken subtype) && subtype.Data == "Widget"
                    && HasFieldValue(annotation))
                {
                    Warn("Form value without an appearance stream");
                }

                return null;
            }

            if (!Resolve(appearances, NameToken.N, out StreamToken normal))
            {
                if (!Resolve(appearances, NameToken.N, out DictionaryToken states))
                {
                    return null;
                }

                NameToken selected = Resolve(annotation, NameToken.As, out NameToken state) ? state : NameToken.Create("Off");
                if (!Resolve(states, selected, out normal))
                {
                    if (selected.Data != "Off")
                    {
                        Warn("Missing annotation appearance state " + selected.Data);
                    }
                    return null;
                }
            }

            return normal;
        }

        private bool HasFieldValue(DictionaryToken field)
        {
            for (int depth = 0; depth < 32; depth++)
            {
                if (field.ContainsKey(NameToken.V))
                {
                    return true;
                }

                if (!Resolve(field, NameToken.Parent, out DictionaryToken parent))
                {
                    return false;
                }

                field = parent;
            }

            Warn("Form field parent nesting exceeds 32");
            return false;
        }

        private bool AnnotationPlacement(DictionaryToken annotation, StreamToken normal, out TransformationMatrix placement)
        {
            placement = TransformationMatrix.Identity;
            if (!Resolve(annotation, NameToken.Rect, out ArrayToken rectangle) || !TryRectangle(rectangle, out PdfRectangle rect)
                || !Resolve(normal.StreamDictionary, NameToken.Bbox, out ArrayToken bounds) || !TryRectangle(bounds, out PdfRectangle box))
            {
                Warn("Annotation appearance without valid bounds");
                return false;
            }

            TransformationMatrix matrix = Resolve(normal.StreamDictionary, NameToken.Matrix, out ArrayToken values)
                ? TransformationMatrix.FromArray(values.Data.OfType<NumericToken>().Select(x => x.Double).ToArray()) : TransformationMatrix.Identity;
            PdfRectangle transformed = matrix.Transform(box).Normalise();
            if (transformed.Width <= 0 || transformed.Height <= 0 || rect.Width <= 0 || rect.Height <= 0)
            {
                return false;
            }

            placement = TransformationMatrix.GetTranslationMatrix(-transformed.Left, -transformed.Bottom)
                    .Multiply(TransformationMatrix.GetScaleMatrix(rect.Width / transformed.Width, rect.Height / transformed.Height))
                    .Multiply(TransformationMatrix.GetTranslationMatrix(rect.Left, rect.Bottom));
            return true;
        }

        private bool Resolve<T>(DictionaryToken dictionary, NameToken key, out T result) where T : class, IToken
        {
            result = null!;
            if (dictionary.TryGet(key, out IToken token) && DirectObjectFinder.TryGet<T>(token, PdfScanner, out T? resolved))
            {
                result = resolved;
                return true;
            }

            return false;
        }

        private static bool TryRectangle(ArrayToken array, out PdfRectangle rectangle)
        {
            double[] numbers = array.Data.OfType<NumericToken>().Select(x => x.Double).ToArray();
            rectangle = numbers.Length == 4 ? new PdfRectangle(numbers[0], numbers[1], numbers[2], numbers[3]).Normalise() : default;
            return numbers.Length == 4;
        }
    }
}
