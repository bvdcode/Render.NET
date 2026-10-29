using System;
using System.Linq;
using System.Threading;
using Render.NET.Svg;

namespace Render.NET.Tgs
{
    /// <summary>Renders one selected TGS/Lottie frame on the managed vector canvas.</summary>
    public static partial class TgsRenderer
    {
        /// <summary>Renders an absolute frame number in [InPoint, OutPoint). Fractional frames are supported.</summary>
        public static PixelBuffer RenderFrame(TgsDocument document, double frame, SvgRenderOptions? options = null, CancellationToken cancellationToken = default)
            => RenderFrameDetailed(document, frame, options, cancellationToken).Image;

        /// <summary>Renders one frame with timing and detected feature diagnostics.</summary>
        public static TgsFrame RenderFrameDetailed(TgsDocument document, double frame, SvgRenderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new SvgRenderOptions();
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(frame) || frame < document.InPoint || frame >= document.OutPoint)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }

            if (!options.AllowPartialRendering && document.UnsupportedFeatures.Count > 0)
            {
                throw new NotSupportedException("Unsupported TGS/Lottie features: " + string.Join("; ", document.UnsupportedFeatures));
            }

            TgsSceneBuilder builder = new(document, cancellationToken);
            SvgDocument scene = builder.Build(frame, options.AllowPartialRendering);
            return new TgsFrame
            {
                SourceFrame = frame,
                Timestamp = TimeSpan.FromSeconds((frame - document.InPoint) / document.FrameRate),
                Duration = TimeSpan.FromSeconds(Math.Min(1, document.OutPoint - frame) / document.FrameRate),
                Image = SvgRenderer.Render(scene, options, cancellationToken),
                Warnings = Array.AsReadOnly(document.UnsupportedFeatures.Concat(builder.Warnings).Concat(scene.UnsupportedFeatures).Distinct().Order().ToArray())
            };
        }

        /// <summary>Renders the frame at an elapsed time from the composition's in-point.</summary>
        public static PixelBuffer RenderAtTime(TgsDocument document, double seconds, SvgRenderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            return RenderFrame(document, document.InPoint + (seconds * document.FrameRate), options, cancellationToken);
        }
    }
}
