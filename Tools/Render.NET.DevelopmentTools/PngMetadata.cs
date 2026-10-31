using System.Buffers.Binary;

namespace Render.NET.DevelopmentTools
{
    public static class PngMetadata
    {
        public static async Task StripAsync(string path)
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (bytes.Length < signature.Length || !bytes.AsSpan(0, signature.Length).SequenceEqual(signature))
            {
                throw new InvalidDataException($"Renderer did not produce PNG: {path}");
            }
            using MemoryStream result = new();
            result.Write(signature);
            int offset = signature.Length;
            bool ended = false;
            while (offset < bytes.Length)
            {
                if (bytes.Length - offset < 12)
                {
                    throw new InvalidDataException("Truncated PNG chunk.");
                }
                uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
                if (length > bytes.Length - offset - 12)
                {
                    throw new InvalidDataException("Invalid PNG chunk length.");
                }
                int chunkLength = checked((int)length + 12);
                string type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
                if (type is "IHDR" or "PLTE" or "tRNS" or "IDAT" or "IEND")
                {
                    result.Write(bytes.AsSpan(offset, chunkLength));
                }
                offset += chunkLength;
                if (type == "IEND")
                {
                    ended = true;
                    break;
                }
            }
            if (!ended)
            {
                throw new InvalidDataException("PNG has no end chunk.");
            }
            await File.WriteAllBytesAsync(path, result.ToArray());
        }
    }
}
