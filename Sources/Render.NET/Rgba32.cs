namespace Render.NET
{
    /// <summary>An sRGB color with straight alpha, stored as four eight-bit channels.</summary>
    /// <param name="R">Red channel.</param>
    /// <param name="G">Green channel.</param>
    /// <param name="B">Blue channel.</param>
    /// <param name="A">Alpha channel; 255 is opaque.</param>
    public readonly record struct Rgba32(byte R, byte G, byte B, byte A = 255);
}
