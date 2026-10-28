using System.Globalization;
using System.Text;

namespace Render.NET.Tests
{
    internal static class PdfTestDocument
    {
        internal static byte[] Create(string content, string pageEntries = "", string resources = "", params byte[][] extraObjects)
        {
            var objects = new List<byte[]>
            {
                Bytes("<< /Type /Catalog /Pages 2 0 R >>"),
                Bytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
                Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << " + resources + " >> /Contents 4 0 R " + pageEntries + " >>"),
                Stream(Bytes(content))
            };
            objects.AddRange(extraObjects);
            return Write(objects);
        }

        internal static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

        internal static byte[] Stream(byte[] bytes, string dictionary = "")
        {
            using MemoryStream? output = new MemoryStream();
            output.Write(Bytes("<< /Length " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " " + dictionary + " >>\nstream\n"));
            output.Write(bytes);
            output.Write(Bytes("\nendstream"));
            return output.ToArray();
        }

        internal static byte[] Write(List<byte[]> objects)
        {
            using MemoryStream? output = new MemoryStream();
            output.Write(Bytes("%PDF-1.7\n%\u00e2\u00e3\u00cf\u00d3\n"));
            var offsets = new List<long> { 0 };
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                output.Write(Bytes((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
                output.Write(objects[i]);
                output.Write(Bytes("\nendobj\n"));
            }

            long xref = output.Position;
            output.Write(Bytes("xref\n0 " + offsets.Count.ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n"));
            foreach (long offset in offsets.Skip(1))
            {
                output.Write(Bytes(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
            }

            output.Write(Bytes("trailer\n<< /Size " + offsets.Count.ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R >>\nstartxref\n"
                + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n"));
            return output.ToArray();
        }
    }
}
