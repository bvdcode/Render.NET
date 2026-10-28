using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal partial class PdfStreamProcessor
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
            if (state.Overprint || state.NonStrokingOverprint)
            {
                Warn("Overprint compositing");
            }
        }

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
