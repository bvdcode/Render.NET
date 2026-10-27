using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Geometry;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Render.NET.Pdf
{
    internal sealed partial class PdfStreamProcessor : BaseStreamProcessor<int>
    {
        private readonly PdfReaderOptions _limits;
        private readonly CancellationToken _cancellationToken;
        private readonly PdfImageDecoder _images;
        private VectorPath _path = new VectorPath();
        private PdfPoint _start;
        private FillRule? _pendingClip;
        private int _operations;
        private int _pathCommands;
        private TransformationMatrix _patternMatrix;

        internal List<IPdfDrawCommand> Commands { get; } = new List<IPdfDrawCommand>();
        internal List<string> Unsupported { get; } = new List<string>();

        internal PdfStreamProcessor(int number, IResourceStore resources, IPdfTokenScanner scanner, IPageContentParser parser,
            ILookupFilterProvider filters, CropBox crop, UserSpaceUnit units, PageRotationDegrees rotation,
            TransformationMatrix initial, RenderParsingOptions options)
            : base(number, resources, scanner, parser, filters, crop, units, rotation, initial, options)
        {
            _limits = options.Limits;
            _cancellationToken = options.CancellationToken;
            _images = new PdfImageDecoder(_limits, filters, scanner, _cancellationToken);
        }

        public override int Process(int pageNumberCurrent, IReadOnlyList<IGraphicsStateOperation> operations)
        {
            ProcessOperations(operations);
            return Commands.Count;
        }

        protected override void ProcessOperations(IReadOnlyList<IGraphicsStateOperation> operations)
        {
            TransformationMatrix savedPatternMatrix = _patternMatrix;
            _patternMatrix = CurrentTransformationMatrix;
            try
            {
                foreach (IGraphicsStateOperation operation in operations)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (++_operations > _limits.MaximumOperations)
                    {
                        throw new InvalidDataException("PDF page exceeds MaximumOperations.");
                    }

                    operation.Run(this);
                    if (_path.CommandCount > _limits.MaximumPathCommands)
                    {
                        throw new InvalidDataException("PDF path exceeds MaximumPathCommands.");
                    }
                }
            }
            finally
            {
                _patternMatrix = savedPatternMatrix;
            }
        }

        public override void PushState()
        {
            if (StackSize >= 64)
            {
                throw new InvalidDataException("PDF graphics state nesting exceeds 64.");
            }

            base.PushState();
            Commands.Add(new StateCommand(true));
        }

        public override void PopState()
        {
            base.PopState();
            Commands.Add(new StateCommand(false));
        }

        public override void BeginSubpath()
        {
        }

        public override void MoveTo(double x, double y)
        {
            CurrentPosition = CurrentTransformationMatrix.Transform(new PdfPoint(x, y));
            _start = CurrentPosition;
            _path.MoveTo((float)CurrentPosition.X, (float)CurrentPosition.Y);
        }

        public override void LineTo(double x, double y)
        {
            CurrentPosition = CurrentTransformationMatrix.Transform(new PdfPoint(x, y));
            _path.LineTo((float)CurrentPosition.X, (float)CurrentPosition.Y);
        }

        public override PdfPoint? CloseSubpath()
        {
            _path.Close();
            CurrentPosition = _start;
            return _start;
        }

        public override void BezierCurveTo(double x2, double y2, double x3, double y3)
        {
            PdfPoint second = CurrentTransformationMatrix.Transform(new PdfPoint(x2, y2));
            PdfPoint end = CurrentTransformationMatrix.Transform(new PdfPoint(x3, y3));
            _path.CubicTo((float)CurrentPosition.X, (float)CurrentPosition.Y, (float)second.X, (float)second.Y, (float)end.X, (float)end.Y);
            CurrentPosition = end;
        }

        public override void BezierCurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            PdfPoint first = CurrentTransformationMatrix.Transform(new PdfPoint(x1, y1));
            PdfPoint second = CurrentTransformationMatrix.Transform(new PdfPoint(x2, y2));
            PdfPoint end = CurrentTransformationMatrix.Transform(new PdfPoint(x3, y3));
            _path.CubicTo((float)first.X, (float)first.Y, (float)second.X, (float)second.Y, (float)end.X, (float)end.Y);
            CurrentPosition = end;
        }

        public override void Rectangle(double x, double y, double width, double height)
        {
            MoveTo(x, y);
            LineTo(x + width, y);
            LineTo(x + width, y + height);
            LineTo(x, y + height);
            CloseSubpath();
        }

        public override void StrokePath(bool close) => PaintPath(false, true, FillingRule.NonZeroWinding, close);
        public override void FillPath(FillingRule fillingRule, bool close) => PaintPath(true, false, fillingRule, close);
        public override void FillStrokePath(FillingRule fillingRule, bool close) => PaintPath(true, true, fillingRule, close);
        public override void ClosePath() => EndPath();
        public override void ModifyClippingIntersect(FillingRule clippingRule) => _pendingClip = Rule(clippingRule);

        private void PaintPath(bool fill, bool stroke, FillingRule rule, bool close)
        {
            if (close)
            {
                CloseSubpath();
            }

            AddPainting(_path, fill, stroke, Rule(rule));
            EndPath();
        }

        public override void EndPath()
        {
            if (_pendingClip is FillRule rule)
            {
                StorePath(_path);
                Commands.Add(new ClipCommand(_path, rule));
            }

            _path = new VectorPath();
            _pendingClip = null;
        }

        protected override void ClipToRectangle(PdfRectangle rectangle, FillingRule clippingRule)
        {
            var path = new VectorPath().Rectangle((float)rectangle.Left, (float)rectangle.Bottom, (float)rectangle.Width, (float)rectangle.Height);
            path = path.Transform(Matrix(CurrentTransformationMatrix));
            StorePath(path);
            Commands.Add(new ClipCommand(path, Rule(clippingRule)));
        }

        private void AddPainting(VectorPath path, bool fill, bool stroke, FillRule rule)
        {
            CurrentGraphicsState state = GetCurrentState();
            CheckState(state);
            StorePath(path);
            ColorBlendMode blend = Blend(state.BlendMode);
            if (fill)
            {
                AddFill(path, rule, state);
            }

            if (stroke)
            {
                if (state.CurrentStrokingColor?.ColorSpace == ColorSpace.Pattern)
                {
                    Warn("Pattern strokes");
                    return;
                }

                AddPaint(new StrokeCommand(path, Color(state.CurrentStrokingColor, state.AlphaConstantStroking), Stroke(state), Matrix(state.CurrentTransformationMatrix), blend), state);
            }
        }

        private void StorePath(VectorPath path)
        {
            _pathCommands = checked(_pathCommands + path.CommandCount);
            if (_pathCommands > _limits.MaximumPathCommands)
            {
                throw new InvalidDataException("PDF page exceeds MaximumPathCommands.");
            }
        }

        private static StrokeStyle Stroke(CurrentGraphicsState state)
        {
            var dashes = new float[state.LineDashPattern.Array.Count];
            for (int i = 0; i < dashes.Length; i++)
            {
                dashes[i] = (float)state.LineDashPattern.Array[i];
            }

            return new StrokeStyle
            {
                Width = (float)state.LineWidth,
                Cap = (StrokeCap)state.CapStyle,
                Join = (StrokeJoin)state.JoinStyle,
                MiterLimit = (float)state.MiterLimit,
                Dashes = dashes,
                DashOffset = state.LineDashPattern.Phase
            };
        }

        internal static Matrix3x2 Matrix(TransformationMatrix matrix)
            => new Matrix3x2((float)matrix.A, (float)matrix.B, (float)matrix.C, (float)matrix.D, (float)matrix.E, (float)matrix.F);

        private static FillRule Rule(FillingRule rule) => rule == FillingRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero;
        private static ColorBlendMode Blend(BlendMode mode) => (int)mode <= 15 ? (ColorBlendMode)mode : ColorBlendMode.Normal;

        internal static Rgba32 Color(IColor? color, double opacity = 1)
        {
            if (color?.ColorSpace == ColorSpace.Pattern)
            {
                return new Rgba32(0, 0, 0, Channel(opacity));
            }

            (double r, double g, double b) = color?.ToRGBValues() ?? (0, 0, 0);
            return new Rgba32(Channel(r), Channel(g), Channel(b), Channel(opacity));
        }

        private static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    }
}
