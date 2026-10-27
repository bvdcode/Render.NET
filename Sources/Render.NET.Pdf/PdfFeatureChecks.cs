using System.Collections.Generic;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfStreamProcessor
    {
        private void Warn(string feature)
        {
            if (!Unsupported.Contains(feature))
            {
                Unsupported.Add(feature);
            }
        }

        private void CheckState(CurrentGraphicsState state)
        {
            if (state.SoftMask != null)
            {
                Warn("Graphics-state soft masks");
            }

            if ((int)state.BlendMode > 11)
            {
                Warn("Non-separable blend mode " + state.BlendMode);
            }

            if (state.CurrentNonStrokingColor?.ColorSpace == ColorSpace.Pattern || state.CurrentStrokingColor?.ColorSpace == ColorSpace.Pattern)
            {
                Warn("Pattern fills and strokes");
            }

            if (state.Overprint || state.NonStrokingOverprint)
            {
                Warn("Overprint compositing");
            }
        }

        internal void CheckPageFeatures(DictionaryToken dictionary)
        {
            if (dictionary.TryGet(NameToken.Annots, out IToken token)
                && UglyToad.PdfPig.Parser.Parts.DirectObjectFinder.TryGet<ArrayToken>(token, PdfScanner, out var annotations))
            {
                foreach (IToken item in annotations.Data)
                {
                    if (UglyToad.PdfPig.Parser.Parts.DirectObjectFinder.TryGet<DictionaryToken>(item, PdfScanner, out var annotation)
                        && annotation.ContainsKey(NameToken.Ap))
                    {
                        Warn("Annotation appearance streams");
                    }
                }
            }
        }

        protected override void ProcessFormXObject(StreamToken formStream, NameToken name)
        {
            if (formStream.StreamDictionary.ContainsKey(NameToken.Group))
            {
                Warn("Transparency group compositing");
            }

            base.ProcessFormXObject(formStream, name);
        }

        public override void PaintShading(NameToken shadingName) => Warn("Shading " + shadingName);

        public override void BeginMarkedContent(NameToken name, NameToken? propertyDictionaryName,
            DictionaryToken? properties)
        {
            if (name.Data == "OC")
            {
                Warn("Optional content visibility");
            }
        }

        public override void EndMarkedContent()
        {
        }
    }
}
