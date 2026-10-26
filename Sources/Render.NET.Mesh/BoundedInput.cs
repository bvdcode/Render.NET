using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Render.NET.Mesh
{
    internal static class BoundedInput
    {
        internal static async Task<MemoryStream> ReadAsync(Stream source, int maximum, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, Array.MaxLength);
            if (!source.CanRead)
            {
                throw new ArgumentException("The source must be readable.", nameof(source));
            }

            token.ThrowIfCancellationRequested();
            if (source.CanSeek && source.Length - source.Position > maximum)
            {
                throw new InvalidDataException("The input exceeds the configured byte limit.");
            }

            MemoryStream result = new();
            try
            {
                byte[] buffer = new byte[65536];
                while (true)
                {
                    int count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (count == 0)
                    {
                        result.Position = 0;
                        return result;
                    }

                    if (result.Length + count > maximum)
                    {
                        throw new InvalidDataException("The input exceeds the configured byte limit.");
                    }

                    result.Write(buffer, 0, count);
                }
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
    }
}
