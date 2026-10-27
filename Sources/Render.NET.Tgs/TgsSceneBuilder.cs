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
    internal sealed partial class TgsSceneBuilder
    {
        private readonly TgsDocument _document;
        private readonly CancellationToken _token;
        private readonly Dictionary<string, JsonElement> _assets = new(StringComparer.Ordinal);
        private readonly HashSet<string> _activeAssets = new();
        private readonly HashSet<string> _warnings = new();
        private readonly XElement _defs = new("defs");
        private int _items;
        private int _commands;
        private int _id;
        internal TgsSceneBuilder(TgsDocument document, CancellationToken token)
        {
            _document = document;
            _token = token;
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
            var root = new XElement("svg", new XAttribute("width", _document.Width), new XAttribute("height", _document.Height), _defs);
            root.Add(Layers(TgsJson.Get(_document.Root, "layers"), frame, 0));
            if (!allowPartial && _warnings.Count > 0)
            {
                throw new NotSupportedException(string.Join("; ", _warnings.Order()));
            }

            return SvgReader.Read(root.ToString(SaveOptions.DisableFormatting), new SvgReaderOptions { MaximumInputBytes = _document.Limits.MaximumJsonBytes, MaximumElements = _document.Limits.MaximumItems, MaximumDepth = _document.Limits.MaximumDepth, MaximumPathCommands = _document.Limits.MaximumPathCommands }, _token);
        }

        private XElement Layers(JsonElement array, double frame, int depth)
        {
            Count(depth);
            JsonElement[] layers = TgsJson.Array(array);
            var byIndex = new Dictionary<double, JsonElement>();
            foreach (JsonElement layer in layers)
            {
                JsonElement index = TgsJson.Get(layer, "ind");
                if (index.ValueKind == JsonValueKind.Number && !byIndex.TryAdd(TgsJson.Number(index), layer))
                {
                    throw new InvalidDataException("Duplicate Lottie layer index.");
                }
            }

            var group = new XElement("g");
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                JsonElement layer = layers[i];
                Count(depth);
                if (TgsJson.Flag(layer, "hd") || frame < TgsJson.Number(layer, "ip", _document.InPoint) || frame >= TgsJson.Number(layer, "op", _document.OutPoint))
                {
                    continue;
                }

                Matrix3x2 matrix = LayerTransform(layer, frame, byIndex, new HashSet<double>());
                XElement content = new XElement("g", LayerContent(layer, frame, depth + 1));
                content.SetAttributeValue("transform", Matrix(matrix));
                content.SetAttributeValue("opacity", F(Math.Clamp(TgsValues.Scalar(TgsJson.Get(TgsJson.Get(layer, "ks"), "o"), frame, 100) / 100, 0, 1)));
                Mask(layer, content, frame);
                group.Add(content);
            }

            return group;
        }

        private Matrix3x2 LayerTransform(JsonElement layer, double frame, Dictionary<double, JsonElement> siblings, HashSet<double> active)
        {
            Matrix3x2 matrix = Transform(TgsJson.Get(layer, "ks"), frame);
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
                XElement group = Layers(TgsJson.Get(asset, "layers"), time, depth);
                double width = TgsJson.Number(layer, "w", TgsJson.Number(asset, "w", _document.Width));
                double height = TgsJson.Number(layer, "h", TgsJson.Number(asset, "h", _document.Height));
                string clip = Id();
                _defs.Add(new XElement("clipPath", new XAttribute("id", clip), new XElement("rect", new XAttribute("width", F(width)), new XAttribute("height", F(height)))));
                return new XElement("g", new XAttribute("clip-path", "url(#" + clip + ")"), group);
            }
            finally
            {
                _activeAssets.Remove(id);
            }
        }

        private void Mask(JsonElement layer, XElement content, double frame)
        {
            JsonElement[] masks = TgsJson.Array(TgsJson.Get(layer, "masksProperties"));
            if (masks.Length != 1 || TgsJson.Text(masks[0], "mode", "a") != "a" || TgsJson.Flag(masks[0], "inv"))
            {
                return;
            }

            VectorPath path = Bezier(TgsJson.Get(masks[0], "pt"), frame);
            Geometry(path);
            string id = Id();
            _defs.Add(new XElement("clipPath", new XAttribute("id", id), PathElement(path)));
            content.SetAttributeValue("clip-path", "url(#" + id + ")");
        }

        private static Matrix3x2 Transform(JsonElement value, double frame)
        {
            Vector2 anchor = TgsValues.Point(TgsJson.Get(value, "a"), frame), position = TgsValues.Point(TgsJson.Get(value, "p"), frame);
            Vector2 scale = TgsValues.Point(TgsJson.Get(value, "s"), frame, 100, 100) / 100;
            double rotation = TgsValues.Scalar(TgsJson.Get(value, "r"), frame);
            float skew = (float)((TgsValues.Scalar(TgsJson.Get(value, "sk"), frame) * Math.PI) / 180);
            float axis = (float)((TgsValues.Scalar(TgsJson.Get(value, "sa"), frame) * Math.PI) / 180);
            return Matrix3x2.CreateTranslation(-anchor) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(-axis) * new Matrix3x2(1, 0, -MathF.Tan(skew), 1, 0, 0) * Matrix3x2.CreateRotation(axis) * Matrix3x2.CreateRotation((float)((rotation * Math.PI) / 180)) * Matrix3x2.CreateTranslation(position);
        }

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
