using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Render.NET.Mesh
{
    internal class ThreeMfPackage : IDisposable
    {
        internal const string ModelRelationship = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";
        internal const string ThumbnailRelationship = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";
        private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private readonly MemoryStream _input;
        private readonly ZipArchive _archive;
        private readonly Dictionary<string, ZipArchiveEntry> _parts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ThreeMfReaderOptions _options;
        private long _readBytes;

        private ThreeMfPackage(MemoryStream input, ThreeMfReaderOptions options)
        {
            _input = input;
            _options = options;
            _archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            bool completed = false;
            try
            {
                if (_archive.Entries.Count > options.MaxArchiveEntries)
                {
                    throw new InvalidDataException("3MF exceeds MaxArchiveEntries.");
                }

                long total = 0;
                foreach (ZipArchiveEntry entry in _archive.Entries)
                {
                    total += entry.Length;
                    if (entry.Length > options.MaxPartBytes || total > options.MaxUncompressedBytes)
                    {
                        throw new InvalidDataException("3MF exceeds the uncompressed part or package limit.");
                    }

                    if (!entry.FullName.EndsWith('/') && !_parts.TryAdd(Resolve("", entry.FullName), entry))
                    {
                        throw new InvalidDataException("3MF has duplicate or ambiguous part names.");
                    }
                }
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    _archive.Dispose();
                }
            }
        }

        internal static async Task<ThreeMfPackage> OpenAsync(Stream source, ThreeMfReaderOptions options, CancellationToken token)
        {
            options.Validate();
            MemoryStream input = await BoundedInput.ReadAsync(source, options.MaxInputBytes, token).ConfigureAwait(false);
            ThreeMfPackage? package = null;
            try
            {
                package = new ThreeMfPackage(input, options);
                return package;
            }
            finally
            {
                if (package is null)
                {
                    await input.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        internal string CanonicalPart(string path) => _parts.TryGetValue(path, out ZipArchiveEntry? entry)
            ? Resolve("", entry.FullName) : throw new InvalidDataException($"Missing 3MF part '{path}'.");

        internal async Task<byte[]> ReadBytesAsync(string path, int maximum, CancellationToken token)
        {
            if (!_parts.TryGetValue(path, out ZipArchiveEntry? entry))
            {
                throw new InvalidDataException($"Missing 3MF part '{path}'.");
            }

            if (entry.Length > maximum)
            {
                throw new InvalidDataException("3MF part exceeds its configured byte limit.");
            }

            await using Stream source = await entry.OpenAsync(token).ConfigureAwait(false);
            await using MemoryStream bytes = await BoundedInput.ReadAsync(source, maximum, token).ConfigureAwait(false);
            _readBytes += bytes.Length;
            if (_readBytes > _options.MaxUncompressedBytes)
            {
                throw new InvalidDataException("3MF exceeds the total decompression budget.");
            }

            return bytes.ToArray();
        }

        internal async Task<XDocument> ReadXmlAsync(string path, CancellationToken token)
        {
            byte[] data = await ReadBytesAsync(path, _options.MaxPartBytes, token).ConfigureAwait(false);
            try
            {
                using MemoryStream bytes = new(data, writable: false);
                using XmlReader reader = XmlReader.Create(bytes, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = _options.MaxPartBytes
                });
                XDocument document = XDocument.Load(reader);
                token.ThrowIfCancellationRequested();
                return document;
            }
            catch (XmlException exception)
            {
                System.Diagnostics.Trace.TraceError("{0}", exception);
                throw new InvalidDataException($"Invalid XML in 3MF part '{path}'.", exception);
            }
        }

        internal async Task<string?> FindRootRelationshipAsync(string type, bool required, CancellationToken token)
        {
            XDocument document = await ReadXmlAsync("_rels/.rels", token).ConfigureAwait(false);
            if (document.Root?.Name != Relationships + "Relationships")
            {
                throw new InvalidDataException("Invalid 3MF package relationships.");
            }

            XElement[] matches = document.Root.Elements(Relationships + "Relationship")
                .Where(item => (string?)item.Attribute("Type") == type).ToArray();
            if (matches.Length == 0 && !required)
            {
                return null;
            }

            if (matches.Length != 1 || (string?)matches[0].Attribute("TargetMode") == "External")
            {
                throw new InvalidDataException("3MF requires one internal relationship of the requested type.");
            }

            string target = (string?)matches[0].Attribute("Target") ?? throw new InvalidDataException("3MF relationship is missing a target.");
            return CanonicalPart(Resolve("", target));
        }

        internal async Task<HashSet<string>> ModelReferencesAsync(string part, CancellationToken token)
        {
            int slash = part.LastIndexOf('/');
            string relationshipsPart = part[..(slash + 1)] + "_rels/" + part[(slash + 1)..] + ".rels";
            HashSet<string> references = new(StringComparer.OrdinalIgnoreCase);
            if (!_parts.ContainsKey(relationshipsPart))
            {
                return references;
            }

            XDocument document = await ReadXmlAsync(relationshipsPart, token).ConfigureAwait(false);
            if (document.Root?.Name != Relationships + "Relationships")
            {
                throw new InvalidDataException("Invalid 3MF model relationships.");
            }

            foreach (XElement item in document.Root.Elements(Relationships + "Relationship"))
            {
                if ((string?)item.Attribute("Type") == ModelRelationship)
                {
                    if ((string?)item.Attribute("TargetMode") == "External")
                    {
                        throw new InvalidDataException("External 3MF model relationships are unsupported.");
                    }

                    string target = (string?)item.Attribute("Target") ?? throw new InvalidDataException("Missing 3MF relationship target.");
                    references.Add(CanonicalPart(Resolve(part, target)));
                }
            }

            return references;
        }

        internal static string Resolve(string sourcePart, string target)
        {
            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(target);
            }
            catch (UriFormatException exception)
            {
                System.Diagnostics.Trace.TraceError("{0}", exception);
                throw new InvalidDataException("Invalid 3MF part URI.", exception);
            }

            if (decoded.Length == 0 || decoded.IndexOfAny(['\\', ':', '?', '#', '\0']) >= 0)
            {
                throw new InvalidDataException("3MF references must be internal package paths.");
            }

            string prefix = decoded.StartsWith('/') || sourcePart.Length == 0 ? "" : sourcePart[..(sourcePart.LastIndexOf('/') + 1)];
            List<string> segments = [];
            foreach (string segment in (prefix + decoded).Split('/'))
            {
                if (segment is "" or ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        throw new InvalidDataException("3MF part path escapes the package root.");
                    }

                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments.Add(segment);
                }
            }

            if (segments.Count == 0)
            {
                throw new InvalidDataException("3MF part path is empty.");
            }

            return string.Join('/', segments);
        }

        public void Dispose()
        {
            _archive.Dispose();
            _input.Dispose();
        }
    }
}
