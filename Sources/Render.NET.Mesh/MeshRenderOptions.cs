using System;
using System.Numerics;

namespace Render.NET.Mesh
{
    /// <summary>Controls a studio-lit, orthographic mesh preview. STL coordinates are treated as Z-up.</summary>
    public class MeshRenderOptions
    {
        /// <summary>Gets the output width in pixels.</summary>
        public int Width { get; init; } = 512;

        /// <summary>Gets the output height in pixels.</summary>
        public int Height { get; init; } = 512;

        /// <summary>Gets the number of samples along each pixel axis, from one to four.</summary>
        public int Supersampling { get; init; } = 2;

        /// <summary>Gets the direction from the model toward the camera.</summary>
        public Vector3 CameraDirection { get; init; } = new(1.6f, -2.4f, 1.5f);

        /// <summary>Gets the camera's preferred upward direction.</summary>
        public Vector3 CameraUp { get; init; } = Vector3.UnitZ;

        /// <summary>Gets the fraction of each image dimension reserved for padding on each side.</summary>
        public float Padding { get; init; } = 0.08f;

        /// <summary>Gets the opaque surface color in sRGB, with components between zero and one.</summary>
        public Vector3 BaseColor { get; init; } = new(0.55f, 0.65f, 0.88f);

        /// <summary>Gets the surface roughness, between 0.08 and one.</summary>
        public float Roughness { get; init; } = 0.32f;

        /// <summary>Gets the metallic fraction, between zero and one.</summary>
        public float Metallic { get; init; } = 0.15f;

        /// <summary>Gets the exposure multiplier applied before tone mapping.</summary>
        public float Exposure { get; init; } = 1;

        /// <summary>Gets the background color. The default is transparent.</summary>
        public Rgba32 Background { get; init; }

        /// <summary>Gets whether adjacent STL faces are smoothed while retaining sharp creases.</summary>
        public bool SmoothNormals { get; init; } = true;

        /// <summary>Gets the maximum angle in degrees between faces contributing to a smooth normal.</summary>
        public float CreaseAngle { get; init; } = 45;

        /// <summary>Gets whether the main light casts filtered shadows onto the model.</summary>
        public bool Shadows { get; init; } = true;

        /// <summary>Gets the square shadow map size, between 64 and 2048 pixels.</summary>
        public int ShadowMapSize { get; init; } = 1024;

        /// <summary>Gets the shadow filter radius as a fraction of the model's longest dimension, between zero and 0.1.</summary>
        public float ShadowSoftness { get; init; } = 0.02f;

        /// <summary>Gets whether local creases receive screen-space ambient occlusion.</summary>
        public bool AmbientOcclusion { get; init; } = true;

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
            if (Supersampling < 1 || Supersampling > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(Supersampling));
            }

            if ((long)Width * Height > 16_777_216 / (Supersampling * Supersampling))
            {
                throw new ArgumentOutOfRangeException(nameof(Width), "The internal raster may contain at most 16,777,216 samples.");
            }

            ValidateDirection(CameraDirection, nameof(CameraDirection));
            ValidateDirection(CameraUp, nameof(CameraUp));
            if (Vector3.Cross(Vector3.Normalize(CameraDirection), Vector3.Normalize(CameraUp)).LengthSquared() < 0.000001f)
            {
                throw new ArgumentException("The camera direction and up vector must not be parallel.", nameof(CameraUp));
            }

            ValidateRange(Padding, 0, 0.4f, nameof(Padding));
            ValidateRange(BaseColor.X, 0, 1, nameof(BaseColor));
            ValidateRange(BaseColor.Y, 0, 1, nameof(BaseColor));
            ValidateRange(BaseColor.Z, 0, 1, nameof(BaseColor));
            ValidateRange(Roughness, 0.08f, 1, nameof(Roughness));
            ValidateRange(Metallic, 0, 1, nameof(Metallic));
            ValidateRange(Exposure, 0.01f, 100, nameof(Exposure));
            ValidateRange(CreaseAngle, 0, 180, nameof(CreaseAngle));
            ValidateRange(ShadowSoftness, 0, 0.1f, nameof(ShadowSoftness));
            if (ShadowMapSize < 64 || ShadowMapSize > 2048)
            {
                throw new ArgumentOutOfRangeException(nameof(ShadowMapSize));
            }
        }

        private static void ValidateDirection(Vector3 value, string name)
        {
            float length = value.LengthSquared();
            if (!float.IsFinite(length) || length < 0.00000001f)
            {
                throw new ArgumentException("A finite, nonzero direction is required.", name);
            }
        }

        private static void ValidateRange(float value, float minimum, float maximum, string name)
        {
            if (!float.IsFinite(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name, $"The value must be between {minimum} and {maximum}.");
            }
        }
    }
}
