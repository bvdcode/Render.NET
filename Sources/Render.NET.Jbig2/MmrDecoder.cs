using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jbig2
{
    internal static class MmrDecoder
    {
        private static readonly Dictionary<int, int>[] Runs = Codes();
        private static Dictionary<int, int>[] Codes()
        {
            var result = new[]
            {
                new Dictionary<int, int>(),
                new Dictionary<int, int>()
            };
            foreach (var row in StandardHuffman.Rows("Fax"))
            {
                for (int color = 0; color < result.Length; color++)
                {
                    string code = row[color + 1];
                    result[color].Add((1 << code.Length) | Convert.ToInt32(code, 2), StandardHuffman.Number(row[0]));
                }
            }

            return result;
        }

        internal static Bitmap Decode(BitReader input, int width, int height, DecodeBudget budget, bool requireTerminator = false)
        {
            var bitmap = new Bitmap(width, height, budget);
            budget.Work((long)width * height);
            budget.Allocate(((long)width + 2) * 8);
            var reference = new int[checked(width + 2)];
            var current = new int[reference.Length];
            int referenceCount = 2;
            reference[0] = width;
            reference[1] = width;
            for (int y = 0; y < height; y++)
            {
                budget.Cancellation.ThrowIfCancellationRequested();
                int a0 = 0, color = 0, steps = 0;
                bool start = true;
                while (a0 < width)
                {
                    if (++steps > ((long)width * 4) + 100)
                    {
                        throw new InvalidDataException("JBIG2 MMR row makes no progress.");
                    }

                    int mode = Mode(input), index = Reference(reference.AsSpan(0, referenceCount), a0, color, start), b1 = reference[index];
                    if (mode == 10)
                    {
                        int a1 = checked(a0 + Run(input, color, width - a0));
                        int a2 = checked(a1 + Run(input, 1 - color, width - a1));
                        Paint(bitmap, y, a0, a1, color);
                        Paint(bitmap, y, a1, a2, 1 - color);
                        a0 = a2;
                    }
                    else if (mode == 11)
                    {
                        int b2 = reference[Math.Min(index + 1, referenceCount - 1)];
                        Paint(bitmap, y, a0, b2, color);
                        a0 = b2;
                    }
                    else
                    {
                        int a1 = checked(b1 + mode);
                        if (a1 < a0 || a1 > width)
                        {
                            throw new InvalidDataException("Invalid JBIG2 MMR vertical transition.");
                        }

                        Paint(bitmap, y, a0, a1, color);
                        a0 = a1;
                        color ^= 1;
                    }

                    start = false;
                }

                referenceCount = Changes(bitmap, y, current);
                (reference, current) = (current, reference);
            }

            bool terminator = input.AvailableBits >= 24 && input.Peek(24) == 0x001001;
            if (requireTerminator && !terminator)
            {
                throw new InvalidDataException("JBIG2 MMR is missing EOFB.");
            }

            if (terminator)
            {
                input.Bits(24);
            }

            input.Align();
            return bitmap;
        }

        private static int Reference(ReadOnlySpan<int> changes, int a0, int color, bool start)
        {
            int low = 0, high = changes.Length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (changes[middle] < a0 || (!start && changes[middle] == a0))
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if ((low & 1) != color)
            {
                low++;
            }

            return Math.Min(low, changes.Length - 2);
        }

        private static int Changes(Bitmap bitmap, int y, int[] result)
        {
            int count = 0, color = 0;
            for (int x = 0; x < bitmap.Width; x++)
            {
                int pixel = bitmap.Get(x, y);
                if (pixel != color)
                {
                    result[count++] = x;
                    color = pixel;
                }
            }

            result[count++] = bitmap.Width;
            result[count++] = bitmap.Width;
            return count;
        }

        private static void Paint(Bitmap bitmap, int y, int from, int to, int color)
        {
            if (color != 0)
            {
                for (int x = from; x < to; x++)
                {
                    bitmap.Set(x, y, 1);
                }
            }
        }

        private static int Run(BitReader input, int color, int maximum)
        {
            int total = 0;
            var codes = Runs[color];
            while (true)
            {
                int code = 1, run = -1;
                for (int length = 1; length <= 13; length++)
                {
                    code = (code << 1) | input.Bit();
                    if (codes.TryGetValue(code, out run))
                    {
                        break;
                    }

                    run = -1;
                }

                if (run < 0 || run > maximum - total)
                {
                    throw new InvalidDataException("Invalid JBIG2 MMR run.");
                }

                total += run;
                if (run < 64)
                {
                    return total;
                }
            }
        }

        private static int Mode(BitReader input)
        {
            int code = 1;
            for (int length = 1; length <= 7; length++)
            {
                code = (code << 1) | input.Bit();
                int? mode = code switch
                {
                    3 => 0,
                    9 => 10,
                    10 => -1,
                    11 => 1,
                    17 => 11,
                    66 => -2,
                    67 => 2,
                    130 => -3,
                    131 => 3,
                    _ => null
                };
                if (mode.HasValue)
                {
                    return mode.Value;
                }
            }

            throw new NotSupportedException("Invalid or unsupported JBIG2 MMR extension mode.");
        }
    }
}
