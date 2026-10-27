using System.Numerics;
using NUnit.Framework;
using Render.NET.Svg;

namespace Render.NET.Tests
{
    public class SvgEffectsTests
    {
        private static PixelBuffer Render(string content, SvgRenderOptions? options = null)
        {
            SvgDocument document = SvgReader.Read("<svg width='100' height='100'>" + content + "</svg>");
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            return SvgRenderer.Render(document, options ?? new SvgRenderOptions { Supersampling = 1 });
        }

        private static string Filter(string operations, string body = "<rect x='30' y='30' width='40' height='40' fill='red' filter='url(#f)'/>") => "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='100' height='100' color-interpolation-filters='sRGB'>" + operations + "</filter></defs>" + body;
        [TestCase("alpha", 255)]
        [TestCase("luminance", 54)]
        public void MaskSeparatesAlphaAndLuminance(string type, int alpha)
        {
            PixelBuffer image = Render("<defs><mask id='m' mask-type='" + type + "'><rect width='100' height='100' fill='red'/></mask></defs><rect width='100' height='100' fill='lime' mask='url(#m)'/>");
            Assert.That(image[50, 50].G, Is.EqualTo(255));
            Assert.That(image[50, 50].A, Is.EqualTo(alpha).Within(1));
        }

        [Test]
        public void ObjectBoxMaskAndGradientRespectTargetBounds()
        {
            PixelBuffer image = Render("<defs><linearGradient id='g'><stop stop-color='white'/><stop offset='1' stop-color='black'/></linearGradient><mask id='m' maskContentUnits='objectBoundingBox'><rect width='1' height='1' fill='url(#g)'/></mask></defs><rect x='20' y='20' width='60' height='60' fill='blue' mask='url(#m)'/>");
            Assert.That(image[20, 50].A, Is.GreaterThan(250));
            Assert.That(image[79, 50].A, Is.LessThan(5));
            Assert.That(image[50, 50].A, Is.EqualTo(125).Within(3));
        }

        [Test]
        public void NestedMaskAndGroupOpacityCompositeOnce()
        {
            PixelBuffer image = Render("<defs><mask id='m' mask-type='alpha'><rect width='100' height='100' opacity='.5'/></mask></defs><g mask='url(#m)' opacity='.5' fill='lime'><rect width='70' height='100'/><rect x='30' width='70' height='100'/></g>");
            Assert.That(image[10, 50].A, Is.EqualTo(64));
            Assert.That(image[50, 50], Is.EqualTo(image[10, 50]));
        }

        [Test]
        public void OffsetTransformsWithObject()
        {
            PixelBuffer image = Render(Filter("<feOffset dx='10' dy='5'/>", "<rect x='10' y='10' width='10' height='10' fill='red' transform='translate(0 100) rotate(-90)' filter='url(#f)'/>"));
            Assert.That(image[20, 75].R, Is.EqualTo(255));
            Assert.That(image[15, 85].A, Is.Zero);
        }

        [Test]
        public void ColorMatrixUsesUnpremultipliedChannels()
        {
            PixelBuffer image = Render(Filter("<feColorMatrix values='0 0 1 0 0 0 1 0 0 0 1 0 0 0 0 0 0 0 .5 0'/>"));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(0, 0, 255, 128)));
        }

        [Test]
        public void TransferTableAndGammaChangeChannelsAndAlpha()
        {
            PixelBuffer image = Render(Filter("<feComponentTransfer><feFuncR type='linear' slope='0' intercept='.25'/><feFuncG type='table' tableValues='0 1'/><feFuncB type='gamma' amplitude='1' exponent='2' offset='.5'/><feFuncA type='discrete' tableValues='0 .5'/></feComponentTransfer>"));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(64, 0, 128, 128)));
        }

        [Test]
        public void BlurPreservesColorWithoutTransparentBlackHalo()
        {
            PixelBuffer image = Render(Filter("<feGaussianBlur stdDeviation='3'/>"));
            Assert.That(image[29, 50].A, Is.InRange(80, 127));
            Assert.That(image[29, 50].R, Is.EqualTo(255));
            Assert.That(image[10, 50].A, Is.Zero);
        }

        [Test]
        public void FilterRunsBeforeClipPath()
        {
            PixelBuffer image = Render("<defs><clipPath id='c'><rect x='50' width='50' height='100'/></clipPath></defs>" + Filter("<feGaussianBlur stdDeviation='3'/>", "<rect x='30' y='30' width='40' height='40' fill='red' filter='url(#f)' clip-path='url(#c)'/>"));
            Assert.That(image[49, 50].A, Is.Zero);
            Assert.That(image[50, 50].A, Is.EqualTo(255));
        }

        [Test]
        public void NamedInputsCompositeAndMergeInPaintOrder()
        {
            string operations = "<feFlood flood-color='blue' result='b'/><feComposite in='b' in2='SourceAlpha' operator='in' result='cut'/><feOffset in='cut' dx='20'/><feMerge><feMergeNode/><feMergeNode in='SourceGraphic'/></feMerge>";
            PixelBuffer image = Render(Filter(operations));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[80, 50], Is.EqualTo(new Rgba32(0, 0, 255)));
            Assert.That(image[10, 50].A, Is.Zero);
        }

        [TestCase("in", 255)]
        [TestCase("out", 0)]
        [TestCase("xor", 0)]
        [TestCase("arithmetic", 128)]
        public void PorterDuffAndArithmeticOperationsHaveExpectedAlpha(string operation, int alpha)
        {
            PixelBuffer image = Render(Filter("<feComposite in='SourceGraphic' in2='SourceGraphic' operator='" + operation + "' k2='.5'/>"));
            Assert.That(image[50, 50].A, Is.EqualTo(alpha));
        }

        [TestCase("dilate", 255, 255)]
        [TestCase("erode", 0, 0)]
        public void MorphologyExpandsOrContractsCoverage(string operation, int outside, int inside)
        {
            PixelBuffer image = Render(Filter("<feMorphology operator='" + operation + "' radius='3'/>"));
            Assert.That(image[28, 50].A, Is.EqualTo(outside));
            Assert.That(image[31, 50].A, Is.EqualTo(inside));
            Assert.That(image[50, 50].A, Is.EqualTo(255));
        }

        [Test]
        public void IdentityConvolutionPreservesSourcePixels()
        {
            PixelBuffer image = Render(Filter("<feConvolveMatrix order='3' kernelMatrix='0 0 0 0 1 0 0 0 0'/>"));
            PixelBuffer expected = Render("<rect x='30' y='30' width='40' height='40' fill='red'/>");
            Assert.That(image.Pixels.Span.SequenceEqual(expected.Pixels.Span), Is.True);
        }

        [Test]
        public void DisplacementUsesSelectedUnpremultipliedControlChannel()
        {
            PixelBuffer image = Render(Filter("<feFlood flood-color='red' result='control'/><feDisplacementMap in='SourceGraphic' in2='control' scale='20' xChannelSelector='R' yChannelSelector='G'/>"));
            Assert.That(image[25, 45].R, Is.EqualTo(255));
            Assert.That(image[65, 45].A, Is.Zero);
        }

        [Test]
        public void TurbulenceIsDeterministicAndSeedChangesOutput()
        {
            PixelBuffer a = Render(Filter("<feTurbulence baseFrequency='.1' numOctaves='2' seed='42'/>"));
            PixelBuffer b = Render(Filter("<feTurbulence baseFrequency='.1' numOctaves='2' seed='42'/>"));
            PixelBuffer c = Render(Filter("<feTurbulence baseFrequency='.1' numOctaves='2' seed='43'/>"));
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.True);
            Assert.That(a.Pixels.Span.SequenceEqual(c.Pixels.Span), Is.False);
        }

        [Test]
        public void DistantLightOnFlatOpaqueSurfaceHasExpectedIntensity()
        {
            PixelBuffer image = Render(Filter("<feDiffuseLighting lighting-color='lime' diffuseConstant='.5'><feDistantLight elevation='90'/></feDiffuseLighting>"));
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(0, 128, 0)));
        }

        [Test]
        public void CssFiltersUseSrgbAndApplyInOrder()
        {
            PixelBuffer image = Render("<rect width='100' height='100' fill='red' style='filter:invert(100%) opacity(50%)'/>");
            Assert.That(image[50, 50], Is.EqualTo(new Rgba32(0, 255, 255, 128)));
        }

        [Test]
        public void TileRepeatsTheDeclaredInputRegionIncludingTransparentSpace()
        {
            PixelBuffer image = Render(Filter("<feOffset x='20' y='20' width='60' height='60' result='cell'/><feTile in='cell'/>", "<rect x='30' y='30' width='20' height='20' fill='red' filter='url(#f)'/>"));
            Assert.That(image[35, 35].R, Is.EqualTo(255));
            Assert.That(image[95, 35].R, Is.EqualTo(255));
            Assert.That(image[75, 35].A, Is.Zero);
        }

        [Test]
        public void LocalFilterImageAndUnboundedFillPaintAreAvailable()
        {
            PixelBuffer image = Render("<defs><path id='square' fill='lime' d='M10 10H30V30H10Z'/></defs>" + Filter("<feImage href='#square'/>", "<rect x='40' y='40' width='20' height='20' filter='url(#f)'/>"));
            Assert.That(image[20, 20], Is.EqualTo(new Rgba32(0, 255, 0)));
            Assert.That(image[50, 50].A, Is.Zero);
            PixelBuffer fill = Render(Filter("<feOffset in='FillPaint' dx='20'/>", "<rect width='10' height='10' fill='red' filter='url(#f)'/>"));
            Assert.That(fill[80, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
        }

        [Test]
        public void CssBlurDoesNotClipZeroHeightStrokeBounds()
        {
            PixelBuffer image = Render("<path d='M20 50H80' fill='none' stroke='red' stroke-width='4' filter='blur(3px)'/>");
            Assert.That(image[50, 50].R, Is.EqualTo(255));
            Assert.That(image[50, 47].A, Is.GreaterThan(20));
            SvgDocument unsupported = SvgReader.Read("<svg><rect width='10' height='10' filter='drop-shadow(2px 3px 4px red)'/></svg>");
            Assert.That(unsupported.UnsupportedFeatures, Is.Not.Empty);
        }

        [Test]
        public void EffectBudgetAndResourceCyclesFailExplicitly()
        {
            Assert.Throws<InvalidOperationException>(() => Render(Filter("<feOffset/>"), new SvgRenderOptions { MaximumEffectBytes = 100 }));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read("<svg><defs><mask id='m' mask='url(#m)'/></defs><rect width='20' height='20' mask='url(#m)'/></svg>"));
        }
    }
}
