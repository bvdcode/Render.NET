using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using Render.NET.Mesh;

namespace Render.NET.Tests
{
    public class StlReaderTests
    {
        private const string AsciiTriangle = """
            solid triangle
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 1e0 0 0
                  vertex 0 1 0
                endloop
              endfacet
            endsolid triangle
            """;

        [Test]
        public async Task ReadsAsciiWithBomMultipleSolidsAndInvariantNumbers()
        {
            using MemoryStream stream = new(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(AsciiTriangle + "\n" + AsciiTriangle)).ToArray());
            TriangleMesh mesh = await StlReader.ReadAsync(stream);
            Assert.That(mesh.TriangleCount, Is.EqualTo(2));
            Assert.That(mesh.Vertices.Span[1], Is.EqualTo(Vector3.UnitX));
            Assert.That(stream.CanRead, Is.True);
        }

        [Test]
        public async Task BinaryHeaderStartingWithSolidIsStillBinary()
        {
            using MemoryStream stream = new(BinaryTriangle());
            TriangleMesh mesh = await StlReader.ReadAsync(stream);
            Assert.That(mesh.TriangleCount, Is.EqualTo(1));
            Assert.That(mesh.Vertices.Span[2], Is.EqualTo(Vector3.UnitY));
        }

        [Test]
        public async Task ReadsFromCurrentPositionOnNonSeekableStream()
        {
            using MemoryStream inner = new(new byte[7].Concat(BinaryTriangle()).ToArray());
            inner.Position = 7;
            using NonSeekableStream source = new(inner);
            TriangleMesh mesh = await StlReader.ReadAsync(source);
            Assert.That(mesh.TriangleCount, Is.EqualTo(1));
            Assert.That(inner.CanRead, Is.True);
        }

        [Test]
        public void RejectsTruncatedBinaryAndNonFiniteCoordinates()
        {
            byte[] bytes = BinaryTriangle();
            Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(new MemoryStream(bytes[..^1])));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(96), float.NaN);
            Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(new MemoryStream(bytes)));
        }

        [TestCase("endsolid triangle", "")]
        [TestCase("endfacet", "invalid")]
        [TestCase("vertex 1e0 0 0", "vertex NaN 0 0")]
        [TestCase("vertex 1e0 0 0", "vertex 1,5 0 0")]
        public void RejectsMalformedAscii(string oldValue, string newValue)
        {
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(AsciiTriangle.Replace(oldValue, newValue)));
            Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(stream));
        }

        [Test]
        public void EnforcesByteAndTriangleLimitsForBothFormats()
        {
            foreach (byte[] data in new[] { BinaryTriangle(), Encoding.UTF8.GetBytes(AsciiTriangle) })
            {
                Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await StlReader.ReadAsync(new NonSeekableStream(new MemoryStream(data)), new StlReaderOptions { MaxInputBytes = 32 }));
            }

            using MemoryStream multiple = new(Encoding.UTF8.GetBytes(AsciiTriangle + "\n" + AsciiTriangle));
            Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(multiple, new StlReaderOptions { MaxTriangleCount = 1 }));
            byte[] twoBinary = new byte[184];
            BinaryTriangle().CopyTo(twoBinary, 0);
            BinaryTriangle().AsSpan(84).CopyTo(twoBinary.AsSpan(134));
            BinaryPrimitives.WriteUInt32LittleEndian(twoBinary.AsSpan(80), 2);
            Assert.ThrowsAsync<InvalidDataException>(async () => await StlReader.ReadAsync(new MemoryStream(twoBinary), new StlReaderOptions { MaxTriangleCount = 1 }));
        }

        [Test]
        public void HonorsCancellation()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await StlReader.ReadAsync(new MemoryStream(BinaryTriangle()), cancellationToken: cancellation.Token));
        }

        internal static byte[] BinaryTriangle()
        {
            byte[] bytes = new byte[134];
            Encoding.ASCII.GetBytes("solid binary header").CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), 1);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(108), 1);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(124), 1);
            return bytes;
        }

        private class NonSeekableStream(Stream inner) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
