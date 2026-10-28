using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal partial class PdfStreamProcessor
    {
        private readonly Dictionary<SoftMask, TransformationMatrix> _maskMatrices = new();
        private readonly Dictionary<SoftMask, MaskPlan> _masks = new();
        private int _captureDepth;

        public override void SetNamedGraphicsState(NameToken stateName)
        {
            CheckPatternGraphicsState(stateName);
            UglyToad.PdfPig.Graphics.Core.RenderingIntent savedIntent = GetCurrentState().RenderingIntent;
            base.SetNamedGraphicsState(stateName);
            CurrentGraphicsState? state = GetCurrentState();
            if (_uncoloredType3)
            {
                state.RenderingIntent = savedIntent;
            }

            if (state.SoftMask is not null && !_maskMatrices.ContainsKey(state.SoftMask))
            {
                _maskMatrices.Add(state.SoftMask, CurrentTransformationMatrix);
            }
        }

        private void AddPaint(IPdfDrawCommand command, CurrentGraphicsState state)
        {
            Commands.Add(state.SoftMask is null ? command : new MaskedCommand(command, GetMask(state.SoftMask)));
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

            Stack<CurrentGraphicsState>? savedStack = GraphicsStack;
            bool savedType3 = _uncoloredType3;
            int start = Commands.Count;
            try
            {
                CurrentGraphicsState? state = GetCurrentState().DeepClone();
                state.SoftMask = null;
                state.AlphaConstantNonStroking = 1;
                state.AlphaConstantStroking = 1;
                state.BlendMode = UglyToad.PdfPig.Graphics.Core.BlendMode.Normal;
                state.CurrentTransformationMatrix = _maskMatrices[mask];
                GraphicsStack = new Stack<CurrentGraphicsState>();
                GraphicsStack.Push(state);
                _uncoloredType3 = false;
                ProcessFormXObject(mask.TransparencyGroup, NameToken.Create("RenderSoftMask"));
                IPdfDrawCommand[]? commands = Commands.GetRange(start, Commands.Count - start).ToArray();
                Rgba32 background = MaskBackground(mask);
                byte[]? transfer = new byte[256];
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
            if (mask.Subtype != SoftMaskType.Luminosity || mask.BC is null)
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
            TransformationMatrix savedText = TextMatrices.TextMatrix;
            TransformationMatrix savedTextLine = TextMatrices.TextLineMatrix;
            UglyToad.PdfPig.PdfFonts.IFont? savedExtendedFont = ActiveExtendedGraphicsStateFont;
            VectorPath? savedTextClip = _textClip;
            _textClip = null;
            int start = Commands.Count;
            CurrentGraphicsState? state = GetCurrentState().DeepClone();
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

                    IPdfDrawCommand[]? commands = Commands.GetRange(start, Commands.Count - start).ToArray();
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
}
