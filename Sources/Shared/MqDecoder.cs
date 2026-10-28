using System;

namespace Render.NET.Entropy
{
    // Numerical probability states specified by ITU-T T.800 Table C.2 and ITU-T T.88 Table E.1.
    internal class MqDecoder
    {
        private static readonly (ushort Probability, byte NextMps, byte NextLps)[] ProbabilityStates =
        {
            (0x5601, 1, 1),
            (0x3401, 2, 6),
            (0x1801, 3, 9),
            (0x0ac1, 4, 12),
            (0x0521, 5, 29),
            (0x0221, 38, 33),
            (0x5601, 7, 6),
            (0x5401, 8, 14),
            (0x4801, 9, 14),
            (0x3801, 10, 14),
            (0x3001, 11, 17),
            (0x2401, 12, 18),
            (0x1c01, 13, 20),
            (0x1601, 29, 21),
            (0x5601, 15, 14),
            (0x5401, 16, 14),
            (0x5101, 17, 15),
            (0x4801, 18, 16),
            (0x3801, 19, 17),
            (0x3401, 20, 18),
            (0x3001, 21, 19),
            (0x2801, 22, 19),
            (0x2401, 23, 20),
            (0x2201, 24, 21),
            (0x1c01, 25, 22),
            (0x1801, 26, 23),
            (0x1601, 27, 24),
            (0x1401, 28, 25),
            (0x1201, 29, 26),
            (0x1101, 30, 27),
            (0x0ac1, 31, 28),
            (0x09c1, 32, 29),
            (0x08a1, 33, 30),
            (0x0521, 34, 31),
            (0x0441, 35, 32),
            (0x02a1, 36, 33),
            (0x0221, 37, 34),
            (0x0141, 38, 35),
            (0x0111, 39, 36),
            (0x0085, 40, 37),
            (0x0049, 41, 38),
            (0x0025, 42, 39),
            (0x0015, 43, 40),
            (0x0009, 44, 41),
            (0x0005, 45, 42),
            (0x0001, 45, 43),
            (0x5601, 46, 46)
        };
        private readonly byte[] _data;
        private readonly byte[] _states;
        private uint _range = 0x8000;
        private uint _code;
        private int _bits;
        private int _position;
        internal MqDecoder(byte[] data, byte[] states)
        {
            _data = data;
            _states = states;
            _code = (uint)At(0) << 16;
            Input();
            _code <<= 7;
            _bits -= 7;
        }

        private int At(int index) => index < _data.Length ? _data[index] : 255;
        private void Input()
        {
            int next = At(_position + 1);
            if (At(_position) == 255 && next > 0x8f)
            {
                _code += 0xff00;
                _bits = 8;
            }
            else
            {
                int shift = At(_position) == 255 ? 9 : 8;
                _position++;
                _code += (uint)next << shift;
                _bits = shift == 9 ? 7 : 8;
            }
        }

        internal int Read(int context) => Read(_states, context);

        internal int Read(byte[] states, int context)
        {
            int state = states[context] >> 1;
            int mps = states[context] & 1;
            uint probability = ProbabilityStates[state].Probability;
            _range -= probability;
            int result;
            if ((_code >> 16) < probability)
            {
                bool exchange = _range < probability;
                result = exchange ? mps : 1 - mps;
                states[context] = Updated(state, mps, !exchange);
                _range = probability;
            }
            else
            {
                _code -= probability << 16;
                if (_range >= 0x8000)
                {
                    return mps;
                }

                bool exchange = _range < probability;
                result = exchange ? 1 - mps : mps;
                states[context] = Updated(state, mps, exchange);
            }

            Renormalize();
            return result;
        }

        private static byte Updated(int state, int mps, bool lps)
        {
            if (lps && (state == 0 || state == 6 || state == 14))
            {
                mps ^= 1;
            }

            return (byte)(((lps ? ProbabilityStates[state].NextLps : ProbabilityStates[state].NextMps) << 1) | mps);
        }

        private void Renormalize()
        {
            do
            {
                if (_bits == 0)
                {
                    Input();
                }

                _range <<= 1;
                _code <<= 1;
                _bits--;
            }
            while (_range < 0x8000);
        }

        internal static void Reset(byte[] states)
        {
            Array.Clear(states);
            states[0] = 8;
            states[17] = 6;
            states[18] = 92;
        }
    }
}
