using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using Jint;
using Jint.Constraints;
using Jint.Native;
using Jint.Runtime.Interop;

namespace Render.NET.Tgs
{
    internal partial class TgsExpressionScope
    {
        private readonly TgsDocument _document;
        private readonly JsonElement[] _layers;
        private readonly double _frame;
        private readonly TgsExpressionOptions _options;
        private readonly TgsExpressionBudget _budget;
        private readonly CancellationToken _token;
        private readonly Dictionary<JsonElement, int> _ids = new();
        private readonly List<(JsonElement Value, int Layer)> _properties = new();
        private readonly Dictionary<(int Id, double Frame), JsonElement> _cache = new();
        private readonly HashSet<(int Id, double Frame)> _active = new();
        private readonly string _metadata;

        internal TgsExpressionScope(TgsDocument document, JsonElement[] layers, double frame, double width, double height, TgsExpressionBudget budget, CancellationToken token)
        {
            _document = document;
            _layers = layers;
            _frame = frame;
            _options = document.Limits.Expressions!;
            _budget = budget;
            _token = token;
            for (int i = 0; i < layers.Length; i++)
            {
                Register(layers[i], i);
            }

            _metadata = Metadata(width, height);
        }

        internal JsonElement[] Resolve()
        {
            if (!_properties.Any(p => TgsJson.Get(p.Value, "x").ValueKind == JsonValueKind.String))
            {
                return _layers;
            }

            var needed = new HashSet<int>();
            for (int i = 0; i < _layers.Length; i++)
            {
                JsonElement layer = _layers[i];
                if (!TgsJson.Flag(layer, "hd") && _frame >= TgsJson.Number(layer, "ip", _document.InPoint) && _frame < TgsJson.Number(layer, "op", _document.OutPoint))
                {
                    RequireLayer(i, needed);
                }
            }

            return _layers.Select((layer, i) => needed.Contains(i) ? Rewrite(layer, _frame) : layer).ToArray();
        }

        private JsonElement Value(int id, double frame, bool original)
        {
            _budget.Check();
            if (id < 0 || id >= _properties.Count || !double.IsFinite(frame))
            {
                throw new InvalidDataException("Invalid expression property reference or time.");
            }

            JsonElement property = _properties[id].Value;
            if (TgsJson.Flag(property, "s") && TgsJson.Get(property, "x").ValueKind == JsonValueKind.Object)
            {
                double x = Value(Id(TgsJson.Get(property, "x")), frame, original).GetDouble();
                double y = Value(Id(TgsJson.Get(property, "y")), frame, original).GetDouble();
                return JsonSerializer.SerializeToElement(new[] { x, y });
            }

            if (original || TgsJson.Get(property, "x").ValueKind != JsonValueKind.String)
            {
                return TgsPropertySample.Read(property, frame);
            }

            if (_cache.TryGetValue((id, frame), out JsonElement cached))
            {
                return cached;
            }

            _budget.Enter(_active.Count + 1);
            if (!_active.Add((id, frame)))
            {
                throw new InvalidDataException("Cyclic Lottie expression property dependency.");
            }

            try
            {
                JsonElement result = Evaluate(id, frame);
                TgsPropertySample.Validate(result, TgsPropertySample.Read(property, frame), _document.Limits.MaximumItems);
                _cache.Add((id, frame), result);
                return result;
            }
            finally
            {
                _active.Remove((id, frame));
            }
        }

        private JsonElement Evaluate(int id, double frame)
        {
            (JsonElement property, int layer) = _properties[id];
            OperationDeadlineConstraint deadline = new();
            Engine engine = new(options => options.LimitMemory(_options.MaximumMemoryBytes).MaxStatements(_options.MaximumStatements).LimitRecursion(_options.MaximumRecursion).Constraint(deadline));
            engine.SetValue("sceneJson", _metadata);
            engine.SetValue("currentId", id);
            engine.SetValue("currentLayer", layer);
            engine.SetValue("currentFrame", frame);
            engine.SetValue("propertyJson", JsonSerializer.Serialize(new { keys = KeyTimes(property) }));
            engine.SetValue("readProperty", new ClrFunction(engine, "readProperty", (_, arguments) =>
            {
                int reference = checked((int)arguments[0].AsNumber());
                double time = arguments[1].AsNumber() * _document.FrameRate;
                return new JsString(Value(reference, time, arguments[2].AsBoolean()).GetRawText());
            }));
            engine.SetValue("mapPoint", new ClrFunction(engine, "mapPoint", (_, arguments) =>
            {
                int reference = checked((int)arguments[0].AsNumber());
                using JsonDocument point = JsonDocument.Parse(arguments[1].AsString());
                double[] coordinates = TgsValues.Numbers(point.RootElement);
                if (coordinates.Length < 2 || reference < 0 || reference >= _layers.Length)
                {
                    throw new InvalidDataException("Invalid expression layer point.");
                }

                Matrix3x2 matrix = LayerMatrix(reference, frame, new HashSet<int>());
                Vector2 mapped = Vector2.Transform(new Vector2((float)coordinates[0], (float)coordinates[1]), matrix);
                return new JsString(JsonSerializer.Serialize(new[] { (double)mapped.X, mapped.Y, 0 }));
            }));
            string source = TgsExpressionMath.Source + "\n" + TgsExpressionHost.Source + "\n" + TgsJson.Text(property, "x") + "\n;JSON.stringify($bm_rt);";
            string json;
            deadline.Begin(_options.EvaluationTimeout, _token);
            try
            {
                json = engine.Evaluate(source).AsString();
                engine.Constraints.Check();
            }
            finally
            {
                deadline.End();
            }
            if (json.Length > _document.Limits.MaximumJsonBytes)
            {
                throw new InvalidDataException("Expression result exceeds MaximumJsonBytes.");
            }

            _budget.Check();
            using JsonDocument result = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = _document.Limits.MaximumDepth });
            return result.RootElement.Clone();
        }

        private JsonElement Rewrite(JsonElement value, double frame)
        {
            using MemoryStream data = new();
            using (Utf8JsonWriter writer = new(data))
            {
                Write(writer, value, frame);
            }

            using JsonDocument json = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = _document.Limits.MaximumDepth });
            return json.RootElement.Clone();
        }

        private void Write(Utf8JsonWriter writer, JsonElement value, double frame)
        {
            _budget.Check();
            if (TgsEffectInfo.IsParameterGroup(value))
            {
                value.WriteTo(writer);
                return;
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                bool expression = TgsJson.Get(value, "x").ValueKind == JsonValueKind.String && _ids.ContainsKey(value);
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (expression && property.Name is "a" or "k" or "x")
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, frame);
                }

                if (expression)
                {
                    writer.WriteNumber("a", 0);
                    writer.WritePropertyName("k");
                    Value(_ids[value], frame, false).WriteTo(writer);
                }

                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Write(writer, item, frame);
                }

                writer.WriteEndArray();
            }
            else
            {
                value.WriteTo(writer);
            }
        }
    }
}
