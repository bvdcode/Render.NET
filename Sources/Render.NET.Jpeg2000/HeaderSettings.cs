using System;
using System.Collections.Generic;
using System.IO;

namespace Render.NET.Jpeg2000
{
    internal class HeaderSettings
    {
        internal CodingStyle? DefaultCoding { get; set; }
        internal Quantization? DefaultQuantization { get; set; }
        internal Dictionary<int, CodingStyle> Coding { get; } = new();
        internal Dictionary<int, Quantization> Quantization { get; } = new();
        internal Dictionary<int, int> Roi { get; } = new();

        internal HeaderSettings Clone()
        {
            HeaderSettings? result = new HeaderSettings
            {
                DefaultCoding = DefaultCoding,
                DefaultQuantization = DefaultQuantization
            };
            foreach (KeyValuePair<int, CodingStyle> item in Coding)
            {
                result.Coding.Add(item.Key, item.Value);
            }

            foreach (KeyValuePair<int, Quantization> item in Quantization)
            {
                result.Quantization.Add(item.Key, item.Value);
            }

            foreach (KeyValuePair<int, int> item in Roi)
            {
                result.Roi.Add(item.Key, item.Value);
            }

            return result;
        }
    }
}
