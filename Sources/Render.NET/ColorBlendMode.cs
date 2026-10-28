using System;
using System.Numerics;

namespace Render.NET
{
    /// <summary>PDF color blend modes, evaluated in sRGB.</summary>
    public enum ColorBlendMode
    {
        /// <summary>Source-over alpha compositing.</summary>
        Normal,
        /// <summary>Multiply source and backdrop.</summary>
        Multiply,
        /// <summary>Invert, multiply, and invert the result.</summary>
        Screen,
        /// <summary>Keep the darker color.</summary>
        Darken,
        /// <summary>Keep the lighter color.</summary>
        Lighten,
        /// <summary>Brighten the backdrop.</summary>
        ColorDodge,
        /// <summary>Darken the backdrop.</summary>
        ColorBurn,
        /// <summary>Multiply or screen according to the source.</summary>
        HardLight,
        /// <summary>Apply a soft lighting curve.</summary>
        SoftLight,
        /// <summary>Multiply or screen according to the backdrop.</summary>
        Overlay,
        /// <summary>Absolute channel difference.</summary>
        Difference,
        /// <summary>A lower-contrast difference.</summary>
        Exclusion,
        /// <summary>Source hue with backdrop saturation and luminosity.</summary>
        Hue,
        /// <summary>Source saturation with backdrop hue and luminosity.</summary>
        Saturation,
        /// <summary>Source hue and saturation with backdrop luminosity.</summary>
        Color,
        /// <summary>Source luminosity with backdrop hue and saturation.</summary>
        Luminosity,
        /// <summary>Add premultiplied source and backdrop, clamping color and coverage.</summary>
        Add,
        /// <summary>Threshold vivid-light color mixing.</summary>
        HardMix
    }
}
