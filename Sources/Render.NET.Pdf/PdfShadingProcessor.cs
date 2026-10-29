using System;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal partial class PdfStreamProcessor
    {
        private long _shadingBytes;
        public override void PaintShading(NameToken shadingName)
        {
            Shading shading = ResourceStore.GetShading(shadingName);
            UglyToad.PdfPig.Graphics.CurrentGraphicsState? state = GetCurrentState();
            IPdfDrawCommand? command = CreateShading(shading, Matrix(CurrentTransformationMatrix), state.AlphaConstantNonStroking, Blend(state.BlendMode));
            if (command is not null)
            {
                AddPaint(command, state);
            }
        }

        private IPdfDrawCommand? CreateShading(Shading shading, Matrix3x2 placement, double opacity, ColorBlendMode blend, bool paintBackground = false)
        {
            Rgba32? background = paintBackground && shading.Background is not null
                ? Color(shading.ColorSpace.GetColor(shading.Background), opacity) : null;
            if (shading is FunctionBasedShading function)
            {
                return new FunctionShadingCommand(function, placement, opacity, blend, background);
            }

            if (shading is not AxialShading && shading is not RadialShading)
            {
                PdfShadingMesh mesh = ReadShadingMesh(shading);
                return new MeshShadingCommand(mesh, placement, opacity, blend, background);
            }

            double[] domain = shading is AxialShading axial ? axial.Domain : ((RadialShading)shading).Domain;
            Rgba32[]? colors = new Rgba32[2049];
            for (int i = 0; i < colors.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                double t = domain[0] + (((domain[1] - domain[0]) * i) / (colors.Length - 1));
                colors[i] = Color(shading.ColorSpace.GetColor(shading.Eval(t)), opacity);
            }

            return new ShadingCommand(shading, colors, placement, blend, background);
        }

        private PdfShadingMesh ReadShadingMesh(Shading shading)
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

            return mesh;
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
}
