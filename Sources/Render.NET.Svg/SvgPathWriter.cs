using System;
using System.Globalization;
using System.Text;

namespace Render.NET.Svg
{
    /// <summary>Serializes core vector geometry as SVG path data.</summary>
    public static class SvgPathWriter
    {
        /// <summary>Writes absolute SVG commands using invariant coordinates.</summary>
        public static string Write(VectorPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            StringBuilder? result = new StringBuilder();
            foreach (VectorPath.PathElement element in path.Elements)
            {
                result.Append(element.Kind switch
                {
                    0 => 'M',
                    1 => 'L',
                    2 => 'Q',
                    3 => 'C',
                    _ => 'Z'
                });
                if (element.Kind == 4)
                {
                    continue;
                }

                Point(element.First.X, element.First.Y);
                if (element.Kind >= 2)
                {
                    Point(element.Second.X, element.Second.Y);
                }

                if (element.Kind == 3)
                {
                    Point(element.Third.X, element.Third.Y);
                }
            }

            return result.ToString();
            void Point(float x, float y) => result.Append(x.ToString("G9", CultureInfo.InvariantCulture)).Append(' ').Append(y.ToString("G9", CultureInfo.InvariantCulture)).Append(' ');
        }
    }
}
