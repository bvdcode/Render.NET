using System;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfStreamProcessor
    {
        private long _shadingBytes;
        public override void PaintShading(NameToken shadingName)
        {
            Shading shading = ResourceStore.GetShading(shadingName);
            var state = GetCurrentState();
            CheckState(state);
            IPdfDrawCommand? command = CreateShading(shading, Matrix(CurrentTransformationMatrix), state.AlphaConstantNonStroking, Blend(state.BlendMode));
            if (command != null)
            {
                AddPaint(command, state);
            }
        }

        private IPdfDrawCommand? CreateShading(Shading shading, Matrix3x2 placement, double opacity, ColorBlendMode blend, bool paintBackground = false)
        {
            Rgba32? background = paintBackground && shading.Background != null
                ? Color(shading.ColorSpace.GetColor(shading.Background), opacity) : null;
            if (shading is FunctionBasedShading function)
            {
                return new FunctionShadingCommand(function, placement, opacity, blend, background);
            }

            if (shading is not AxialShading && shading is not RadialShading)
            {
                PdfShadingMesh mesh = PdfShadingMesh.Read(shading, _limits, _cancellationToken);
                _shadingBytes = checked(_shadingBytes + mesh.StorageBytes);
                if (_shadingBytes > _limits.MaximumShadingBytes)
                {
                    throw new System.IO.InvalidDataException("PDF page exceeds MaximumShadingBytes.");
                }

                _pathCommands = checked(_pathCommands + (mesh.Triangles.Count * 3) + (mesh.Patches.Count * 16));
                if (_pathCommands > _limits.MaximumPathCommands)
                {
                    throw new System.IO.InvalidDataException("PDF page exceeds MaximumPathCommands including mesh shadings.");
                }

                return new MeshShadingCommand(mesh, placement, opacity, blend, background);
            }

            double[] domain = shading is AxialShading axial ? axial.Domain : ((RadialShading)shading).Domain;
            var colors = new Rgba32[2049];
            for (int i = 0; i < colors.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                double t = domain[0] + (((domain[1] - domain[0]) * i) / (colors.Length - 1));
                colors[i] = Color(shading.ColorSpace.GetColor(shading.Eval(t)), opacity);
            }

            return new ShadingCommand(shading, colors, placement, blend, background);
        }

        private void AddFill(VectorPath path, FillRule rule, UglyToad.PdfPig.Graphics.CurrentGraphicsState state)
        {
            if (state.CurrentNonStrokingColor?.ColorSpace == ColorSpace.Pattern)
            {
                AddPatternPaint(path, rule, state, false);
            }
            else
            {
                AddPaint(new FillCommand(path, Color(state.CurrentNonStrokingColor, state.AlphaConstantNonStroking), rule, Blend(state.BlendMode)), state);
            }
        }
    }

    internal sealed record PatternPaintCommand(VectorPath Path, FillRule Rule, IPdfDrawCommand Pattern,
        StrokeStyle? Stroke = null, Matrix3x2 Pen = default) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            canvas.Save();
            try
            {
                if (Stroke == null)
                {
                    canvas.ClipPath(Path, device, Rule, token);
                }
                else if (Matrix3x2.Invert(Pen, out Matrix3x2 inverse))
                {
                    canvas.ClipStrokePath(Path.Transform(inverse), Stroke, Pen * device, token);
                }
                else
                {
                    return;
                }

                Pattern.Draw(canvas, device, token);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }

    internal sealed record ShadingCommand(Shading Shading, Rgba32[] Colors, Matrix3x2 Placement, ColorBlendMode Blend, Rgba32? Background = null) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
        {
            canvas.Save();
            try
            {
                if (Shading.BBox is { } box)
                {
                    canvas.ClipPath(new VectorPath().Rectangle((float)box.Left, (float)box.Bottom, (float)box.Width, (float)box.Height),
                        Placement * device, cancellationToken: cancellationToken);
                }

                canvas.DrawShader(Sample, Contains, Placement * device, Blend, cancellationToken);
            }
            finally
            {
                canvas.Restore();
            }
        }

        private Rgba32 Sample(Vector2 point)
            => TryParameter(point, out double t) ? Colors[(int)Math.Round(Math.Clamp(t, 0, 1) * (Colors.Length - 1))] : Background ?? default;

        private bool Contains(Vector2 point) => Background.HasValue || TryParameter(point, out _);

        private bool TryParameter(Vector2 point, out double t)
        {
            t = double.NaN;
            bool[] extend;
            if (Shading is AxialShading axial)
            {
                double dx = axial.Coords[2] - axial.Coords[0];
                double dy = axial.Coords[3] - axial.Coords[1];
                double length = (dx * dx) + (dy * dy);
                if (length == 0)
                {
                    return false;
                }

                t = (((point.X - axial.Coords[0]) * dx) + ((point.Y - axial.Coords[1]) * dy)) / length;
                extend = axial.Extend;
            }
            else
            {
                var radial = (RadialShading)Shading;
                t = RadialParameter(radial, point);
                extend = radial.Extend;
            }

            return Valid(t, extend);
        }

        private static bool Valid(double t, bool[] extend)
            => double.IsFinite(t) && (t >= 0 || extend[0]) && (t <= 1 || extend[1]);

        private static double RadialParameter(RadialShading shading, Vector2 point)
        {
            double[] c = shading.Coords;
            double dx = c[3] - c[0];
            double dy = c[4] - c[1];
            double dr = c[5] - c[2];
            double x = point.X - c[0];
            double y = point.Y - c[1];
            double a = ((dx * dx) + (dy * dy)) - (dr * dr);
            double b = -2 * ((x * dx) + (y * dy) + (c[2] * dr));
            double constant = ((x * x) + (y * y)) - (c[2] * c[2]);
            if (Math.Abs(a) < 1e-12)
            {
                double t = -constant / b;
                return c[2] + (t * dr) >= 0 ? t : double.NaN;
            }

            double discriminant = (b * b) - (4 * a * constant);
            if (discriminant < 0)
            {
                return double.NaN;
            }

            double first = (-b - Math.Sqrt(discriminant)) / (2 * a);
            double second = (-b + Math.Sqrt(discriminant)) / (2 * a);
            double larger = Math.Max(first, second);
            double smaller = Math.Min(first, second);
            return Valid(larger, shading.Extend) && c[2] + (larger * dr) >= 0 ? larger
                : Valid(smaller, shading.Extend) && c[2] + (smaller * dr) >= 0 ? smaller : double.NaN;
        }
    }
}
