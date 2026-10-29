using System.Numerics;

namespace Render.NET.Svg
{
    internal readonly record struct SvgNoiseOctave(Vector2 Frequency, float Amplitude, int Width, int Height, int OriginX, int OriginY);
}
