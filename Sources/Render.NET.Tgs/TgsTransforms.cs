using System;
using System.Numerics;
using System.Text.Json;

namespace Render.NET.Tgs
{
    internal static class TgsTransforms
    {
        internal static Matrix3x2 Transform(JsonElement value, double frame, double orientation = 0)
        {
            Vector2 anchor = TgsValues.Point(TgsJson.Get(value, "a"), frame), position = TgsValues.Point(TgsJson.Get(value, "p"), frame);
            Vector2 scale = TgsValues.Point(TgsJson.Get(value, "s"), frame, 100, 100) / 100;
            double rotation = TgsValues.Scalar(TgsJson.Get(value, "r"), frame) + orientation;
            float skew = (float)((TgsValues.Scalar(TgsJson.Get(value, "sk"), frame) * Math.PI) / 180);
            float axis = (float)((TgsValues.Scalar(TgsJson.Get(value, "sa"), frame) * Math.PI) / 180);
            return Matrix3x2.CreateTranslation(-anchor) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(-axis) * new Matrix3x2(1, 0, -MathF.Tan(skew), 1, 0, 0) * Matrix3x2.CreateRotation(axis) * Matrix3x2.CreateRotation((float)((rotation * Math.PI) / 180)) * Matrix3x2.CreateTranslation(position);
        }
    }
}
