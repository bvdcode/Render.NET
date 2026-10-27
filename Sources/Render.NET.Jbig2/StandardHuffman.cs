using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Render.NET.Jbig2
{
    // Normative numerical data: ITU-T T.88 Annex B and T.6 Tables 2 and 3.
    internal static class StandardHuffman
    {
        internal static readonly HuffmanTable[] Tables = Load();
        internal static IEnumerable<string[]> Rows(string name)
        {
            using var stream = typeof(StandardHuffman).Assembly.GetManifestResourceStream("Render.NET.Jbig2.Tables." + name + ".csv")!;
            using var reader = new StreamReader(stream);
            reader.ReadLine();
            while (reader.ReadLine()is string line)
            {
                yield return line.Split(',');
            }
        }

        internal static int Number(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        private static HuffmanTable[] Load()
        {
            var rows = new List<HuffmanRow>[16];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new List<HuffmanRow>();
            }

            foreach (var row in Rows("Huffman"))
            {
                rows[Number(row[0])].Add(new HuffmanRow(Number(row[1]), Number(row[2]), Number(row[3]), row[4] == "1"));
            }

            return Array.ConvertAll(rows, r => new HuffmanTable(r));
        }
    }
}
