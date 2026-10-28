using System;

namespace Render.NET.Jpeg2000
{

    /// <summary>A decoded image retaining component precision, signs and sampling.</summary>
    public class Jpeg2000Image
    {
        private readonly Jpeg2000Component[] _components;
        internal int X0 { get; }
        internal int Y0 { get; }
        /// <summary>Gets the reference-grid image width.</summary>
        public int Width { get; }
        /// <summary>Gets the reference-grid image height.</summary>
        public int Height { get; }
        /// <summary>Gets the component count.</summary>
        public int ComponentCount => _components.Length;
        /// <summary>Gets the JP2 colour interpretation, or Unspecified for raw codestreams.</summary>
        public Jpeg2000ColorSpace ColorSpace { get; }
        /// <summary>Gets the opacity component index, or -1.</summary>
        public int AlphaComponent { get; }
        /// <summary>Gets whether JP2 declares premultiplied colour components.</summary>
        public bool PremultipliedAlpha { get; }
        /// <summary>Gets whether JP2 supplies an ICC profile; profile conversion is left to the caller.</summary>
        public bool HasIccProfile { get; }

        internal Jpeg2000Image(int x0, int y0, int width, int height, Jpeg2000Component[] components, ContainerInfo info)
        {
            X0 = x0;
            Y0 = y0;
            Width = width;
            Height = height;
            _components = components;
            ColorSpace = info.ColorSpace;
            AlphaComponent = info.Alpha;
            PremultipliedAlpha = info.Premultiplied;
            HasIccProfile = info.Profile;
        }

        /// <summary>Gets a component without reducing its precision.</summary>
        public Jpeg2000Component GetComponent(int component) => _components[component];
        /// <summary>Gets a sample at image coordinates using nearest-neighbour sampling for subsampled components.</summary>
        public int GetSample(int x, int y, int component)
        {
            if ((uint)x >= Width || (uint)y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            Jpeg2000Component? c = _components[component];
            int cx = Math.Clamp(((X0 + x) / c.HorizontalSampling) - c.X0, 0, c.Width - 1);
            int cy = Math.Clamp(((Y0 + y) / c.VerticalSampling) - c.Y0, 0, c.Height - 1);
            return c.Values[(cy * c.Width) + cx];
        }
    }
}
