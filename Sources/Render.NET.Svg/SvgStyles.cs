using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal sealed partial class SvgSceneBuilder
    {
        private Dictionary<string, string> Style(XElement e, Dictionary<string, string> parent)
        {
            var result = parent.Where(p => Inherited.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            foreach (XAttribute a in e.Attributes())
            {
                if (Supported.Contains(a.Name.LocalName))
                {
                    Set(result, parent, a.Name.LocalName, a.Value);
                }
                else if (a.Name.LocalName is "filter" or "mask" or "marker-start" or "marker-mid" or "marker-end" or "vector-effect" or "paint-order")
                {
                    Warn("SVG property: " + a.Name.LocalName);
                }
            }

            foreach (var rule in _rules.OrderBy(r => r.Specificity))
            {
                if (Matches(e, rule.Selector))
                {
                    foreach (var p in rule.Values)
                    {
                        Set(result, parent, p.Key, p.Value);
                    }
                }
            }

            foreach (var p in Declarations((string?)e.Attribute("style") ?? ""))
            {
                Set(result, parent, p.Key, p.Value);
            }

            return result;
        }

        private void Set(Dictionary<string, string> target, Dictionary<string, string> parent, string key, string value)
        {
            if (!Supported.Contains(key))
            {
                Warn("SVG property: " + key);
                return;
            }

            if (value == "inherit")
            {
                if (parent.TryGetValue(key, out string? inherited))
                {
                    target[key] = inherited;
                }
                else
                {
                    target.Remove(key);
                }
            }
            else
            {
                target[key] = value;
            }
        }

        private static Dictionary<string, string> Declarations(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (string declaration in text.Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon > 0)
                {
                    result[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
                }
            }

            return result;
        }

        private void StyleSheet(string text)
        {
            while (text.Contains("/*", StringComparison.Ordinal))
            {
                int start = text.IndexOf("/*", StringComparison.Ordinal), end = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    Warn("Invalid SVG stylesheet comment");
                    return;
                }

                text = text.Remove(start, (end + 2) - start);
            }

            int pos = 0;
            while (pos < text.Length)
            {
                int start = text.IndexOf('{', pos), end = start < 0 ? -1 : text.IndexOf('}', start);
                if (end < 0)
                {
                    if (!string.IsNullOrWhiteSpace(text[pos..]))
                    {
                        Warn("SVG stylesheet syntax");
                    }

                    break;
                }

                foreach (string raw in text[pos..start].Split(','))
                {
                    string selector = raw.Trim();
                    if (selector.Length == 0 || selector.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '#' and not '-' and not '_' and not '*'))
                    {
                        Warn("SVG CSS selector: " + selector);
                        continue;
                    }

                    int specificity = (selector.Count(c => c == '#') * 100) + (selector.Count(c => c == '.') * 10) + (char.IsAsciiLetter(selector[0]) ? 1 : 0);
                    _rules.Add((selector, specificity, Declarations(text[(start + 1)..end])));
                }

                pos = end + 1;
            }
        }

        private static bool Matches(XElement e, string selector)
        {
            int position = 0;
            while (position < selector.Length)
            {
                char prefix = selector[position];
                int start = prefix is '.' or '#' ? ++position : position;
                while (position < selector.Length && selector[position] is not '.' and not '#')
                {
                    position++;
                }

                string name = selector[start..position];
                bool match = prefix switch
                {
                    '.' => ((string?)e.Attribute("class") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(name),
                    '#' => (string?)e.Attribute("id") == name,
                    _ => name == "*" || e.Name.LocalName == name
                };
                if (!match)
                {
                    return false;
                }
            }

            return true;
        }

        private StrokeStyle Pen(Dictionary<string, string> style)
        {
            float[] dash = Value(style, "stroke-dasharray", "none") is string d && d != "none" ? SvgNumbers.List(d) : Array.Empty<float>();
            if (dash.Any(x => x < 0))
            {
                throw new InvalidDataException("Negative SVG dash length.");
            }

            if (dash.Length % 2 != 0)
            {
                dash = dash.Concat(dash).ToArray();
            }

            if (dash.Length > 0 && dash.All(x => x == 0))
            {
                dash = Array.Empty<float>();
            }

            return new StrokeStyle
            {
                Width = Math.Max(0, SvgLength.Parse(Value(style, "stroke-width", "1"), MathF.Sqrt(((_width * _width) + (_height * _height)) / 2))),
                Cap = Value(style, "stroke-linecap", "butt") switch
                {
                    "round" => StrokeCap.Round,
                    "square" => StrokeCap.Square,
                    _ => StrokeCap.Butt
                },
                Join = Value(style, "stroke-linejoin", "miter") switch
                {
                    "round" => StrokeJoin.Round,
                    "bevel" => StrokeJoin.Bevel,
                    _ => StrokeJoin.Miter
                },
                MiterLimit = Math.Max(1, SvgLength.Parse(Value(style, "stroke-miterlimit", "4"))),
                Dashes = dash,
                DashOffset = SvgLength.Parse(Value(style, "stroke-dashoffset", "0"))
            };
        }
    }
}
