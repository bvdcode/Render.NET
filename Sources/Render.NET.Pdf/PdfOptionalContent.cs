using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal class PdfOptionalContent
    {
        private readonly IPdfTokenScanner _scanner;
        private readonly Dictionary<DictionaryToken, bool> _states = new();
        private readonly bool _baseState = true;

        internal PdfOptionalContent(DictionaryToken catalog, IPdfTokenScanner scanner)
        {
            _scanner = scanner;
            if (!Get(catalog, "OCProperties", out DictionaryToken properties)
                || !Get(properties, "D", out DictionaryToken configuration))
            {
                return;
            }

            _baseState = !Get(configuration, "BaseState", out NameToken state) || state.Data != "OFF";
            SetStates(configuration, "ON", true);
            SetStates(configuration, "OFF", false);
            ApplyViewUsage(properties, configuration);
        }

        internal bool IsVisible(IToken token, int depth = 0)
        {
            if (depth >= 64 || !DirectObjectFinder.TryGet(token, _scanner, out DictionaryToken? dictionary))
            {
                throw new InvalidDataException("Invalid or cyclic PDF optional content.");
            }

            if (Get(dictionary, "Type", out NameToken type) && type.Data == "OCG")
            {
                return _states.TryGetValue(dictionary, out bool visible) ? visible : _baseState;
            }

            if (Get(dictionary, "VE", out ArrayToken expression))
            {
                return Expression(expression, depth + 1);
            }

            if (!dictionary.TryGet(NameToken.Create("OCGs"), out IToken groups))
            {
                return true;
            }

            IToken[] members = DirectObjectFinder.TryGet(groups, _scanner, out ArrayToken? array) ? array.Data.ToArray() : new[] { groups };
            bool[] values = members.Select(member => IsVisible(member, depth + 1)).ToArray();
            string policy = Get(dictionary, "P", out NameToken name) ? name.Data : "AnyOn";
            return policy switch
            {
                "AllOn" => values.All(value => value),
                "AnyOn" => values.Any(value => value),
                "AnyOff" => values.Any(value => !value),
                "AllOff" => values.All(value => !value),
                _ => throw new InvalidDataException("Invalid PDF optional content policy: " + policy)
            };
        }

        private bool Expression(ArrayToken expression, int depth)
        {
            if (depth >= 64 || expression.Data.Count < 2 || expression.Data[0] is not NameToken operation)
            {
                throw new InvalidDataException("Invalid PDF optional content visibility expression.");
            }

            bool[] values = expression.Data.Skip(1).Select(item =>
                DirectObjectFinder.TryGet(item, _scanner, out ArrayToken? nested)
                    ? Expression(nested, depth + 1) : IsVisible(item, depth + 1)).ToArray();
            return operation.Data switch
            {
                "And" => values.All(value => value),
                "Or" => values.Any(value => value),
                "Not" when values.Length == 1 => !values[0],
                _ => throw new InvalidDataException("Invalid PDF optional content visibility operator.")
            };
        }

        private void SetStates(DictionaryToken configuration, string name, bool visible)
        {
            if (!Get(configuration, name, out ArrayToken groups))
            {
                return;
            }

            foreach (IToken item in groups.Data)
            {
                if (!DirectObjectFinder.TryGet(item, _scanner, out DictionaryToken? group))
                {
                    throw new InvalidDataException("Invalid PDF optional content group.");
                }

                _states[group] = visible;
            }
        }

        private void ApplyViewUsage(DictionaryToken properties, DictionaryToken configuration)
        {
            if (!Get(configuration, "AS", out ArrayToken applications))
            {
                return;
            }

            foreach (IToken item in applications.Data)
            {
                if (!DirectObjectFinder.TryGet(item, _scanner, out DictionaryToken? application)
                    || !Get(application, "Event", out NameToken purpose) || purpose.Data != "View"
                    || !Get(application, "Category", out ArrayToken categories)
                    || !categories.Data.OfType<NameToken>().Any(category => category.Data == "View"))
                {
                    continue;
                }

                if (!Get(application, "OCGs", out ArrayToken groups) && !Get(properties, "OCGs", out groups))
                {
                    continue;
                }

                ApplyViewStates(groups);
            }
        }

        private void ApplyViewStates(ArrayToken groups)
        {
            foreach (IToken member in groups.Data)
            {
                if (DirectObjectFinder.TryGet(member, _scanner, out DictionaryToken? group)
                    && Get(group, "Usage", out DictionaryToken usage)
                    && Get(usage, "View", out DictionaryToken view)
                    && Get(view, "ViewState", out NameToken state))
                {
                    _states[group] = state.Data switch
                    {
                        "ON" => true,
                        "OFF" => false,
                        _ => throw new InvalidDataException("Invalid PDF optional content view state.")
                    };
                }
            }
        }

        private bool Get<T>(DictionaryToken dictionary, string name, out T value) where T : class, IToken
        {
            value = null!;
            if (dictionary.TryGet(NameToken.Create(name), out IToken token)
                && DirectObjectFinder.TryGet(token, _scanner, out T? resolved))
            {
                value = resolved;
                return true;
            }

            return false;
        }
    }
}
