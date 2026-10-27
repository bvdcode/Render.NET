using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal sealed partial class SvgSceneBuilder
    {
        private readonly Dictionary<SvgFontRequest, SvgTrueType[]> _fonts = new();
        private readonly Dictionary<string, SvgTrueType> _bundledFonts = new(StringComparer.Ordinal);
        private sealed class TextSpan
        {
            internal required XElement Element;
            internal required Dictionary<string, string> Style;
            internal required TextSpan? Parent;
            internal float Size;
            internal Matrix3x2 Transform;
            internal int Index;
            internal float[] X = Array.Empty<float>(), Y = Array.Empty<float>(), Dx = Array.Empty<float>(), Dy = Array.Empty<float>(), Rotate = Array.Empty<float>();
        }

        private sealed record TextCharacter(Rune Rune, TextSpan Span, bool Preserve);
        private sealed record PlacedGlyph(VectorPath Path, Dictionary<string, string> Style, float X, float Y, float Advance, float Rotation, TextSpan Span, bool Chunk);
        private SvgNode[] TextNodes(XElement e, Dictionary<string, string> style, int depth)
        {
            var raw = new List<TextCharacter>();
            GatherText(e, style, null, 16, Matrix3x2.Identity, false, raw, depth);
            var text = NormalizeText(raw);
            var glyphs = new List<PlacedGlyph>();
            float x = 0, y = 0;
            SvgTrueType? previousFont = null;
            int previousGlyph = 0;
            foreach (TextCharacter character in text)
            {
                _token.ThrowIfCancellationRequested();
                bool chunk = Position(character.Span, ref x, ref y, out float rotation);
                var(font, glyph) = TextFont(character.Span.Style, character.Rune.Value);
                float scale = character.Span.Size / font.Units;
                if (!chunk && previousFont == font && Value(character.Span.Style, "font-kerning", "auto") != "none")
                {
                    x += font.Kerning(previousGlyph, glyph) * scale;
                }

                VectorPath path = font.Outline(glyph).Transform(Matrix3x2.CreateScale(scale, -scale));
                float spacing = TextLength(Value(character.Span.Style, "letter-spacing", "0"), character.Span.Size);
                if (Rune.IsWhiteSpace(character.Rune))
                {
                    spacing += TextLength(Value(character.Span.Style, "word-spacing", "0"), character.Span.Size);
                }

                float advance = (font.Advance(glyph) * scale) + spacing;
                Decorate(path, character.Span.Style, advance, character.Span.Size);
                glyphs.Add(new PlacedGlyph(path, character.Span.Style, x, y + Baseline(character.Span.Style, font, character.Span.Size), advance, rotation, character.Span, chunk || glyphs.Count == 0));
                x += advance;
                previousFont = font;
                previousGlyph = glyph;
            }

            AdjustTextLength(e, glyphs);
            Anchor(glyphs);
            var result = new List<SvgNode>();
            var spans = new Dictionary<TextSpan, (SvgNode Node, List<SvgNode> Children)>();
            List<SvgNode> Container(TextSpan span)
            {
                if (span.Parent == null)
                {
                    return result;
                }

                if (!spans.TryGetValue(span, out var group))
                {
                    var children = new List<SvgNode>();
                    SvgNode node = Empty with
                    {
                        Opacity = Opacity(span.Style, "opacity"),
                        Clip = Clip(Value(span.Style, "clip-path", "none"), depth + 1),
                        Mask = Mask(Value(span.Style, "mask", "none"), depth + 1),
                        Filter = Filter(Value(span.Style, "filter", "none"), span.Style, depth + 1)
                    };
                    spans.Add(span, group = (node, children));
                    Container(span.Parent).Add(node);
                }

                return group.Children;
            }

            foreach (PlacedGlyph glyph in glyphs)
            {
                VectorPath path = PlaceTextGlyph(glyph);
                path = ValidatePath(path, glyph.Style) ?? new VectorPath();
                Container(glyph.Span).Add(new SvgNode(path, Paint(Value(glyph.Style, "fill", "black"), glyph.Style), Paint(Value(glyph.Style, "stroke", "none"), glyph.Style), Pen(glyph.Style), Rule(Value(glyph.Style, "fill-rule", "nonzero")), FillRule.NonZero, Matrix3x2.Identity, 1, Opacity(glyph.Style, "fill-opacity"), Opacity(glyph.Style, "stroke-opacity"), null, Array.Empty<SvgNode>()));
            }

            SvgNode Finish(SvgNode node)
            {
                foreach (var group in spans.Values)
                {
                    if (ReferenceEquals(group.Node, node))
                    {
                        return node with
                        {
                            Children = group.Children.Select(Finish).ToArray()
                        };
                    }
                }

                return node;
            }

            return result.Select(Finish).ToArray();
        }

        private void GatherText(XElement element, Dictionary<string, string> style, TextSpan? parent, float parentSize, Matrix3x2 transform, bool preserve, List<TextCharacter> text, int depth)
        {
            Count(depth);
            float size = TextLength(Value(style, "font-size", "16"), parentSize);
            if (size < 0 || size > 1_000_000)
            {
                throw new InvalidDataException("Invalid SVG font size.");
            }

            style = new Dictionary<string, string>(style)
            {
                ["font-size"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            TextFeatures(style);
            var span = new TextSpan
            {
                Element = element,
                Style = style,
                Parent = parent,
                Size = size,
                Transform = transform,
                X = TextPositions(element, "x", _width, size),
                Y = TextPositions(element, "y", _height, size),
                Dx = TextPositions(element, "dx", _width, size),
                Dy = TextPositions(element, "dy", _height, size),
                Rotate = element.Attribute("rotate") is { } rotate ? SvgNumbers.List(rotate.Value) : Array.Empty<float>()
            };
            string space = (string?)element.Attribute(XNamespace.Xml + "space") ?? "";
            preserve = space == "preserve" || (space != "default" && preserve) || Value(style, "white-space", "normal").StartsWith("pre", StringComparison.Ordinal);
            GatherChildren(element, span, size, transform, preserve, text, depth);
        }

        private void GatherChildren(XElement element, TextSpan span, float size, Matrix3x2 transform, bool preserve, List<TextCharacter> text, int depth)
        {
            Dictionary<string, string> style = span.Style;
            foreach (XNode node in element.Nodes())
            {
                if (node is XText value)
                {
                    foreach (Rune rune in value.Value.EnumerateRunes())
                    {
                        if (text.Count >= _limits.MaximumPathCommands)
                        {
                            throw new InvalidDataException("SVG text exceeds MaximumPathCommands.");
                        }

                        text.Add(new TextCharacter(rune, span, preserve));
                    }
                }
                else if (node is XElement child)
                {
                    if (child.Name.LocalName is "tspan" or "textPath" or "a")
                    {
                        Dictionary<string, string> childStyle = Style(child, style);
                        GatherText(child, childStyle, span, size, SvgNumbers.Transform((string?)child.Attribute("transform")) * transform, preserve, text, depth + 1);
                    }
                    else if (!Metadata.Contains(child.Name.LocalName))
                    {
                        Warn("SVG text element: " + child.Name.LocalName);
                    }
                }
            }
        }

        private static List<TextCharacter> NormalizeText(List<TextCharacter> source)
        {
            var result = new List<TextCharacter>();
            bool space = true;
            foreach (TextCharacter item in source)
            {
                bool whitespace = item.Rune.Value is 9 or 10 or 13 or 32;
                if (item.Preserve)
                {
                    result.Add(whitespace ? item with { Rune = new Rune(' ') } : item);
                    space = whitespace;
                }
                else if (!whitespace || !space)
                {
                    result.Add(whitespace ? item with { Rune = new Rune(' ') } : item);
                    space = whitespace;
                }
            }

            if (result.Count > 0 && !result[^1].Preserve && result[^1].Rune.Value == 32)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private void TextFeatures(Dictionary<string, string> style)
        {
            foreach (var feature in new[]
            {
                ("direction", "ltr"),
                ("unicode-bidi", "normal"),
                ("writing-mode", "horizontal-tb"),
                ("font-feature-settings", "normal"),
                ("font-variant", "normal")
            }

            )
            {
                if (Value(style, feature.Item1, feature.Item2) != feature.Item2)
                {
                    Warn("SVG text property: " + feature.Item1);
                }
            }
        }

        private static void Decorate(VectorPath path, Dictionary<string, string> style, float advance, float size)
        {
            float thickness = Math.Max(size / 16, .5f);
            foreach (string name in Value(style, "text-decoration", "none").Split(' '))
            {
                float? y = name switch
                {
                    "underline" => size * .1f,
                    "overline" => -size * .8f,
                    "line-through" => -size * .3f,
                    _ => null
                };
                if (y.HasValue)
                {
                    path.Rectangle(0, y.Value, advance, thickness);
                }
            }
        }

        private static bool Position(TextSpan span, ref float x, ref float y, out float rotation)
        {
            var ancestors = new Stack<TextSpan>();
            for (TextSpan? current = span; current != null; current = current.Parent)
            {
                ancestors.Push(current);
            }

            bool absolute = false;
            float dx = 0, dy = 0;
            rotation = 0;
            foreach (TextSpan current in ancestors)
            {
                int i = current.Index++;
                if (i < current.X.Length)
                {
                    x = current.X[i];
                    absolute = true;
                }

                if (i < current.Y.Length)
                {
                    y = current.Y[i];
                    absolute = true;
                }

                if (i < current.Dx.Length)
                {
                    dx = current.Dx[i];
                }

                if (i < current.Dy.Length)
                {
                    dy = current.Dy[i];
                }

                if (current.Rotate.Length > 0)
                {
                    rotation = current.Rotate[Math.Min(i, current.Rotate.Length - 1)];
                }
            }

            x += dx;
            y += dy;
            return absolute;
        }

        private static float TextLength(string value, float size)
        {
            if (value is "normal" or "none")
            {
                return 0;
            }

            if (value.EndsWith("rem", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^3]) * 16;
            }

            if (value.EndsWith("em", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^2]) * size;
            }

            if (value.EndsWith("ex", StringComparison.Ordinal))
            {
                return SvgLength.Parse(value[..^2]) * size * .5f;
            }

            return SvgLength.Parse(value, size);
        }

        private static float[] TextPositions(XElement e, string name, float percent, float size)
        {
            if (e.Attribute(name) is not { } attribute)
            {
                return Array.Empty<float>();
            }

            string[] items = attribute.Value.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return items.Select(item => item.EndsWith('%') ? SvgLength.Parse(item, percent) : TextLength(item, size)).ToArray();
        }

        private static float Baseline(Dictionary<string, string> style, SvgTrueType font, float size)
        {
            string value = Value(style, "dominant-baseline", Value(style, "alignment-baseline", "auto"));
            float offset = value switch
            {
                "middle" => font.XHeight / 2,
                "central" => (font.Ascent + font.Descent) / 2,
                "hanging" => font.Ascent * .8f,
                "text-before-edge" or "text-top" => font.Ascent,
                "text-after-edge" or "text-bottom" => font.Descent,
                _ => 0
            };
            string shift = Value(style, "baseline-shift", "0");
            return ((offset * size) / font.Units) - (shift switch
            {
                "super" => size * .6f,
                "sub" => -size * .3f,
                "baseline" => 0,
                _ => TextLength(shift, size)});
        }

        private (SvgTrueType Font, int Glyph) TextFont(Dictionary<string, string> style, int character)
        {
            string family = Value(style, "font-family", "sans-serif");
            string weight = Value(style, "font-weight", "400");
            int numeric = weight is "bold" or "bolder" ? 700 : int.TryParse(weight, out int parsed) ? parsed : 400;
            var request = new SvgFontRequest(family, numeric, Value(style, "font-style", "normal") is "italic" or "oblique");
            SvgTrueType[] fonts = TextFonts(request);
            foreach (SvgTrueType font in fonts)
            {
                int glyph = font.Glyph(character);
                if (glyph != 0)
                {
                    return (font, glyph);
                }
            }

            Warn("Missing SVG font glyph U+" + character.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            return (fonts[0], 0);
        }

        private SvgTrueType[] TextFonts(SvgFontRequest request)
        {
            if (!_fonts.TryGetValue(request, out SvgTrueType[]? fonts))
            {
                var list = new List<SvgTrueType>();
                foreach (string candidate in request.Family.Split(','))
                {
                    ReadOnlyMemory<byte> custom = _limits.FontResolver?.Invoke(request with { Family = candidate.Trim().Trim('\'', '"') }) ?? default;
                    if (!custom.IsEmpty)
                    {
                        list.Add(new SvgTrueType(custom));
                    }
                }

                string variant = request.Weight >= 600 ? request.Italic ? "BoldItalic" : "Bold" : request.Italic ? "Italic" : "Regular";
                list.Add(BundledFont("Aileron-" + variant));
                list.Add(BundledFont("Canada1500-" + variant));
                fonts = list.ToArray();
                _fonts.Add(request, fonts);
            }

            return fonts;
        }

        private SvgTrueType BundledFont(string name)
        {
            if (_bundledFonts.TryGetValue(name, out SvgTrueType? cached))
            {
                return cached;
            }

            using Stream input = typeof(SvgReader).Assembly.GetManifestResourceStream("Render.NET.Svg.Fonts." + name + ".ttf") ?? throw new InvalidOperationException("Bundled SVG font is missing.");
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            var font = new SvgTrueType(memory.ToArray());
            _bundledFonts.Add(name, font);
            return font;
        }

        private static void Anchor(List<PlacedGlyph> glyphs)
        {
            int start = 0;
            while (start < glyphs.Count)
            {
                int end = start + 1;
                while (end < glyphs.Count && !glyphs[end].Chunk)
                {
                    end++;
                }

                float width = (glyphs[end - 1].X + glyphs[end - 1].Advance) - glyphs[start].X;
                float shift = Value(glyphs[start].Style, "text-anchor", "start") switch
                {
                    "middle" => width / 2,
                    "end" => width,
                    _ => 0
                };
                for (int i = start; i < end; i++)
                {
                    glyphs[i] = glyphs[i] with
                    {
                        X = glyphs[i].X - shift
                    };
                }

                start = end;
            }
        }

        private static void AdjustTextLength(XElement e, List<PlacedGlyph> glyphs)
        {
            if (e.Attribute("textLength") is not { } length || glyphs.Count == 0)
            {
                return;
            }

            float target = SvgLength.Parse(length.Value), actual = (glyphs[^1].X + glyphs[^1].Advance) - glyphs[0].X;
            if (target < 0 || actual <= 0)
            {
                return;
            }

            bool scale = (string?)e.Attribute("lengthAdjust") == "spacingAndGlyphs";
            float origin = glyphs[0].X;
            for (int i = 0; i < glyphs.Count; i++)
            {
                PlacedGlyph g = glyphs[i];
                glyphs[i] = scale ? g with
                {
                    X = origin + (((g.X - origin) * target) / actual),
                    Path = g.Path.Transform(Matrix3x2.CreateScale(target / actual, 1)),
                    Advance = (g.Advance * target) / actual
                }

                : g with
                {
                    X = g.X + (((target - actual) * i) / Math.Max(1, glyphs.Count - 1))
                };
            }
        }

        private VectorPath PlaceTextGlyph(PlacedGlyph glyph)
        {
            TextSpan? path = glyph.Span;
            while (path != null && path.Element.Name.LocalName != "textPath")
            {
                path = path.Parent;
            }

            Matrix3x2 placement = Matrix3x2.CreateRotation((glyph.Rotation * MathF.PI) / 180) * Matrix3x2.CreateTranslation(glyph.X, glyph.Y);
            if (path != null)
            {
                XElement? target = Reference(Href(path.Element));
                VectorPath? curve = target == null ? null : Shape(target)?.Transform(SvgNumbers.Transform((string?)target.Attribute("transform")));
                if (curve == null)
                {
                    return new VectorPath();
                }

                float offset = SvgLength.Parse((string?)path.Element.Attribute("startOffset") ?? "0", PathLength(curve));
                if (!PointOnPath(curve, glyph.X + offset + (glyph.Advance / 2), out Vector2 point, out float angle))
                {
                    return new VectorPath();
                }

                placement = Matrix3x2.CreateTranslation(-glyph.Advance / 2, glyph.Y) * Matrix3x2.CreateRotation(angle + ((glyph.Rotation * MathF.PI) / 180)) * Matrix3x2.CreateTranslation(point);
            }

            return glyph.Path.Transform(placement * glyph.Span.Transform);
        }

        private float PathLength(VectorPath path) => PathSegments(path).Sum(segment => segment.Length);
        private List<(Vector2 A, Vector2 B, float Length)> PathSegments(VectorPath path)
        {
            var result = new List<(Vector2, Vector2, float)>();
            foreach (PathContour contour in PathFlattener.Flatten(path, Matrix3x2.Identity, .05f, _token))
            {
                for (int i = 1; i < contour.Points.Count; i++)
                {
                    result.Add((contour.Points[i - 1], contour.Points[i], Vector2.Distance(contour.Points[i - 1], contour.Points[i])));
                }

                if (contour.Closed && contour.Points.Count > 1)
                {
                    result.Add((contour.Points[^1], contour.Points[0], Vector2.Distance(contour.Points[^1], contour.Points[0])));
                }
            }

            return result;
        }

        private bool PointOnPath(VectorPath path, float distance, out Vector2 point, out float angle)
        {
            foreach (var segment in PathSegments(path))
            {
                if (distance >= 0 && distance <= segment.Length && segment.Length > 0)
                {
                    point = Vector2.Lerp(segment.A, segment.B, distance / segment.Length);
                    angle = MathF.Atan2(segment.B.Y - segment.A.Y, segment.B.X - segment.A.X);
                    return true;
                }

                distance -= segment.Length;
            }

            point = default;
            angle = 0;
            return false;
        }
    }
}
