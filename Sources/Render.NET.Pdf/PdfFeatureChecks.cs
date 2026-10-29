using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Pdf
{
    internal partial class PdfStreamProcessor
    {
        private readonly Stack<bool> _markedVisibility = new();
        private bool ContentVisible => _markedVisibility.Count == 0 || _markedVisibility.Peek();

        private void Warn(string feature)
        {
            if (!Unsupported.Contains(feature))
            {
                Unsupported.Add(feature);
            }
        }

        private void CheckState(CurrentGraphicsState state)
        {
            if (ContentVisible && (state.Overprint || state.NonStrokingOverprint))
            {
                Warn("Overprint compositing");
            }
        }

        public override void BeginMarkedContent(NameToken name, NameToken? propertyDictionaryName,
            DictionaryToken? properties)
        {
            bool visible = ContentVisible;
            if (name.Data == "OC" && visible)
            {
                DictionaryToken? group = properties ?? (propertyDictionaryName is not null
                    ? ResourceStore.GetMarkedContentPropertiesDictionary(propertyDictionaryName)
                    : null);
                if (group is null)
                {
                    Warn("Unresolved optional-content group");
                }
                else
                {
                    visible = _optionalContent.IsVisible(group);
                }
            }

            _markedVisibility.Push(visible);
        }

        public override void EndMarkedContent()
        {
            if (_markedVisibility.Count == 0)
            {
                throw new InvalidDataException("Unbalanced PDF marked content.");
            }

            _markedVisibility.Pop();
        }
    }
}
