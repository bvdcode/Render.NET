using System;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal static class HeaderParser
    {
        internal static void Read(int marker, ByteReader data, HeaderSettings settings, int components)
        {
            switch (marker)
            {
                case 0xff52:
                    settings.DefaultCoding = Coding(data, null);
                    break;
                case 0xff53:
                    int c = Component(data, components);
                    settings.Coding[c] = Coding(data, settings.DefaultCoding ?? throw new InvalidDataException("COC before COD."));
                    break;
                case 0xff5c:
                    settings.DefaultQuantization = Quantized(data);
                    break;
                case 0xff5d:
                    settings.Quantization[Component(data, components)] = Quantized(data);
                    break;
                case 0xff5e:
                    Region(data, settings, components);
                    break;
                case 0xff55: // Tile-part length index.
                case 0xff57: // Packet length index in main header.
                case 0xff58: // Packet length index in tile header.
                case 0xff64:
                    return; // Comment.
                case 0xff5f:
                    throw new NotSupportedException("JPEG 2000 progression order changes (POC).");
                case 0xff60:
                case 0xff61:
                    throw new NotSupportedException("JPEG 2000 packed packet headers (PPM/PPT).");
                default:
                    throw new NotSupportedException("JPEG 2000 marker 0x" + marker.ToString("X4"));
            }

            data.End();
        }

        private static int Component(ByteReader data, int count)
        {
            int component = count < 257 ? data.U8() : data.U16();
            if (component >= count)
            {
                throw new InvalidDataException("Invalid JPEG 2000 component index.");
            }

            return component;
        }

        private static CodingStyle Coding(ByteReader data, CodingStyle? inherited)
        {
            int flags = data.U8();
            if ((flags & ~(inherited == null ? 7 : 1)) != 0)
            {
                throw new NotSupportedException("JPEG 2000 coding style flags.");
            }

            int progression = inherited?.Progression ?? data.U8();
            int layers = inherited?.Layers ?? data.U16();
            int mct = inherited == null ? data.U8() : inherited.Mct ? 1 : 0;
            if (progression > 4 || layers < 1 || mct > 1)
            {
                throw new NotSupportedException("JPEG 2000 progression or component transform.");
            }

            int levels = data.U8();
            int bx = data.U8() + 2;
            int by = data.U8() + 2;
            int style = data.U8();
            int transform = data.U8();
            ValidateCoding(levels, bx, by, style, transform);
            var precincts = ReadPrecincts(data, flags, levels);
            if (inherited != null)
            {
                flags |= inherited.Flags & 6;
            }

            return new CodingStyle(flags, progression, layers, mct == 1, levels, bx, by, style, transform == 1, precincts);
        }

        private static void ValidateCoding(int levels, int bx, int by, int style, int transform)
        {
            if (levels > 16 || bx > 10 || by > 10 || bx + by > 12 || style > 63 || transform > 1)
            {
                throw new NotSupportedException("JPEG 2000 decomposition, code-block style or transform.");
            }
        }

        private static byte[] ReadPrecincts(ByteReader data, int flags, int levels)
        {
            var precincts = new byte[levels + 1];
            for (int i = 0; i < precincts.Length; i++)
            {
                precincts[i] = (flags & 1) != 0 ? data.U8() : (byte)255;
                if (i > 0 && ((precincts[i] & 15) == 0 || (precincts[i] >> 4) == 0))
                {
                    throw new InvalidDataException("Invalid JPEG 2000 precinct exponents.");
                }
            }

            return precincts;
        }

        private static Quantization Quantized(ByteReader data)
        {
            int control = data.U8();
            int kind = control & 31;
            if (kind > 2)
            {
                throw new NotSupportedException("JPEG 2000 quantization style.");
            }

            int count = data.Remaining / (kind == 0 ? 1 : 2);
            if (count == 0 || count > 49 || (kind == 1 && count != 1))
            {
                throw new InvalidDataException("Invalid JPEG 2000 quantization table.");
            }

            var exponents = new int[count];
            var mantissas = new int[count];
            for (int i = 0; i < count; i++)
            {
                int value = kind == 0 ? data.U8() << 8 : data.U16();
                exponents[i] = value >> 11;
                mantissas[i] = value & 2047;
            }

            return new Quantization(control >> 5, kind, exponents, mantissas);
        }

        private static void Region(ByteReader data, HeaderSettings settings, int components)
        {
            int c = Component(data, components);
            if (data.U8() != 0)
            {
                throw new NotSupportedException("JPEG 2000 ROI style.");
            }

            int shift = data.U8();
            if (shift > 30)
            {
                throw new NotSupportedException("JPEG 2000 ROI shift above 30 bits.");
            }

            settings.Roi[c] = shift;
        }
    }
}
