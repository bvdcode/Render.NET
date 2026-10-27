using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfStreamProcessor
    {
        private readonly Dictionary<SoftMask, TransformationMatrix> _maskMatrices = new();
        private readonly Dictionary<SoftMask, MaskPlan> _masks = new();
        private int _captureDepth;

        public override void SetNamedGraphicsState(NameToken stateName)
        {
            CheckPatternGraphicsState(stateName);
            var savedIntent = GetCurrentState().RenderingIntent;
            base.SetNamedGraphicsState(stateName);
            var state = GetCurrentState();
            if (_uncoloredType3)
            {
                state.RenderingIntent = savedIntent;
            }

            if (state.SoftMask != null && !_maskMatrices.ContainsKey(state.SoftMask))
            {
                _maskMatrices.Add(state.SoftMask, CurrentTransformationMatrix);
            }
        }

        private void AddPaint(IPdfDrawCommand command, CurrentGraphicsState state)
        {
            Commands.Add(state.SoftMask == null ? command : new MaskedCommand(command, GetMask(state.SoftMask)));
        }

        private MaskPlan GetMask(SoftMask mask)
        {
            if (_masks.TryGetValue(mask, out MaskPlan? plan))
            {
                return plan;
            }

            if (++_captureDepth > 8)
            {
                throw new NotSupportedException("Soft mask nesting exceeds eight.");
            }

            var savedStack = GraphicsStack;
            bool savedType3 = _uncoloredType3;
            int start = Commands.Count;
            try
            {
                var state = GetCurrentState().DeepClone();
                state.SoftMask = null;
                state.AlphaConstantNonStroking = 1;
                state.AlphaConstantStroking = 1;
                state.BlendMode = UglyToad.PdfPig.Graphics.Core.BlendMode.Normal;
                state.CurrentTransformationMatrix = _maskMatrices[mask];
                GraphicsStack = new Stack<CurrentGraphicsState>();
                GraphicsStack.Push(state);
                _uncoloredType3 = false;
                ProcessFormXObject(mask.TransparencyGroup, NameToken.Create("RenderSoftMask"));
                var commands = Commands.GetRange(start, Commands.Count - start).ToArray();
                Rgba32 background = MaskBackground(mask);
                var transfer = new byte[256];
                for (int i = 0; i < transfer.Length; i++)
                {
                    double value = mask.TransferFunction?.Eval(new[] { i / 255d })[0] ?? i / 255d;
                    transfer[i] = Channel(value);
                }

                plan = new MaskPlan(commands, mask.Subtype == SoftMaskType.Luminosity, background, transfer);
                _masks.Add(mask, plan);
                return plan;
            }
            finally
            {
                Commands.RemoveRange(start, Commands.Count - start);
                GraphicsStack = savedStack;
                _uncoloredType3 = savedType3;
                _captureDepth--;
            }
        }

        private Rgba32 MaskBackground(SoftMask mask)
        {
            if (mask.Subtype != SoftMaskType.Luminosity || mask.BC == null)
            {
                return new Rgba32(0, 0, 0);
            }

            double[] c = mask.BC;
            if (c.Length == 1)
            {
                return new Rgba32(Channel(c[0]), Channel(c[0]), Channel(c[0]));
            }

            if (c.Length == 3)
            {
                return new Rgba32(Channel(c[0]), Channel(c[1]), Channel(c[2]));
            }

            Warn("Soft mask backdrop color space");
            return new Rgba32(0, 0, 0);
        }

        protected override void ProcessFormXObject(StreamToken formStream, NameToken name)
        {
            VectorPath savedPath = _path;
            PdfPoint savedStart = _start;
            PdfPoint savedPosition = CurrentPosition;
            FillRule? savedClip = _pendingClip;
            _path = new VectorPath();
            _pendingClip = null;
            var savedText = TextMatrices.TextMatrix;
            var savedTextLine = TextMatrices.TextLineMatrix;
            var savedExtendedFont = ActiveExtendedGraphicsStateFont;
            VectorPath? savedTextClip = _textClip;
            _textClip = null;
            int start = Commands.Count;
            var state = GetCurrentState().DeepClone();
            bool grouped = Resolve(formStream.StreamDictionary, NameToken.Group, out DictionaryToken group);
            try
            {
                base.ProcessFormXObject(formStream, name);
                if (grouped)
                {
                    if (group.TryGet(NameToken.Cs, out IToken colorSpace)
                        && colorSpace is not NameToken { Data: "DeviceRGB" or "DeviceGray" })
                    {
                        Warn("Transparency group color-space override");
                    }

                    bool isolated = Resolve(group, NameToken.I, out BooleanToken isolation) && isolation.Data;
                    bool knocksOut = Resolve(group, NameToken.K, out BooleanToken knockout) && knockout.Data;

                    var commands = Commands.GetRange(start, Commands.Count - start).ToArray();
                    Commands.RemoveRange(start, Commands.Count - start);
                    AddPaint(new GroupCommand(commands, (float)state.AlphaConstantNonStroking, Blend(state.BlendMode), isolated, knocksOut), state);
                }
            }
            finally
            {
                _path = savedPath;
                _start = savedStart;
                CurrentPosition = savedPosition;
                _pendingClip = savedClip;
                TextMatrices.TextMatrix = savedText;
                TextMatrices.TextLineMatrix = savedTextLine;
                ActiveExtendedGraphicsStateFont = savedExtendedFont;
                _textClip = savedTextClip;
            }
        }
    }

    internal sealed record GroupCommand(IPdfDrawCommand[] Commands, float Opacity, ColorBlendMode Blend, bool Isolated, bool Knockout = false) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken cancellationToken)
            => canvas.DrawLayer(layer => DrawCommands(Commands, layer, device, cancellationToken), Knockout, Opacity, Blend, Isolated, cancellationToken: cancellationToken);

        internal static void DrawCommands(IPdfDrawCommand[] commands, RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            foreach (IPdfDrawCommand command in commands)
            {
                token.ThrowIfCancellationRequested();
                if (command is StateCommand or ClipCommand)
                {
                    command.Draw(canvas, device, token);
                }
                else
                {
                    canvas.DrawObject(element => command.Draw(element, device, token), token);
                }
            }
        }
    }

    internal sealed record MaskPlan(IPdfDrawCommand[] Commands, bool Luminosity, Rgba32 Background, byte[] Transfer)
    {
        internal PixelBuffer Render(RasterCanvas parent, Matrix3x2 device, CancellationToken token)
        {
            var image = new PixelBuffer(parent.Buffer.Width, parent.Buffer.Height);
            if (Luminosity)
            {
                image.Clear(Background);
            }

            GroupCommand.DrawCommands(Commands, new RasterCanvas(image), device, token);
            for (int y = 0; y < image.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < image.Width; x++)
                {
                    Rgba32 c = image[x, y];
                    double alpha = c.A / 255d;
                    double luminosity = (0.3 * ((c.R * alpha) + (Background.R * (1 - alpha))))
                        + (0.59 * ((c.G * alpha) + (Background.G * (1 - alpha))))
                        + (0.11 * ((c.B * alpha) + (Background.B * (1 - alpha))));
                    int value = Luminosity ? Math.Clamp((int)Math.Round(luminosity), 0, 255) : c.A;
                    image[x, y] = new Rgba32(0, 0, 0, Transfer[value]);
                }
            }

            return image;
        }
    }

    internal sealed record MaskedCommand(IPdfDrawCommand Command, MaskPlan Mask) : IPdfDrawCommand
    {
        public void Draw(RasterCanvas canvas, Matrix3x2 device, CancellationToken token)
        {
            PixelBuffer mask = Mask.Render(canvas, device, token);
            canvas.DrawLayer(layer => Command.Draw(layer, device, token), isolated: false, alphaMask: mask, cancellationToken: token);
        }
    }
}
