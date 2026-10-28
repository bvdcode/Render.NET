using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal partial class SvgSceneBuilder
    {
        private readonly XElement _root;
        private readonly SvgReaderOptions _limits;
        private readonly CancellationToken _token;
        private readonly Dictionary<string, XElement> _ids = new(StringComparer.Ordinal);
        private readonly HashSet<XElement> _active = new();
        private readonly Dictionary<XElement, SvgGradient?> _gradients = new();
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private readonly List<(string Selector, int Specificity, Dictionary<string, string> Values)> _rules = new();
        private float _width;
        private float _height;
        private int _elements;
        private int _commands;
        private static readonly HashSet<string> Definitions = new("defs symbol clipPath mask filter pattern marker linearGradient radialGradient style title desc metadata".Split(' '));
        private static readonly HashSet<string> Containers = new("g svg a symbol clipPath mask".Split(' '));
        private static readonly HashSet<string> Metadata = new("title desc metadata namedview".Split(' '));
        private static readonly HashSet<string> Inherited = new("fill stroke color fill-opacity stroke-opacity fill-rule clip-rule stroke-width stroke-linecap stroke-linejoin stroke-miterlimit stroke-dasharray stroke-dashoffset visibility font-family font-size font-weight font-style font-variant font-kerning font-feature-settings text-anchor letter-spacing word-spacing dominant-baseline alignment-baseline baseline-shift text-decoration direction unicode-bidi writing-mode white-space color-interpolation color-interpolation-filters".Split(' '));
        private static readonly HashSet<string> Supported = new(Inherited.Concat("opacity display clip-path stop-color stop-opacity mask mask-type filter color-interpolation color-interpolation-filters mix-blend-mode isolation flood-color flood-opacity lighting-color".Split(' ')));
        internal SvgSceneBuilder(XElement root, SvgReaderOptions limits, CancellationToken token)
        {
            _root = root;
            _limits = limits;
            _token = token;
        }

        internal SvgDocument Build()
        {
            if (_root.Name.LocalName != "svg" || !IsSvgElement(_root))
            {
                throw new InvalidDataException("Expected an SVG root element.");
            }

            Index(_root, 0);
            return BuildViewport();
        }

        private SvgDocument BuildViewport()
        {
            float[]? v = _root.Attribute("viewBox") is { } box ? SvgNumbers.List(box.Value) : null;
            if (v is not null && (v.Length != 4 || v[2] <= 0 || v[3] <= 0))
            {
                throw new InvalidDataException("Invalid SVG viewBox.");
            }

            _width = Length(_root, "width", v?[2] ?? 300, 300);
            _height = Length(_root, "height", v?[3] ?? 150, 150);
            if (_width <= 0 || _height <= 0)
            {
                throw new InvalidDataException("SVG viewport must be positive.");
            }

            (float, float, float, float) viewBox = v is null ? (0, 0, _width, _height) : (v[0], v[1], v[2], v[3]);
            float intrinsicWidth = _width, intrinsicHeight = _height;
            _width = viewBox.Item3;
            _height = viewBox.Item4;
            SvgNode node = Node(_root, new Dictionary<string, string>(), 0, true);
            return new SvgDocument(node, intrinsicWidth, intrinsicHeight, viewBox, (string?)_root.Attribute("preserveAspectRatio") ?? "xMidYMid meet", _warnings.Order().ToArray());
        }

        private static bool IsSvgElement(XElement element) => element.Name.NamespaceName.Length == 0 || element.Name.NamespaceName == "http://www.w3.org/2000/svg";

        private void Index(XElement element, int depth, bool svgContent = true)
        {
            Count(depth);
            svgContent = svgContent && IsSvgElement(element) && !Metadata.Contains(element.Name.LocalName);
            if (svgContent)
            {
                if (element.Attribute("id") is { } id && !_ids.TryAdd(id.Value, element))
                {
                    throw new InvalidDataException("Duplicate SVG id: " + id.Value);
                }

                if (element.Name.LocalName == "style")
                {
                    StyleSheet(element.Value);
                }
            }

            foreach (XElement child in element.Elements().ToArray())
            {
                Index(child, depth + 1, svgContent);
            }

            if (!svgContent)
            {
                element.Remove();
            }
        }

        private void Count(int depth)
        {
            _token.ThrowIfCancellationRequested();
            if (++_elements > _limits.MaximumElements || depth >= _limits.MaximumDepth)
            {
                throw new InvalidDataException("SVG exceeds element/reference nesting limits.");
            }
        }

        private SvgNode Node(XElement e, Dictionary<string, string> inherited, int depth, bool definition = false)
        {
            Count(depth);
            if (!_active.Add(e))
            {
                throw new InvalidDataException("Cyclic SVG reference.");
            }

            try
            {
                return BuildNode(e, inherited, depth, definition);
            }
            finally
            {
                _active.Remove(e);
            }
        }

        private SvgNode BuildNode(XElement e, Dictionary<string, string> inherited, int depth, bool definition)
        {
            if (!IsSvgElement(e))
            {
                return Empty;
            }

            Dictionary<string, string> style = Style(e, inherited);
            if (Value(style, "display", "inline") == "none")
            {
                return Empty;
            }

            string name = e.Name.LocalName;
            if (name == "svg" && e != _root)
            {
                Warn("Nested SVG viewport");
            }

            if (!definition && Definitions.Contains(name))
            {
                return Empty;
            }

            Matrix3x2 transform = SvgNumbers.Transform((string?)e.Attribute("transform"));
            var children = new List<SvgNode>();
            VectorPath? path = Shape(e);
            PixelBuffer? image = name == "image" ? Image(Href(e)) : null;
            Populate(e, name, style, path, children, depth);
            if (path is not null)
            {
                path = ValidatePath(path, style);
            }

            SvgClip? clip = Clip(Value(style, "clip-path", "none"), depth);
            return new SvgNode(path, Paint(Value(style, "fill", "black"), style), Paint(Value(style, "stroke", "none"), style), Pen(style), Rule(Value(style, "fill-rule", "nonzero")), Rule(Value(style, "clip-rule", "nonzero")), transform, Opacity(style, "opacity"), Opacity(style, "fill-opacity"), Opacity(style, "stroke-opacity"), clip, children.ToArray())
            {
                Mask = Mask(Value(style, "mask", "none"), depth),
                Filter = Filter(Value(style, "filter", "none"), style, depth),
                BlendMode = Blend(Value(style, "mix-blend-mode", "normal")),
                Image = image,
                ImageTransform = image is null ? Matrix3x2.Identity : ImagePlacement(e, image),
                ImageViewport = image is null ? null : new VectorPath().Rectangle(Length(e, "x", 0, _width), Length(e, "y", 0, _height), Length(e, "width", image.Width, _width), Length(e, "height", image.Height, _height))
            };
        }

        private void Populate(XElement e, string name, Dictionary<string, string> style, VectorPath? path, List<SvgNode> children, int depth)
        {
            if (name == "use")
            {
                Use(e, style, children, depth);
            }
            else if (name == "text")
            {
                children.AddRange(TextNodes(e, style, depth));
            }
            else if (Containers.Contains(name))
            {
                foreach (XElement child in e.Elements())
                {
                    children.Add(Node(child, style, depth + 1));
                }
            }
            else if (path is null && name != "image" && !Metadata.Contains(name))
            {
                Warn("SVG element: " + name);
            }
        }

        private VectorPath? ValidatePath(VectorPath path, Dictionary<string, string> style)
        {
            _commands = checked(_commands + path.CommandCount);
            if (_commands > _limits.MaximumPathCommands)
            {
                throw new InvalidDataException("SVG exceeds MaximumPathCommands.");
            }

            return Value(style, "visibility", "visible") is "hidden" or "collapse" ? null : path;
        }

        private static SvgNode Empty => new(null, null, null, new StrokeStyle(), FillRule.NonZero, FillRule.NonZero, Matrix3x2.Identity, 1, 1, 1, null, Array.Empty<SvgNode>());

        private void Use(XElement e, Dictionary<string, string> style, List<SvgNode> children, int depth)
        {
            XElement? target = Reference(Href(e));
            if (target is null)
            {
                return;
            }

            SvgNode node = Node(target, style, depth + 1, true);
            Matrix3x2 placement = Matrix3x2.CreateTranslation(Length(e, "x", 0, _width), Length(e, "y", 0, _height));
            if (target.Name.LocalName == "symbol" && target.Attribute("viewBox") is { } box)
            {
                float[] v = SvgNumbers.List(box.Value);
                if (v.Length != 4 || v[2] <= 0 || v[3] <= 0)
                {
                    throw new InvalidDataException("Invalid symbol viewBox.");
                }

                placement = SvgRenderer.Viewport((v[0], v[1], v[2], v[3]), Length(e, "width", _width, _width), Length(e, "height", _height, _height), (string?)target.Attribute("preserveAspectRatio") ?? "xMidYMid meet") * placement;
            }

            children.Add(node with { Transform = node.Transform * placement });
        }

        private SvgClip? Clip(string value, int depth)
        {
            if (value == "none")
            {
                return null;
            }

            XElement? target = Reference(Url(value));
            if (target is null)
            {
                return null;
            }

            if (target.Name.LocalName != "clipPath")
            {
                Warn("Non-clipPath clipping reference");
                return null;
            }

            SvgNode node = Node(target, new Dictionary<string, string>(), depth + 1, true);
            var shapes = new List<SvgClipShape>();
            try
            {
                node.ClipShapes(node.Transform, shapes);
            }
            catch (NotSupportedException ex)
            {
                Warn(ex.Message);
            }

            return new SvgClip(shapes.ToArray(), (string?)target.Attribute("clipPathUnits") == "objectBoundingBox");
        }

        private XElement? Reference(string? href)
        {
            if (href is null)
            {
                Warn("Missing SVG reference");
                return null;
            }

            if (!href.StartsWith('#'))
            {
                Warn("External SVG resource");
                return null;
            }

            if (_ids.TryGetValue(href[1..], out XElement? e))
            {
                return e;
            }

            Warn("Missing SVG reference " + href);
            return null;
        }

        private static string? Href(XElement e) => (string?)e.Attribute("href") ?? (string?)e.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"));
        private static string? Url(string value) => value.StartsWith("url(", StringComparison.Ordinal) && value.EndsWith(')') ? value[4..^1].Trim().Trim('\'', '"') : null;
        private void Warn(string text) => _warnings.Add(text);
        private static FillRule Rule(string value) => value == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;
        private static string Value(Dictionary<string, string> values, string name, string fallback) => values.GetValueOrDefault(name, fallback);
        private static float Opacity(Dictionary<string, string> values, string name) => Math.Clamp(SvgLength.Parse(Value(values, name, "1")), 0, 1);
        private static float Length(XElement e, string name, float fallback, float percent) => e.Attribute(name) is { } a ? SvgLength.Parse(a.Value, percent) : fallback;
    }
}
