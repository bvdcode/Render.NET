using Render.NET.Entropy;
using System;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class BlockDecoder
    {
        private readonly CodeBlock _block;
        private readonly int _orientation;
        private readonly int _style;
        private readonly DecodeBudget _budget;
        private readonly int[] _magnitude;
        private readonly byte[] _flags;
        private readonly byte[] _lowest;
        private readonly byte[] _states = new byte[19];
        private MqDecoder? _mq;
        private RawBits? _raw;
        private int _plane;
        internal BlockDecoder(CodeBlock block, int orientation, int style, DecodeBudget budget)
        {
            _block = block;
            _orientation = orientation;
            _style = style;
            _budget = budget;
            int length = block.Width * block.Height;
            budget.Reserve((long)length * 14);
            _magnitude = new int[length];
            _flags = new byte[length];
            _lowest = new byte[length];
            MqDecoder.Reset(_states);
        }

        internal double[] Decode(int planes, double step, bool reversible, int roi)
        {
            int first = (planes + roi) - _block.ZeroPlanes - 1;
            if (first > 29)
            {
                throw new NotSupportedException("JPEG 2000 coefficient precision above 30 bits.");
            }

            if (_block.Passes > Math.Max(0, (3 * first) + 1))
            {
                throw new InvalidDataException("Too many JPEG 2000 coding passes.");
            }

            foreach (CodewordSegment segment in _block.Segments)
            {
                byte[] data = segment.Join(_budget);
                _raw = segment.Raw ? new RawBits(data) : null;
                _mq = segment.Raw ? null : new MqDecoder(data, _states);
                for (int p = segment.FirstPass; p < segment.FirstPass + segment.Passes; p++)
                {
                    _budget.Cancellation.ThrowIfCancellationRequested();
                    _plane = first - ((p + 2) / 3);
                    switch ((p + 2) % 3)
                    {
                        case 0:
                            Significance();
                            break;
                        case 1:
                            Refine();
                            break;
                        case 2:
                            Cleanup();
                            break;
                    }

                    if ((_style & 2) != 0)
                    {
                        MqDecoder.Reset(_states);
                    }
                }
            }

            return Reconstruct(step, reversible, roi);
        }

        private int Bit(int context) => _raw is not null ? _raw.Read() : _mq!.Read(context);
        private byte Flag(int x, int y, int currentY)
        {
            if ((uint)x >= _block.Width || (uint)y >= _block.Height)
            {
                return 0;
            }

            if ((_style & 8) != 0 && currentY % 4 == 3 && y > currentY)
            {
                return 0;
            }

            return _flags[(y * _block.Width) + x];
        }

        private int Significant(int x, int y, int currentY) => Flag(x, y, currentY) & 1;
        private int Context(int x, int y)
        {
            int h = Significant(x - 1, y, y) + Significant(x + 1, y, y);
            int v = Significant(x, y - 1, y) + Significant(x, y + 1, y);
            int d = Significant(x - 1, y - 1, y) + Significant(x + 1, y - 1, y) + Significant(x - 1, y + 1, y) + Significant(x + 1, y + 1, y);
            if (_orientation == 3)
            {
                int total = h + v;
                return d >= 3 ? 8 : d == 2 ? (total == 0 ? 6 : 7) : d == 1 ? 3 + Math.Min(total, 2) : Math.Min(total, 2);
            }

            if (_orientation == 1)
            {
                (h, v) = (v, h);
            }

            if (h == 2)
            {
                return 8;
            }

            if (h == 1)
            {
                return v != 0 ? 7 : d != 0 ? 6 : 5;
            }

            return v == 2 ? 4 : v == 1 ? 3 : Math.Min(d, 2);
        }

        private int SignedNeighbor(int x, int y, int currentY)
        {
            byte flag = Flag(x, y, currentY);
            return (flag & 1) == 0 ? 0 : (flag & 8) == 0 ? 1 : -1;
        }

        private void MakeSignificant(int x, int y, int index)
        {
            int h = Math.Sign(SignedNeighbor(x - 1, y, y) + SignedNeighbor(x + 1, y, y));
            int v = Math.Sign(SignedNeighbor(x, y - 1, y) + SignedNeighbor(x, y + 1, y));
            int predict = h < 0 || (h == 0 && v < 0) ? 1 : 0;
            if (predict != 0)
            {
                h = -h;
                v = -v;
            }

            int context = h == 0 ? 9 + Math.Abs(v) : 12 + v;
            int sign = _raw is not null ? _raw.Read() : _mq!.Read(context) ^ predict;
            _magnitude[index] = 1 << _plane;
            _lowest[index] = (byte)_plane;
            _flags[index] |= (byte)(1 | (sign << 3));
        }

        private void Significance()
        {
            for (int stripe = 0; stripe < _block.Height; stripe += 4)
            {
                for (int x = 0; x < _block.Width; x++)
                {
                    for (int y = stripe; y < Math.Min(stripe + 4, _block.Height); y++)
                    {
                        int i = (y * _block.Width) + x;
                        if ((_flags[i] & 1) != 0)
                        {
                            continue;
                        }

                        int context = Context(x, y);
                        if (context == 0)
                        {
                            continue;
                        }

                        _flags[i] |= 2;
                        if (Bit(context) != 0)
                        {
                            MakeSignificant(x, y, i);
                        }
                    }
                }
            }
        }

        private void Refine()
        {
            for (int stripe = 0; stripe < _block.Height; stripe += 4)
            {
                for (int x = 0; x < _block.Width; x++)
                {
                    for (int y = stripe; y < Math.Min(stripe + 4, _block.Height); y++)
                    {
                        int i = (y * _block.Width) + x;
                        if ((_flags[i] & 3) != 1)
                        {
                            continue;
                        }

                        int context = (_flags[i] & 4) != 0 ? 16 : Context(x, y) == 0 ? 14 : 15;
                        _magnitude[i] |= Bit(context) << _plane;
                        _lowest[i] = (byte)_plane;
                        _flags[i] |= 4;
                    }
                }
            }
        }

        private bool RunEligible(int x, int stripe)
        {
            if (stripe + 4 > _block.Height)
            {
                return false;
            }

            for (int y = stripe; y < stripe + 4; y++)
            {
                if ((_flags[(y * _block.Width) + x] & 3) != 0 || Context(x, y) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private void Cleanup()
        {
            for (int stripe = 0; stripe < _block.Height; stripe += 4)
            {
                for (int x = 0; x < _block.Width; x++)
                {
                    int start = stripe;
                    if (RunEligible(x, stripe))
                    {
                        if (Bit(17) == 0)
                        {
                            continue;
                        }

                        start += (Bit(18) << 1) | Bit(18);
                        MakeSignificant(x, start, (start * _block.Width) + x);
                        start++;
                    }

                    CleanupColumn(x, start, Math.Min(stripe + 4, _block.Height));
                }
            }

            for (int i = 0; i < _flags.Length; i++)
            {
                _flags[i] &= 0xfd;
            }

            if ((_style & 32) != 0 && ((Bit(18) << 3) | (Bit(18) << 2) | (Bit(18) << 1) | Bit(18)) != 10)
            {
                throw new InvalidDataException("Invalid JPEG 2000 segmentation symbol.");
            }
        }

        private void CleanupColumn(int x, int start, int end)
        {
            for (int y = start; y < end; y++)
            {
                int i = (y * _block.Width) + x;
                if ((_flags[i] & 3) == 0 && Bit(Context(x, y)) != 0)
                {
                    MakeSignificant(x, y, i);
                }
            }
        }

        private double[] Reconstruct(double step, bool reversible, int roi)
        {
            double[]? values = new double[_magnitude.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int magnitude = _magnitude[i];
                if (magnitude == 0)
                {
                    continue;
                }

                bool shifted = roi > 0 && magnitude >= (1 << roi);
                if (shifted)
                {
                    magnitude >>= roi;
                }

                int remaining = Math.Max(0, _lowest[i] - (shifted ? roi : 0));
                double value = magnitude + (reversible && remaining == 0 ? 0 : Math.ScaleB(0.5, remaining));
                if (reversible)
                {
                    value = Math.Floor(value);
                }

                values[i] = ((_flags[i] & 8) == 0 ? value : -value) * step;
            }

            return values;
        }

        private class RawBits
        {
            private readonly byte[] _data;
            private int _position;
            private int _bits;
            private int _value;
            internal RawBits(byte[] data) => _data = data;
            internal int Read()
            {
                if (_bits == 0)
                {
                    _bits = _value == 255 ? 7 : 8;
                    _value = _position < _data.Length ? _data[_position++] : 255;
                }

                return (_value >> --_bits) & 1;
            }
        }
    }
}
