using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    internal partial class SvgSceneBuilder
    {
        private Dictionary<string, string> Style(XElement e, Dictionary<string, string> parent)
        {
            var result = parent.Where(p => Inherited.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            Presentation(e, parent, result);
            foreach ((string Selector, int Specificity, Dictionary<string, string> Values) rule in _rules.OrderBy(r => r.Specificity))
            {
                if (Matches(e, rule.Selector))
                {
                    foreach (KeyValuePair<string, string> p in rule.Values)
                    {
                        Set(result, parent, p.Key, p.Value);
                    }
                }
            }

            foreach (KeyValuePair<string, string> p in Declarations((string?)e.Attribute("style") ?? ""))
            {
                Set(result, parent, p.Key, p.Value);
            }

            if (result.TryGetValue("font-size", out string? size))
            {
                float parentSize = TextLength(Value(parent, "font-size", "16"), 16);
                result["font-size"] = TextLength(size, parentSize).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return result;
        }

        private void Presentation(XElement e, Dictionary<string, string> parent, Dictionary<string, string> result)
        {
            foreach (XAttribute a in e.Attributes().Where(a => a.Name.NamespaceName.Length == 0))
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
        }

        private void Set(Dictionary<string, string> target, Dictionary<string, string> parent, string key, string value)
        {
            if (key == "font")
            {
                SetFont(target, value);
                return;
            }

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

        private void SetFont(Dictionary<string, string> target, string value)
        {
            System.Text.RegularExpressions.Match? match = System.Text.RegularExpressions.Regex.Match(value, @"^\s*(?<prefix>.*?)(?<size>(?:\d*\.)?\d+(?:px|pt|pc|mm|cm|in|em|ex|rem|%))(?:\s*/\s*[^\s]+)?\s+(?<family>.+)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                Warn("SVG font shorthand: " + value);
                return;
            }

            target["font-size"] = match.Groups["size"].Value;
            target["font-family"] = match.Groups["family"].Value;
            target["font-style"] = "normal";
            target["font-weight"] = "400";
            foreach (string part in match.Groups["prefix"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part is "italic" or "oblique")
                {
                    target["font-style"] = part;
                }
                else if (part is "bold" or "bolder" or "lighter" || int.TryParse(part, out _))
                {
                    target["font-weight"] = part;
                }
                else if (part != "normal")
                {
                    Warn("SVG font variant: " + part);
                }
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
            string? stripped = StripComments(text);
            if (stripped is null)
            {
                return;
            }

            text = stripped;
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
                    if (!ValidSelector(selector))
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

        private string? StripComments(string text)
        {
            while (text.Contains("/*", StringComparison.Ordinal))
            {
                int start = text.IndexOf("/*", StringComparison.Ordinal), end = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    Warn("Invalid SVG stylesheet comment");
                    return null;
                }

                text = text.Remove(start, (end + 2) - start);
            }

            return text;
        }

        private static bool ValidSelector(string selector) => selector.Length != 0 && selector.All(c => char.IsAsciiLetterOrDigit(c) || ".#-_*".Contains(c));
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
                if (!SelectorPartMatches(e, prefix, name))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SelectorPartMatches(XElement e, char prefix, string name) => prefix switch
        {
            '.' => ((string?)e.Attribute("class") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(name),
            '#' => (string?)e.Attribute("id") == name,
            _ => name == "*" || e.Name.LocalName == name
        };
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
