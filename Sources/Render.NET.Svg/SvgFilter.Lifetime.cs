using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal partial record SvgFilter
    {
        private Dictionary<string, int> LastUses()
        {
            var uses = new Dictionary<string, int>(System.StringComparer.Ordinal);
            for (int index = 0; index < Primitives.Length; index++)
            {
                if (UsesSecondInput(Primitives[index]) && Primitives[index].Attribute("in2") is null)
                {
                    uses["SourceGraphic"] = index;
                }

                foreach (XAttribute attribute in Primitives[index].DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "in" or "in2"))
                {
                    uses[attribute.Value] = index;
                    if (attribute.Value == "SourceAlpha")
                    {
                        uses["SourceGraphic"] = index;
                    }
                }
            }

            return uses;
        }

        private static void ReleaseResults(Dictionary<string, PixelBuffer> results,
            Dictionary<PixelBuffer, (float X, float Y, float Width, float Height)> regions, PixelBuffer previous,
            Dictionary<string, int> lastUses, int index, SvgBufferScope buffers)
        {
            foreach (string name in results.Keys.Where(name => lastUses.GetValueOrDefault(name, -1) <= index).ToArray())
            {
                results.Remove(name);
            }

            HashSet<PixelBuffer> retained = results.Values.ToHashSet();
            retained.Add(previous);
            foreach (PixelBuffer image in regions.Keys.Where(image => !retained.Contains(image)).ToArray())
            {
                regions.Remove(image);
            }

            buffers.Retain(retained);
        }

        private static bool CanOverwrite(PixelBuffer input, Dictionary<string, PixelBuffer> results, Dictionary<string, int> lastUses, int index, SvgBufferScope buffers)
            => buffers.Owns(input) && !results.Any(pair => ReferenceEquals(pair.Value, input) && lastUses.GetValueOrDefault(pair.Key, -1) > index);

        private static bool UsesSecondInput(XElement primitive) => primitive.Name.LocalName is "feComposite" or "feBlend" or "feDisplacementMap";
    }
}
