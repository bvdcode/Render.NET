using System;
using System.Collections.Generic;
using System.Numerics;

namespace Render.NET.Svg
{
    internal sealed partial class SvgTrueType
    {
        internal float Kerning(int left, int right)
        {
            if (_pairs.TryGetValue((left, right), out float cached))
            {
                return cached;
            }

            float value = GposKerning(left, right);
            if (float.IsNaN(value))
            {
                value = LegacyKerning(left, right);
            }

            _pairs[(left, right)] = value;
            return value;
        }

        private float LegacyKerning(int left, int right)
        {
            if (!_tables.TryGetValue("kern", out var table) || table.Length < 4 || U16(table.Start) != 0)
            {
                return 0;
            }

            int count = U16(table.Start + 2), position = table.Start + 4;
            float result = 0;
            for (int i = 0; i < count; i++)
            {
                int length = U16(position + 2), coverage = U16(position + 4);
                if (length < 6 || position > (table.Start + table.Length) - length)
                {
                    break;
                }

                if ((coverage >> 8) == 0 && (coverage & 7) == 1)
                {
                    int pairs = U16(position + 6), lo = 0, hi = pairs - 1;
                    uint target = ((uint)left << 16) | (uint)right;
                    while (lo <= hi)
                    {
                        int mid = lo + ((hi - lo) / 2), record = position + 14 + (mid * 6);
                        uint pair = U32(record);
                        if (pair < target)
                        {
                            lo = mid + 1;
                        }
                        else if (pair > target)
                        {
                            hi = mid - 1;
                        }
                        else
                        {
                            result = (coverage & 8) != 0 ? S16(record + 4) : result + S16(record + 4);
                            break;
                        }
                    }
                }

                position += length;
            }

            return result;
        }

        private float GposKerning(int left, int right)
        {
            if (!_tables.TryGetValue("GPOS", out var table) || table.Length < 10)
            {
                return float.NaN;
            }

            int featureList = table.Start + U16(table.Start + 6), lookupList = table.Start + U16(table.Start + 8);
            var indices = new HashSet<int>();
            for (int i = 0; i < U16(featureList); i++)
            {
                int record = featureList + 2 + (i * 6);
                if (Tag(record) != "kern")
                {
                    continue;
                }

                int feature = featureList + U16(record + 4);
                for (int j = 0; j < U16(feature + 2); j++)
                {
                    indices.Add(U16(feature + 4 + (j * 2)));
                }
            }

            float result = 0;
            bool applicable = false;
            foreach (int index in indices)
            {
                if (index >= U16(lookupList))
                {
                    continue;
                }

                int lookup = lookupList + U16(lookupList + 2 + (index * 2)), type = U16(lookup);
                for (int i = 0; i < U16(lookup + 4); i++)
                {
                    int subtable = lookup + U16(lookup + 6 + (i * 2)), actual = type;
                    if (type == 9 && U16(subtable) == 1)
                    {
                        actual = U16(subtable + 2);
                        subtable += Offset(subtable + 4);
                    }

                    if (actual == 2 && PairPosition(subtable, left, right) is { } pair)
                    {
                        applicable = true;
                        result += pair;
                        break;
                    }
                }
            }

            return applicable ? result : float.NaN;
        }

        private float? PairPosition(int table, int left, int right)
        {
            int coverage = Coverage(table + U16(table + 2), left);
            if (coverage < 0)
            {
                return null;
            }

            int f1 = U16(table + 4), f2 = U16(table + 6), size1 = BitOperations.PopCount((uint)f1) * 2, size2 = BitOperations.PopCount((uint)f2) * 2;
            if (U16(table) == 1)
            {
                if (coverage >= U16(table + 8))
                {
                    return null;
                }

                int set = table + U16(table + 10 + (coverage * 2)), lo = 0, hi = U16(set) - 1, stride = 2 + size1 + size2;
                while (lo <= hi)
                {
                    int mid = lo + ((hi - lo) / 2), position = set + 2 + (mid * stride), other = U16(position);
                    if (other == right)
                    {
                        return XAdvance(position + 2, f1);
                    }

                    if (other < right)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }

                return null;
            }

            if (U16(table) == 2)
            {
                int a = Class(table + U16(table + 8), left), b = Class(table + U16(table + 10), right);
                int rows = U16(table + 12), columns = U16(table + 14);
                return a >= rows || b >= columns ? null : XAdvance(table + 16 + (((a * columns) + b) * (size1 + size2)), f1);
            }

            return null;
        }

        private float XAdvance(int table, int format)
        {
            int offset = BitOperations.PopCount((uint)(format & 3)) * 2;
            return (format & 4) != 0 ? S16(table + offset) : 0;
        }

        private int Coverage(int table, int glyph)
        {
            if (U16(table) == 1)
            {
                int lo = 0, hi = U16(table + 2) - 1;
                while (lo <= hi)
                {
                    int mid = lo + ((hi - lo) / 2), value = U16(table + 4 + (mid * 2));
                    if (value == glyph)
                    {
                        return mid;
                    }

                    if (value < glyph)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }
            }
            else if (U16(table) == 2)
            {
                for (int i = 0; i < U16(table + 2); i++)
                {
                    int range = table + 4 + (i * 6), start = U16(range), end = U16(range + 2);
                    if (glyph >= start && glyph <= end)
                    {
                        return (U16(range + 4) + glyph) - start;
                    }
                }
            }

            return -1;
        }

        private int Class(int table, int glyph)
        {
            if (U16(table) == 1)
            {
                int start = U16(table + 2), count = U16(table + 4);
                return glyph >= start && glyph - start < count ? U16(table + 6 + ((glyph - start) * 2)) : 0;
            }

            if (U16(table) == 2)
            {
                for (int i = 0; i < U16(table + 2); i++)
                {
                    int range = table + 4 + (i * 6);
                    if (glyph >= U16(range) && glyph <= U16(range + 2))
                    {
                        return U16(range + 4);
                    }
                }
            }

            return 0;
        }
    }
}
