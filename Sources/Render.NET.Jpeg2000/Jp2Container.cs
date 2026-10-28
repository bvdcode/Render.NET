using System;
using System.IO;

namespace Render.NET.Jpeg2000
{

    internal static class Jp2Container
    {
        internal static (ReadOnlyMemory<byte> Stream, ContainerInfo Info) Read(ReadOnlyMemory<byte> data)
        {
            ContainerInfo? info = new ContainerInfo();
            if (data.Length >= 2 && data.Span[0] == 255 && data.Span[1] == 0x4f)
            {
                return (data, info);
            }

            ByteReader? input = new ByteReader(data);
            (uint Type, ByteReader Data) first = Box(input);
            if (first.Type != 0x6a502020 || first.Data.U32() != 0x0d0a870a)
            {
                throw new InvalidDataException("Not a JP2 file or J2K codestream.");
            }

            first.Data.End();
            ReadOnlyMemory<byte> stream = default;
            bool header = false;
            bool brand = false;
            int boxes = 0;
            while (input.Remaining > 0)
            {
                if (++boxes > 65536)
                {
                    throw new InvalidDataException("Too many JP2 boxes.");
                }

                (uint Type, ByteReader Data) box = Box(input);
                ReadBox(box.Type, box.Data, info, ref header, ref brand, ref stream);
            }

            if (!brand || !header || stream.IsEmpty)
            {
                throw new InvalidDataException("JP2 is missing required boxes.");
            }

            return (stream, info);
        }

        private static void ReadBox(uint type, ByteReader data, ContainerInfo info, ref bool header, ref bool brand, ref ReadOnlyMemory<byte> stream)
        {
            switch (type)
            {
                case 0x66747970:
                    Brand(data);
                    brand = true;
                    break;
                case 0x6a703268:
                    if (header)
                    {
                        throw new InvalidDataException("Duplicate JP2 header.");
                    }

                    Header(data, info);
                    header = true;
                    break;
                case 0x6a703263:
                    if (!header || !stream.IsEmpty)
                    {
                        throw new InvalidDataException("Invalid JP2 codestream box order.");
                    }

                    stream = data.Data;
                    break;
            }
        }

        private static (uint Type, ByteReader Data) Box(ByteReader input)
        {
            uint size = input.U32();
            uint type = input.U32();
            long length = size;
            int header = 8;
            if (size == 1)
            {
                ulong large = ((ulong)input.U32() << 32) | input.U32();
                if (large > int.MaxValue)
                {
                    throw new InvalidDataException("JP2 box is too large.");
                }

                length = (long)large;
                header = 16;
            }

            if (size == 0)
            {
                length = (long)input.Remaining + header;
            }

            if (length < header || length - header > input.Remaining)
            {
                throw new InvalidDataException("Truncated or invalid JP2 box.");
            }

            return (type, new ByteReader(input.Take((int)length - header)));
        }

        private static void Brand(ByteReader input)
        {
            uint brand = input.U32();
            input.U32();
            bool compatible = brand == 0x6a703220;
            while (input.Remaining > 0)
            {
                compatible |= input.U32() == 0x6a703220;
            }

            if (!compatible)
            {
                throw new NotSupportedException("Only JP2-compatible containers are supported.");
            }
        }

        private static void Header(ByteReader input, ContainerInfo info)
        {
            int count = 0;
            while (input.Remaining > 0)
            {
                if (++count > 65536)
                {
                    throw new InvalidDataException("Too many JP2 header boxes.");
                }

                (uint Type, ByteReader Data) box = Box(input);
                switch (box.Type)
                {
                    case 0x69686472:
                        Dimensions(box.Data, info);
                        break;
                    case 0x636f6c72:
                        Colour(box.Data, info);
                        break;
                    case 0x63646566:
                        Channels(box.Data, info);
                        break;
                    case 0x70636c72:
                    case 0x636d6170:
                        throw new NotSupportedException("JP2 palette and component mapping boxes.");
                }
            }

            if (info.Components == 0)
            {
                throw new InvalidDataException("JP2 is missing the image header.");
            }
        }

        private static void Dimensions(ByteReader input, ContainerInfo info)
        {
            if (info.Components != 0)
            {
                throw new InvalidDataException("Duplicate JP2 image header.");
            }

            info.Height = CheckedDimension(input.U32());
            info.Width = CheckedDimension(input.U32());
            info.Components = input.U16();
            input.U8();
            if (input.U8() != 7)
            {
                throw new NotSupportedException("JP2 compression type.");
            }

            input.U8();
            input.U8();
            input.End();
        }

        private static int CheckedDimension(uint value)
        {
            if (value == 0 || value > int.MaxValue)
            {
                throw new InvalidDataException("Invalid JP2 dimensions.");
            }

            return (int)value;
        }

        private static void Colour(ByteReader input, ContainerInfo info)
        {
            int method = input.U8();
            input.U8();
            input.U8();
            if (method != 1)
            {
                info.Profile = true;
                return;
            }

            uint colour = input.U32();
            input.End();
            if (colour != 16 && colour != 17 && colour != 18)
            {
                throw new NotSupportedException("JP2 enumerated colour space.");
            }

            info.ColorSpace = (Jpeg2000ColorSpace)colour;
        }

        private static void Channels(ByteReader input, ContainerInfo info)
        {
            int count = input.U16();
            for (int i = 0; i < count; i++)
            {
                int channel = input.U16();
                int type = input.U16();
                int association = input.U16();
                if (channel >= info.Components)
                {
                    throw new InvalidDataException("Invalid JP2 channel index.");
                }

                if (type == 0)
                {
                    if (association != channel + 1)
                    {
                        throw new NotSupportedException("JP2 colour channel remapping.");
                    }

                    continue;
                }

                if ((type != 1 && type != 2) || association != 0 || info.Alpha >= 0)
                {
                    throw new NotSupportedException("JP2 channel definitions.");
                }

                info.Alpha = channel;
                info.Premultiplied = type == 2;
            }

            input.End();
        }
    }
}
