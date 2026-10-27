using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Render.NET.Svg
{
    /// <summary>Reads SVG without executing scripts or loading external resources.</summary>
    public static class SvgReader
    {
        /// <summary>Parses SVG XML and interprets vector geometry.</summary>
        public static SvgDocument Read(string xml, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(xml);
            options ??= new SvgReaderOptions();
            Validate(options);
            cancellationToken.ThrowIfCancellationRequested();
            if (Encoding.UTF8.GetByteCount(xml) > options.MaximumInputBytes)
            {
                throw new InvalidDataException("SVG exceeds MaximumInputBytes.");
            }

            using var source = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = options.MaximumInputBytes });
            XElement root = XElement.Load(reader);
            return new SvgSceneBuilder(root, options, cancellationToken).Build();
        }

        /// <summary>Reads bounded SVG input and leaves the source stream open.</summary>
        public static async Task<SvgDocument> ReadAsync(Stream source, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new SvgReaderOptions();
            Validate(options);
            using var data = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (data.Length + count > options.MaximumInputBytes)
                {
                    throw new InvalidDataException("SVG exceeds MaximumInputBytes.");
                }

                data.Write(buffer, 0, count);
            }

            data.Position = 0;
            using var text = new StreamReader(data, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Read(await text.ReadToEndAsync(cancellationToken).ConfigureAwait(false), options, cancellationToken);
        }

        /// <summary>Reads a local SVG file.</summary>
        public static async Task<SvgDocument> ReadFileAsync(string path, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            await using var source = File.OpenRead(path);
            return await ReadAsync(source, options, cancellationToken).ConfigureAwait(false);
        }

        private static void Validate(SvgReaderOptions o)
        {
            if (o.MaximumInputBytes <= 0 || o.MaximumElements <= 0 || o.MaximumPathCommands <= 0 || o.MaximumDepth < 1 || o.MaximumDepth > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(o));
            }
        }
    }
}
