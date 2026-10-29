using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using Render.NET.Svg;

namespace Render.NET.Tgs
{
    internal partial class TgsSceneBuilder
    {
        private readonly TgsDocument _document;
        private readonly CancellationToken _token;
        private readonly Dictionary<string, JsonElement> _assets = new(StringComparer.Ordinal);
        private readonly HashSet<string> _activeAssets = new();
        private readonly HashSet<string> _warnings = new();
        private readonly XElement _defs = new("defs");
        private readonly TgsExpressionBudget? _expressionBudget;
        private int _items;
        private int _commands;
        private int _id;
        internal IEnumerable<string> Warnings => _warnings;
        internal TgsSceneBuilder(TgsDocument document, CancellationToken token)
        {
            _document = document;
            _token = token;
            if (document.Limits.Expressions is not null)
            {
                _expressionBudget = new TgsExpressionBudget(document.Limits.Expressions, token);
            }
            foreach (JsonElement asset in TgsJson.Array(TgsJson.Get(document.Root, "assets")))
            {
                if (!_assets.TryAdd(TgsJson.Text(asset, "id"), asset))
                {
                    throw new InvalidDataException("Duplicate Lottie asset id.");
                }
            }
        }

        internal SvgDocument Build(double frame, bool allowPartial)
        {
            XElement? root = new XElement("svg", new XAttribute("width", _document.Width), new XAttribute("height", _document.Height), _defs);
            root.Add(Layers(TgsJson.Get(_document.Root, "layers"), frame, 0));
            if (!allowPartial && _warnings.Count > 0)
            {
                throw new NotSupportedException(string.Join("; ", _warnings.Order()));
            }

            return SvgReader.Read(root.ToString(SaveOptions.DisableFormatting), new SvgReaderOptions { MaximumInputBytes = _document.Limits.MaximumJsonBytes, MaximumElements = _document.Limits.MaximumItems, MaximumDepth = _document.Limits.MaximumDepth, MaximumPathCommands = _document.Limits.MaximumPathCommands }, _token);
        }

        private XElement Layers(JsonElement array, double frame, int depth, double width = 0, double height = 0)
        {
            Count(depth);
            JsonElement[] layers = TgsJson.Array(array);
            if (_expressionBudget is not null)
            {
                layers = new TgsExpressionScope(_document, layers, frame, width > 0 ? width : _document.Width, height > 0 ? height : _document.Height, _expressionBudget, _token).Resolve();
            }
            Dictionary<double, JsonElement> byIndex = IndexLayers(layers);

            XElement? group = new XElement("g");
            var matteLayers = new HashSet<int>();
            for (int i = 0; i < layers.Length; i++)
            {
                if (TgsJson.Number(layers[i], "tt") is >= 1 and <= 4)
                {
                    matteLayers.Add(MatteIndex(layers, i));
                }
            }

            for (int i = layers.Length - 1; i >= 0; i--)
            {
                JsonElement layer = layers[i];
                Count(depth);
                if (matteLayers.Contains(i) || TgsJson.Flag(layer, "hd") || frame < TgsJson.Number(layer, "ip", _document.InPoint) || frame >= TgsJson.Number(layer, "op", _document.OutPoint))
                {
                    continue;
                }

                group.Add(PaintLayer(layers, i, byIndex, frame, depth, new HashSet<int>()));
            }

            return group;
        }

        private static Dictionary<double, JsonElement> IndexLayers(JsonElement[] layers)
        {
            var byIndex = new Dictionary<double, JsonElement>();
            foreach (JsonElement layer in layers)
            {
                JsonElement index = TgsJson.Get(layer, "ind");
                if (index.ValueKind == JsonValueKind.Number && !byIndex.TryAdd(TgsJson.Number(index), layer))
                {
                    throw new InvalidDataException("Duplicate Lottie layer index.");
                }
            }

            return byIndex;
        }

        private XElement PaintLayer(JsonElement[] layers, int index, Dictionary<double, JsonElement> byIndex, double frame, int depth, HashSet<int> active, bool includeHidden = false)
        {
            Count(depth);
            if (!active.Add(index))
            {
                throw new InvalidDataException("Cyclic Lottie track matte.");
            }

            try
            {
                JsonElement layer = layers[index];
                if ((!includeHidden && TgsJson.Flag(layer, "hd")) || frame < TgsJson.Number(layer, "ip", _document.InPoint) || frame >= TgsJson.Number(layer, "op", _document.OutPoint))
                {
                    return new XElement("g");
                }

                Matrix3x2 matrix = LayerTransform(layer, frame, byIndex, new HashSet<double>());
                XElement content = new XElement("g", LayerContent(layer, frame, depth + 1));
                Mask(layer, content, frame);
                content = new XElement("g", content);
                Effects(layer, content, frame);
                content.SetAttributeValue("transform", Matrix(matrix));
                content.SetAttributeValue("opacity", F(Math.Clamp(TgsValues.Scalar(TgsJson.Get(TgsJson.Get(layer, "ks"), "o"), frame, 100) / 100, 0, 1)));
                XElement result = new("g", content);
                SetMatte(layer, result, layers, byIndex, frame, depth, active);
                int matte = (int)TgsJson.Number(layer, "tt");
                if (matte is >= 1 and <= 4)
                {
                    XElement drawing = PaintLayer(layers, MatteIndex(layers, index), byIndex, frame, depth + 1, active);
                    string mask = Matte(drawing, matte);
                    result.SetAttributeValue("mask", "url(#" + mask + ")");
                }

                string? blend = BlendMode((int)TgsJson.Number(layer, "bm"));
                if (blend is not null)
                {
                    result.SetAttributeValue("mix-blend-mode", blend);
                }

                return result;
            }
            finally
            {
                active.Remove(index);
            }
        }

        private static int MatteIndex(JsonElement[] layers, int index)
        {
            JsonElement target = TgsJson.Get(layers[index], "tp");
            if (target.ValueKind == JsonValueKind.Number)
            {
                double id = TgsJson.Number(target);
                int found = Array.FindIndex(layers, layer => TgsJson.Number(layer, "ind", double.NaN) == id);
                if (found >= 0)
                {
                    return found;
                }

                throw new InvalidDataException("Missing Lottie track matte layer.");
            }

            return index > 0 ? index - 1 : throw new InvalidDataException("Missing preceding Lottie track matte.");
        }

        private Matrix3x2 LayerTransform(JsonElement layer, double frame, Dictionary<double, JsonElement> siblings, HashSet<double> active)
        {
            JsonElement values = TgsJson.Get(layer, "ks");
            Matrix3x2 matrix = Transform(values, frame, TgsJson.Flag(layer, "ao") ? AutoOrientation(TgsJson.Get(values, "p"), frame) : 0);
            JsonElement parent = TgsJson.Get(layer, "parent");
            if (parent.ValueKind == JsonValueKind.Number)
            {
                double index = TgsJson.Number(parent);
                if (!active.Add(index) || active.Count > _document.Limits.MaximumDepth)
                {
                    throw new InvalidDataException("Cyclic/deep Lottie parent chain.");
                }

                if (!siblings.TryGetValue(index, out JsonElement ancestor))
                {
                    throw new InvalidDataException("Missing Lottie parent.");
                }

                matrix *= LayerTransform(ancestor, frame, siblings, active);
            }

            return matrix;
        }

        private XElement LayerContent(JsonElement layer, double frame, int depth)
        {
            int type = (int)TgsJson.Number(layer, "ty", -1);
            if (type == 4)
            {
                return Shapes(TgsJson.Get(layer, "shapes"), frame, depth);
            }

            if (type == 1)
            {
                return new XElement("g", new XElement("rect", new XAttribute("width", F(TgsJson.Number(layer, "sw"))), new XAttribute("height", F(TgsJson.Number(layer, "sh"))), new XAttribute("fill", TgsJson.Text(layer, "sc", "black"))));
            }

            if (type == 0)
            {
                return Precomposition(layer, frame, depth);
            }

            return new XElement("g");
        }

        private XElement Precomposition(JsonElement layer, double frame, int depth)
        {
            string id = TgsJson.Text(layer, "refId");
            if (!_assets.TryGetValue(id, out JsonElement asset))
            {
                throw new InvalidDataException("Missing Lottie precomposition: " + id);
            }

            if (!_activeAssets.Add(id))
            {
                throw new InvalidDataException("Cyclic Lottie precomposition.");
            }

            try
            {
                double stretch = TgsJson.Number(layer, "sr", 1);
                if (stretch == 0)
                {
                    throw new InvalidDataException("Zero Lottie time stretch.");
                }

                JsonElement remap = TgsJson.Get(layer, "tm");
                double time = remap.ValueKind == JsonValueKind.Object ? TgsValues.Scalar(remap, frame) * _document.FrameRate : (frame - TgsJson.Number(layer, "st")) / stretch;
                double width = TgsJson.Number(layer, "w", TgsJson.Number(asset, "w", _document.Width));
                double height = TgsJson.Number(layer, "h", TgsJson.Number(asset, "h", _document.Height));
                XElement group = Layers(TgsJson.Get(asset, "layers"), time, depth, width, height);
                string clip = Id();
                _defs.Add(new XElement("clipPath", new XAttribute("id", clip), new XElement("rect", new XAttribute("width", F(width)), new XAttribute("height", F(height)))));
                return new XElement("g", new XAttribute("clip-path", "url(#" + clip + ")"), group);
            }
            finally
            {
                _activeAssets.Remove(id);
            }
        }

        private static double AutoOrientation(JsonElement position, double frame)
        {
            IEnumerable<double> Times(JsonElement property) => TgsJson.Array(TgsJson.Get(property, "k")).Where(k => TgsJson.Get(k, "t").ValueKind == JsonValueKind.Number).Select(k => TgsJson.Number(k, "t"));
            double[] times = (TgsJson.Flag(position, "s") ? Times(TgsJson.Get(position, "x")).Concat(Times(TgsJson.Get(position, "y"))) : Times(position)).ToArray();
            if (times.Length > 0)
            {
                frame = Math.Clamp(frame, times.Min(), times.Max());
            }

            Vector2 delta = TgsValues.Point(position, frame + .01) - TgsValues.Point(position, frame - .01);
            return delta.LengthSquared() < 1e-12 ? 0 : (Math.Atan2(delta.Y, delta.X) * 180) / Math.PI;
        }

        private static Matrix3x2 Transform(JsonElement value, double frame, double orientation = 0) => TgsTransforms.Transform(value, frame, orientation);

        private void Count(int depth)
        {
            _token.ThrowIfCancellationRequested();
            if (++_items > _document.Limits.MaximumItems || depth >= _document.Limits.MaximumDepth)
            {
                throw new InvalidDataException("Lottie expanded scene exceeds limits.");
            }
        }

        private void Geometry(VectorPath path)
        {
            _commands = checked(_commands + path.CommandCount);
            if (_commands > _document.Limits.MaximumPathCommands)
            {
                throw new InvalidDataException("Lottie frame exceeds MaximumPathCommands.");
            }
        }

        private string Id() => "tgs" + (++_id).ToString(CultureInfo.InvariantCulture);
        private static string F(double value)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue)
            {
                throw new InvalidDataException("Invalid Lottie coordinate.");
            }

            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static string Matrix(Matrix3x2 m) => $"matrix({F(m.M11)} {F(m.M12)} {F(m.M21)} {F(m.M22)} {F(m.M31)} {F(m.M32)})";
        private static XElement PathElement(VectorPath path) => new XElement("path", new XAttribute("d", SvgPathWriter.Write(path)));
    }
}
