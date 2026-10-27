using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal sealed partial class SvgSceneBuilder
    {
        private readonly Dictionary<string, PixelBuffer?> _images = new(StringComparer.Ordinal);
        private PixelBuffer? Image(string? href)
        {
            if (href == null)
            {
                Warn("Missing SVG image href");
                return null;
            }

            if (_images.TryGetValue(href, out PixelBuffer? cached))
            {
                return cached;
            }

            PixelBuffer? image = null;
            if (href.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase))
            {
                int comma = href.IndexOf(',');
                if (comma < 0 || !href[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Embedded PNG must use a base64 data URI.");
                }

                image = SvgPng.Decode(Convert.FromBase64String(href[(comma + 1)..]), _limits.MaximumImagePixels, _token);
            }
            else if (_limits.ImageResolver?.Invoke(href) is { } resolved)
            {
                if ((long)resolved.Width * resolved.Height > _limits.MaximumImagePixels)
                {
                    throw new InvalidDataException("Resolved SVG image exceeds MaximumImagePixels.");
                }

                image = SvgPixels.Copy(resolved);
            }
            else
            {
                Warn("Unresolved SVG image resource");
            }

            _images.Add(href, image);
            return image;
        }

        private Matrix3x2 ImagePlacement(XElement element, PixelBuffer image)
        {
            float width = Length(element, "width", image.Width, _width), height = Length(element, "height", image.Height, _height);
            if (width < 0 || height < 0)
            {
                throw new InvalidDataException("Negative SVG image dimensions.");
            }

            return SvgRenderer.Viewport((0, 0, image.Width, image.Height), width, height, (string?)element.Attribute("preserveAspectRatio") ?? "xMidYMid meet") * Matrix3x2.CreateTranslation(Length(element, "x", 0, _width), Length(element, "y", 0, _height));
        }
    }
}
