using System;

namespace Render.NET.Jpeg2000
{
    // Numerical probability states specified by ITU-T T.800 (11/2015), Table C.2.
    internal sealed class MqDecoder
    {
        private static readonly ushort[] Qe =
        {
            0x5601,
            0x3401,
            0x1801,
            0x0ac1,
            0x0521,
            0x0221,
            0x5601,
            0x5401,
            0x4801,
            0x3801,
            0x3001,
            0x2401,
            0x1c01,
            0x1601,
            0x5601,
            0x5401,
            0x5101,
            0x4801,
            0x3801,
            0x3401,
            0x3001,
            0x2801,
            0x2401,
            0x2201,
            0x1c01,
            0x1801,
            0x1601,
            0x1401,
            0x1201,
            0x1101,
            0x0ac1,
            0x09c1,
            0x08a1,
            0x0521,
            0x0441,
            0x02a1,
            0x0221,
            0x0141,
            0x0111,
            0x0085,
            0x0049,
            0x0025,
            0x0015,
            0x0009,
            0x0005,
            0x0001,
            0x5601
        };
        private static readonly byte[] NextMps =
        {
            1,
            2,
            3,
            4,
            5,
            38,
            7,
            8,
            9,
            10,
            11,
            12,
            13,
            29,
            15,
            16,
            17,
            18,
            19,
            20,
            21,
            22,
            23,
            24,
            25,
            26,
            27,
            28,
            29,
            30,
            31,
            32,
            33,
            34,
            35,
            36,
            37,
            38,
            39,
            40,
            41,
            42,
            43,
            44,
            45,
            45,
            46
        };
        private static readonly byte[] NextLps =
        {
            1,
            6,
            9,
            12,
            29,
            33,
            6,
            14,
            14,
            14,
            17,
            18,
            20,
            21,
            14,
            14,
            15,
            16,
            17,
            18,
            19,
            19,
            20,
            21,
            22,
            23,
            24,
            25,
            26,
            27,
            28,
            29,
            30,
            31,
            32,
            33,
            34,
            35,
            36,
            37,
            38,
            39,
            40,
            41,
            42,
            43,
            46
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

        internal int Read(int context)
        {
            int state = _states[context] >> 1;
            int mps = _states[context] & 1;
            uint probability = Qe[state];
            _range -= probability;
            int result;
            if ((_code >> 16) < probability)
            {
                bool exchange = _range < probability;
                result = exchange ? mps : 1 - mps;
                _states[context] = Updated(state, mps, !exchange);
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
                _states[context] = Updated(state, mps, exchange);
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

            return (byte)(((lps ? NextLps[state] : NextMps[state]) << 1) | mps);
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
