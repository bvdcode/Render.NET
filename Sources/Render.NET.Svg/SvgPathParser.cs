using System;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Render.NET.Svg
{
    /// <summary>Parses SVG path data, including elliptical arcs and smooth/relative commands.</summary>
    public static class SvgPathParser
    {
        /// <summary>Creates a mutable vector path from an SVG d attribute.</summary>
        public static VectorPath Parse(string data, int maximumCommands = 1_000_000, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCommands);
            return new Parser(data, maximumCommands, cancellationToken).Run();
        }

        private class Parser
        {
            private readonly SvgNumbers _numbers;
            private readonly VectorPath _path = new();
            private readonly int _maximum;
            private readonly CancellationToken _token;
            private Vector2 _point;
            private Vector2 _start;
            private Vector2 _control;
            private char _previous;
            internal Parser(string data, int maximum, CancellationToken token)
            {
                _numbers = new SvgNumbers(data);
                _maximum = maximum;
                _token = token;
            }

            internal VectorPath Run()
            {
                char command = '\0';
                while (!_numbers.End)
                {
                    _token.ThrowIfCancellationRequested();
                    if (char.IsAsciiLetter(_numbers.Peek))
                    {
                        command = _numbers.Take();
                    }

                    if (_path.CommandCount == 0 && char.ToUpperInvariant(command) != 'M')
                    {
                        throw new InvalidDataException("An SVG path must start with moveto.");
                    }

                    char upper = char.ToUpperInvariant(command);
                    bool relative = char.IsLower(command);
                    Execute(upper, relative);
                    _previous = upper;
                    if (_path.CommandCount > _maximum)
                    {
                        throw new InvalidDataException("SVG path exceeds MaximumPathCommands.");
                    }

                    command = upper == 'M' ? (relative ? 'l' : 'L') : upper == 'Z' ? '\0' : command;
                }

                return _path;
            }

            private Vector2 Point(bool relative) => new Vector2(_numbers.Read(), _numbers.Read()) + (relative ? _point : Vector2.Zero);
            private void Execute(char command, bool relative)
            {
                switch (command)
                {
                    case 'M':
                        _point = Point(relative);
                        _start = _point;
                        _path.MoveTo(_point.X, _point.Y);
                        break;
                    case 'L':
                        Line(Point(relative));
                        break;
                    case 'H':
                        Line(new Vector2(_numbers.Read() + (relative ? _point.X : 0), _point.Y));
                        break;
                    case 'V':
                        Line(new Vector2(_point.X, _numbers.Read() + (relative ? _point.Y : 0)));
                        break;
                    case 'Z':
                        _path.Close();
                        _point = _start;
                        break;
                    default:
                        Curve(command, relative);
                        break;
                }
            }

            private void Line(Vector2 end)
            {
                _path.LineTo(end.X, end.Y);
                _point = end;
            }

            private Vector2 Reflect(char a, char b) => _previous == a || _previous == b ? (2 * _point) - _control : _point;
            private void Curve(char command, bool relative)
            {
                Vector2 a;
                Vector2 b;
                Vector2 end;
                switch (command)
                {
                    case 'C':
                    case 'S':
                        a = command == 'C' ? Point(relative) : Reflect('C', 'S');
                        b = Point(relative);
                        end = Point(relative);
                        _path.CubicTo(a.X, a.Y, b.X, b.Y, end.X, end.Y);
                        _control = b;
                        _point = end;
                        break;
                    case 'Q':
                    case 'T':
                        a = command == 'Q' ? Point(relative) : Reflect('Q', 'T');
                        end = Point(relative);
                        _path.QuadraticTo(a.X, a.Y, end.X, end.Y);
                        _control = a;
                        _point = end;
                        break;
                    case 'A':
                        float rx = _numbers.Read();
                        float ry = _numbers.Read();
                        float angle = _numbers.Read();
                        bool large = _numbers.Flag();
                        bool sweep = _numbers.Flag();
                        end = Point(relative);
                        SvgArc.Append(_path, _point, end, rx, ry, angle, large, sweep);
                        _point = end;
                        break;
                    default:
                        throw new InvalidDataException("Unknown SVG path command: " + command);
                }
            }
        }
    }
}
