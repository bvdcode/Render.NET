using System;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace Render.NET.Svg
{
    internal sealed class SvgNumbers
    {
        private readonly string _text;
        private int _position;
        internal SvgNumbers(string text) => _text = text;
        internal bool End
        {
            get
            {
                Skip();
                return _position == _text.Length;
            }
        }

        internal char Peek
        {
            get
            {
                Skip();
                return _position < _text.Length ? _text[_position] : '\0';
            }
        }

        internal char Take()
        {
            Skip();
            return _position < _text.Length ? _text[_position++] : '\0';
        }

        internal void Skip()
        {
            while (_position < _text.Length && (char.IsWhiteSpace(_text[_position]) || _text[_position] == ','))
            {
                _position++;
            }
        }

        internal float Read()
        {
            Skip();
            int start = _position;
            Sign();
            Digits();
            if (_position < _text.Length && _text[_position] == '.')
            {
                _position++;
                Digits();
            }

            if (_position < _text.Length && (_text[_position] == 'e' || _text[_position] == 'E'))
            {
                _position++;
                Sign();
                Digits();
            }

            if (start == _position || !float.TryParse(_text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            {
                throw new InvalidDataException("Invalid SVG number at offset " + start);
            }

            return value;
        }

        internal bool Flag()
        {
            char c = Take();
            return c switch
            {
                '0' => false,
                '1' => true,
                _ => throw new InvalidDataException("An SVG arc flag must be 0 or 1.")};
        }

        private void Sign()
        {
            if (_position < _text.Length && (_text[_position] == '+' || _text[_position] == '-'))
            {
                _position++;
            }
        }

        private void Digits()
        {
            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                _position++;
            }
        }

        internal static float[] List(string text)
        {
            var reader = new SvgNumbers(text);
            var result = new System.Collections.Generic.List<float>();
            while (!reader.End)
            {
                result.Add(reader.Read());
            }

            return result.ToArray();
        }

        internal static Matrix3x2 Transform(string? text)
        {
            Matrix3x2 matrix = Matrix3x2.Identity;
            if (string.IsNullOrWhiteSpace(text))
            {
                return matrix;
            }

            int pos = 0;
            while (pos < text.Length)
            {
                while (pos < text.Length && (char.IsWhiteSpace(text[pos]) || text[pos] == ','))
                {
                    pos++;
                }

                if (pos == text.Length)
                {
                    break;
                }

                int open = text.IndexOf('(', pos);
                int close = open < 0 ? -1 : text.IndexOf(')', open);
                if (close < 0)
                {
                    throw new InvalidDataException("Invalid SVG transform.");
                }

                string name = text[pos..open].Trim();
                float[] values = List(text[(open + 1)..close]);
                matrix = Operation(name, values) * matrix;
                pos = close + 1;
            }

            return matrix;
        }

        private static Matrix3x2 Operation(string name, float[] v) => (name, v.Length) switch
        {
            ("matrix", 6) => new Matrix3x2(v[0], v[1], v[2], v[3], v[4], v[5]),
            ("translate", 1) => Matrix3x2.CreateTranslation(v[0], 0),
            ("translate", 2) => Matrix3x2.CreateTranslation(v[0], v[1]),
            ("scale", 1) => Matrix3x2.CreateScale(v[0]),
            ("scale", 2) => Matrix3x2.CreateScale(v[0], v[1]),
            ("rotate", 1) => Matrix3x2.CreateRotation((v[0] * MathF.PI) / 180),
            ("rotate", 3) => Matrix3x2.CreateRotation((v[0] * MathF.PI) / 180, new Vector2(v[1], v[2])),
            ("skewX", 1) => new Matrix3x2(1, 0, MathF.Tan((v[0] * MathF.PI) / 180), 1, 0, 0),
            ("skewY", 1) => new Matrix3x2(1, MathF.Tan((v[0] * MathF.PI) / 180), 0, 1, 0, 0),
            _ => throw new InvalidDataException("Invalid SVG transform: " + name)};
    }
}
