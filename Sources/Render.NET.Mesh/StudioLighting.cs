using System;
using System.Numerics;

namespace Render.NET.Mesh
{
    internal class StudioLighting
    {
        internal static readonly Vector3 KeyDirection = Vector3.Normalize(new Vector3(-3, -4, 6));
        private static readonly Vector3 FillDirection = Vector3.Normalize(new Vector3(4, -1, 2));
        private static readonly Vector3 RimDirection = Vector3.Normalize(new Vector3(1, 4, 3));
        private readonly MeshRenderOptions _options;
        private readonly Vector3 _baseColor;
        private readonly Vector3 _fresnel;
        private readonly RasterCamera? _shadowCamera;
        private readonly float[]? _shadowDepths;

        internal StudioLighting(MeshRenderOptions options, RasterCamera? shadowCamera, float[]? shadowDepths)
        {
            _options = options;
            _baseColor = new Vector3(ToLinear(options.BaseColor.X), ToLinear(options.BaseColor.Y), ToLinear(options.BaseColor.Z));
            _fresnel = Vector3.Lerp(new Vector3(0.04f), _baseColor, options.Metallic);
            _shadowCamera = shadowCamera;
            _shadowDepths = shadowDepths;
        }

        internal Vector3 Shade(Vector3 point, Vector3 normal, Vector3 view, float occlusion)
        {
            float hemisphere = normal.Z * 0.5f + 0.5f;
            Vector3 ambient = Vector3.Lerp(new Vector3(0.12f, 0.14f, 0.19f), new Vector3(0.38f, 0.43f, 0.55f), hemisphere);
            Vector3 color = _baseColor * ambient * occlusion * (1 - 0.65f * _options.Metallic);
            color += EvaluateLight(normal, view, KeyDirection) * new Vector3(4.8f, 4.5f, 4.1f) * Shadow(point, normal);
            color += EvaluateLight(normal, view, FillDirection) * new Vector3(0.85f, 1.05f, 1.5f);
            color += EvaluateLight(normal, view, RimDirection) * new Vector3(2.3f, 2.55f, 3.1f);
            color *= _options.Exposure;
            return new Vector3(ToneMap(color.X), ToneMap(color.Y), ToneMap(color.Z));
        }

        private Vector3 EvaluateLight(Vector3 normal, Vector3 view, Vector3 light)
        {
            float nl = MathF.Max(Vector3.Dot(normal, light), 0);
            if (nl == 0)
            {
                return Vector3.Zero;
            }

            Vector3 halfVector = Vector3.Normalize(light + view);
            float nv = MathF.Max(Vector3.Dot(normal, view), 0.001f);
            float nh = MathF.Max(Vector3.Dot(normal, halfVector), 0);
            float vh = MathF.Max(Vector3.Dot(view, halfVector), 0);
            float alpha = _options.Roughness * _options.Roughness;
            float alphaSquared = alpha * alpha;
            float denominator = nh * nh * (alphaSquared - 1) + 1;
            float distribution = alphaSquared / (MathF.PI * denominator * denominator);
            float k = (_options.Roughness + 1) * (_options.Roughness + 1) / 8;
            float geometry = nv / (nv * (1 - k) + k) * nl / (nl * (1 - k) + k);
            Vector3 fresnel = _fresnel + (Vector3.One - _fresnel) * MathF.Pow(1 - vh, 5);
            Vector3 specular = fresnel * (distribution * geometry / (4 * nv * nl + 0.0001f));
            Vector3 diffuse = (Vector3.One - fresnel) * (1 - _options.Metallic) * _baseColor / MathF.PI;
            return (diffuse + specular) * nl;
        }

        private float Shadow(Vector3 point, Vector3 normal)
        {
            if (_shadowCamera == null || _shadowDepths == null)
            {
                return 1;
            }

            Vector3 projected = _shadowCamera.Project(point);
            int centerX = (int)MathF.Floor(projected.X);
            int centerY = (int)MathF.Floor(projected.Y);
            float bias = 0.0025f + 0.004f * (1 - MathF.Max(Vector3.Dot(normal, KeyDirection), 0));
            float step = MathF.Max(1, _shadowCamera.Scale * _options.ShadowSoftness);
            float nl = MathF.Max(Vector3.Dot(normal, KeyDirection), 0.15f);
            float depthDx = Vector3.Dot(_shadowCamera.Unproject(projected.X + 1, projected.Y, projected.Z) - point, normal) / nl;
            float depthDy = Vector3.Dot(_shadowCamera.Unproject(projected.X, projected.Y + 1, projected.Z) - point, normal) / nl;
            float visible = 0;
            float total = 0;
            for (int dy = -2; dy <= 2; dy++)
            {
                int y = centerY + (int)MathF.Round(dy * step);
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = centerX + (int)MathF.Round(dx * step);
                    float weight = (3 - Math.Abs(dx)) * (3 - Math.Abs(dy));
                    total += weight;
                    float receiverDepth = projected.Z + (x + 0.5f - projected.X) * depthDx + (y + 0.5f - projected.Y) * depthDy;
                    if ((uint)x >= (uint)_shadowCamera.Width || (uint)y >= (uint)_shadowCamera.Height ||
                        receiverDepth - bias <= _shadowDepths[y * _shadowCamera.Width + x])
                    {
                        visible += weight;
                    }
                }
            }

            return visible / total;
        }

        internal static float ToLinear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

        internal static byte ToSrgbByte(float value)
        {
            value = Math.Clamp(value, 0, 1);
            float srgb = value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - 0.055f;
            return (byte)Math.Clamp((int)MathF.Round(srgb * 255), 0, 255);
        }

        private static float ToneMap(float value) => Math.Clamp(value * (2.51f * value + 0.03f) / (value * (2.43f * value + 0.59f) + 0.14f), 0, 1);
    }
}
