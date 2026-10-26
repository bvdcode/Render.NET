using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;

namespace Render.NET.Mesh
{
    internal static class AsciiStlParser
    {
        internal static TriangleMesh Parse(string text, StlReaderOptions options, CancellationToken token)
        {
            using StringReader reader = new(text);
            List<Vector3> vertices = [];
            string[]? line = NextLine(reader);
            while (line is not null)
            {
                RequireWord(line, 0, "solid");
                line = NextLine(reader);
                while (line is not null && !EqualsWord(line[0], "endsolid"))
                {
                    token.ThrowIfCancellationRequested();
                    if (vertices.Count / 3 >= options.MaxTriangleCount)
                    {
                        throw new InvalidDataException("The STL input exceeds MaxTriangleCount.");
                    }

                    RequireLength(line, 5);
                    RequireWord(line, 0, "facet");
                    RequireWord(line, 1, "normal");
                    ParseVector(line, 2);
                    string[] outer = RequiredLine(reader);
                    RequireLength(outer, 2);
                    RequireWord(outer, 0, "outer");
                    RequireWord(outer, 1, "loop");
                    for (int corner = 0; corner < 3; corner++)
                    {
                        string[] vertex = RequiredLine(reader);
                        RequireLength(vertex, 4);
                        RequireWord(vertex, 0, "vertex");
                        vertices.Add(ParseVector(vertex, 1));
                    }

                    string[] endLoop = RequiredLine(reader);
                    RequireLength(endLoop, 1);
                    RequireWord(endLoop, 0, "endloop");
                    string[] endFacet = RequiredLine(reader);
                    RequireLength(endFacet, 1);
                    RequireWord(endFacet, 0, "endfacet");
                    line = NextLine(reader);
                }

                if (line is null)
                {
                    throw new InvalidDataException("The ASCII STL is missing endsolid.");
                }

                line = NextLine(reader);
            }

            if (vertices.Count == 0)
            {
                throw new InvalidDataException("The STL file contains no triangles.");
            }

            int[] indices = new int[vertices.Count];
            for (int index = 0; index < indices.Length; index++)
            {
                indices[index] = index;
            }

            return new TriangleMesh(vertices.ToArray(), indices);
        }

        private static Vector3 ParseVector(string[] tokens, int offset)
        {
            return new Vector3(ParseNumber(tokens[offset]), ParseNumber(tokens[offset + 1]), ParseNumber(tokens[offset + 2]));
        }

        private static float ParseNumber(string text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            {
                throw new InvalidDataException("STL coordinates and normals must be finite numbers.");
            }

            return value;
        }

        private static string[]? NextLine(StringReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0)
                {
                    return tokens;
                }
            }

            return null;
        }

        private static string[] RequiredLine(StringReader reader)
        {
            return NextLine(reader) ?? throw new InvalidDataException("The ASCII STL ended inside a facet.");
        }

        private static void RequireLength(string[] tokens, int length)
        {
            if (tokens.Length != length)
            {
                throw new InvalidDataException("The ASCII STL contains an invalid facet line.");
            }
        }

        private static void RequireWord(string[] tokens, int index, string word)
        {
            if (index >= tokens.Length || !EqualsWord(tokens[index], word))
            {
                throw new InvalidDataException($"Expected '{word}' in ASCII STL.");
            }
        }

        private static bool EqualsWord(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
