using System;

namespace Render.NET.Jpeg2000
{
    /// <summary>The enumerated colour space in a JP2 container, when present.</summary>
    public enum Jpeg2000ColorSpace
    {
        /// <summary>The caller supplies the colour interpretation.</summary>
        Unspecified = 0,
        /// <summary>Standard RGB.</summary>
        Srgb = 16,
        /// <summary>Greyscale.</summary>
        Gray = 17,
        /// <summary>Standard YCbCr.</summary>
        Sycc = 18
    }
}
