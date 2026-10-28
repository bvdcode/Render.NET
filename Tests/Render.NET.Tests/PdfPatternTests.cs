using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfPatternTests
    {
        private static readonly PdfRenderOptions Exact = new() { Dpi = 72, Supersampling = 1 };
        private static readonly Rgba32 Red = new(255, 0, 0);
        private static readonly Rgba32 White = new(255, 255, 255);

        [TestCase(1, 10, 10)]
        [TestCase(2, 10, 10)]
        [TestCase(3, 10, 10)]
        [TestCase(1, -10, 10)]
        [TestCase(1, 10, -10)]
        [TestCase(1, -10, -10)]
        public async Task ColoredCellsRepeatWithTransparentGapsAndEitherStepSign(int type, int xStep, int yStep)
        {
            byte[] data = PatternPage("1 0 0 rg -10 -10 30 30 re f", bounds: "0 0 5 5", spacing: $"{xStep} {yStep}", type: type);
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 2, 2), Is.EqualTo(Red));
            Assert.That(At(image, 12, 92), Is.EqualTo(Red));
            Assert.That(At(image, 7, 2), Is.EqualTo(White));
            Assert.That(At(image, 2, 7), Is.EqualTo(White));
        }

        [Test]
        public async Task BoundingBoxOffsetAndNegativeCoordinatesClipTheCell()
        {
            PixelBuffer image = await Render(PatternPage("1 0 0 rg -20 -20 40 40 re f", bounds: "-3 -2 3 2"));
            Assert.That(At(image, 8, 9), Is.EqualTo(Red));
            Assert.That(At(image, 11, 11), Is.EqualTo(Red));
            Assert.That(At(image, 13, 11), Is.EqualTo(White));
            Assert.That(At(image, 11, 12), Is.EqualTo(White));
        }

        [Test]
        public async Task PaintTimeTransformDoesNotMoveThePatternGrid()
        {
            PixelBuffer baseline = await Render(PatternPage("1 0 0 rg 0 0 5 5 re f"));
            PixelBuffer transformed = await Render(PatternPage("1 0 0 rg 0 0 5 5 re f",
                paint: "1 0 0 1 17 23 cm /Pattern cs /P scn -17 -23 200 100 re f"));
            Assert.That(transformed.Pixels.ToArray(), Is.EqualTo(baseline.Pixels.ToArray()));
        }

        [Test]
        public async Task PatternMatrixCanRotateAndTranslateTheGrid()
        {
            PixelBuffer image = await Render(PatternPage("1 0 0 rg 0 0 3 5 re f", matrix: "0 1 -1 0 30 10"));
            Assert.That(At(image, 27, 11), Is.EqualTo(Red));
            Assert.That(At(image, 27, 21), Is.EqualTo(Red));
            Assert.That(At(image, 24, 11), Is.EqualTo(White));
            Assert.That(At(image, 27, 15), Is.EqualTo(White));
        }

        [TestCase("DeviceRGB", "1 0 0", "0 1 0", 255, 0)]
        [TestCase("DeviceGray", "0", "0.5", 0, 128)]
        [TestCase("DeviceCMYK", "0 1 1 0", "1 0 1 0", 255, 0)]
        public async Task StencilsPreserveTheirBaseColorAcrossSaveRestore(string space, string first, string second, int red, int otherRed)
        {
            string paint = $"/Ink cs {first} /P scn q {second} /P scn 100 0 100 100 re f Q 0 0 100 100 re f";
            byte[] data = PdfTestDocument.Create(paint, resources: "/Pattern << /P 5 0 R >> /ColorSpace << /Ink [/Pattern /" + space + "] >>",
                extraObjects: Cell("0 0 5 5 re f", paintType: 2));
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 2, 2).R, Is.EqualTo(red));
            Assert.That(At(image, 102, 2).R, Is.EqualTo(otherRed).Within(2));
            Assert.That(At(image, 7, 2), Is.EqualTo(White));
        }

        [Test]
        public async Task CallerClippingAndEvenOddFillArePreserved()
        {
            PixelBuffer image = await Render(PatternPage("1 0 0 rg 0 0 10 10 re f",
                paint: "0 0 90 100 re W n /Pattern cs /P scn 10 10 160 80 re 30 30 30 40 re f*"));
            Assert.That(At(image, 20, 20), Is.EqualTo(Red));
            Assert.That(At(image, 40, 40), Is.EqualTo(White));
            Assert.That(At(image, 100, 20), Is.EqualTo(White));
            Assert.That(At(image, 5, 5), Is.EqualTo(White));
        }

        [Test]
        public async Task PatternStrokesUseAffinePensCapsAndDashLengths()
        {
            const string stroke = "2 0 0 1 0 0 cm 8 w 1 J [10 10] 0 d 10 50 m 90 50 l S";
            PixelBuffer image = await Render(PatternPage("1 0 0 rg 0 0 10 10 re f", paint: "/Pattern CS /P SCN " + stroke));
            PixelBuffer reference = await Render(PdfTestDocument.Create("1 0 0 RG " + stroke));
            Assert.That(image.Pixels.ToArray(), Is.EqualTo(reference.Pixels.ToArray()));
        }

        [Test]
        public async Task ShadingPatternsCanStrokeDashedPaths()
        {
            byte[] data = PdfTestDocument.Create("/Pattern CS /P SCN 10 w [20 10] 0 d 10 50 m 190 50 l S",
                resources: "/Pattern << /P 5 0 R >>", extraObjects: PdfTestDocument.Bytes(
                    "<< /PatternType 2 /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 200 0] /Extend [true true] "
                    + "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >> >>"));
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 15, 50).R, Is.GreaterThan(230));
            Assert.That(At(image, 35, 50), Is.EqualTo(White));
            Assert.That(At(image, 175, 50).B, Is.GreaterThan(220));
            Assert.That(At(image, 15, 40), Is.EqualTo(White));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task TextUsesPatternsForFillAndStroke(int mode)
        {
            string text = $"BT /F 45 Tf {mode} Tr 12 30 Td (Mesh) Tj ET";
            byte[] data = PdfTestDocument.Create("/Pattern cs /P scn /Pattern CS /P SCN " + text,
                resources: "/Pattern << /P 5 0 R >> /Font << /F 6 0 R >>",
                extraObjects: new[] { Cell("1 0 0 rg 0 0 10 10 re f"), PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>") });
            byte[] reference = PdfTestDocument.Create("1 0 0 rg 1 0 0 RG " + text, resources: "/Font << /F 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
            Assert.That((await Render(data)).Pixels.ToArray(), Is.EqualTo((await Render(reference)).Pixels.ToArray()));
        }

        [Test]
        public async Task OuterOpacityAppliesOnceAfterCellChildrenOverlap()
        {
            PixelBuffer image = await Render(PatternPage("1 0 0 rg 0 0 7 10 re f 3 0 7 10 re f",
                paint: "/Half gs /Pattern cs /P scn 0 0 200 100 re f", resources: "/ExtGState << /Half << /ca 0.5 >> >>"));
            Assert.That(At(image, 1, 2).G, Is.InRange(127, 128));
            Assert.That(At(image, 5, 2), Is.EqualTo(At(image, 1, 2)));
        }

        [Test]
        public async Task CellTransparencyAndOuterBlendModeComposeIndependently()
        {
            byte[] data = PatternPage("/Inner gs 1 0 0 rg 0 0 7 10 re f 3 0 7 10 re f",
                paint: "0 0 1 rg 0 0 200 100 re f /Outer gs /Pattern cs /P scn 0 0 200 100 re f",
                resources: "/ExtGState << /Outer << /ca 0.5 /BM /Multiply >> >>",
                cellResources: "/ExtGState << /Inner << /ca 0.5 >> >>");
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 1, 2).B, Is.InRange(190, 192));
            Assert.That(At(image, 5, 2).B, Is.InRange(158, 160));
            Assert.That(At(image, 5, 2).R, Is.Zero);
        }

        [Test]
        public async Task CellBlendModesSeeTheActualPageBackdrop()
        {
            PixelBuffer image = await Render(PatternPage("/M gs 1 0 0 rg 0 0 5 5 re f",
                paint: "0 0 1 rg 0 0 200 100 re f /Pattern cs /P scn 0 0 200 100 re f",
                cellResources: "/ExtGState << /M << /BM /Multiply >> >>"));
            Assert.That(At(image, 2, 2), Is.EqualTo(new Rgba32(0, 0, 0)));
            Assert.That(At(image, 7, 2), Is.EqualTo(new Rgba32(0, 0, 255)));
        }

        [Test]
        public async Task ColoredPatternInheritsTheParentInitialColor()
        {
            PixelBuffer image = await Render(PatternPage("0 0 5 5 re f", paint: "1 0 0 rg /Pattern cs /P scn 0 0 200 100 re f"));
            Assert.That(At(image, 2, 2), Is.EqualTo(new Rgba32(0, 0, 0)));
        }

        [Test]
        public async Task CellFormsResolveTheirLocalResources()
        {
            byte[] data = PdfTestDocument.Create("/Pattern cs /P scn 0 0 200 100 re f",
                resources: "/Pattern << /P 5 0 R >> /XObject << /F 7 0 R >>", extraObjects: new[]
                {
                    Cell("/F Do", resources: "/XObject << /F 6 0 R >>"),
                    PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 0 0 5 5 re f"), "/Subtype /Form /BBox [0 0 5 5]"),
                    PdfTestDocument.Stream(PdfTestDocument.Bytes("0 0 1 rg 0 0 5 5 re f"), "/Subtype /Form /BBox [0 0 5 5]")
                });
            Assert.That(At(await Render(data), 2, 2), Is.EqualTo(Red));
        }

        [Test]
        public async Task APatternInsideAFormInheritsTheFormInitialStateAndPlacement()
        {
            byte[] data = PdfTestDocument.Create("0 1 0 rg 1 0 0 1 13 17 cm /F Do", resources: "/XObject << /F 5 0 R >>", extraObjects: new[]
            {
                PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 1 0 0 1 7 0 cm /Pattern cs /P scn -7 0 80 60 re f"),
                    "/Subtype /Form /BBox [0 0 80 60] /Resources << /Pattern << /P 6 0 R >> >>"),
                Cell("0 0 5 5 re f")
            });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 15, 19), Is.EqualTo(new Rgba32(0, 255, 0)));
            Assert.That(At(image, 20, 19), Is.EqualTo(White));
        }

        [Test]
        public async Task AGraphicsSoftMaskAppliesToTheWholePatternOnce()
        {
            byte[] data = PdfTestDocument.Create("/S gs /Pattern cs /P scn 0 0 200 100 re f",
                resources: "/Pattern << /P 5 0 R >> /ExtGState << /S << /SMask << /S /Alpha /G 6 0 R >> >> >>", extraObjects: new[]
                {
                    Cell("1 0 0 rg 0 0 10 10 re f"),
                    PdfTestDocument.Stream(PdfTestDocument.Bytes("/H gs 0 0 100 100 re f"),
                        "/Subtype /Form /BBox [0 0 200 100] /Group << /S /Transparency /I true >> /Resources << /ExtGState << /H << /ca 0.5 >> >> >>")
                });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 20, 20), Is.EqualTo(new Rgba32(255, 127, 127)));
            Assert.That(At(image, 120, 20), Is.EqualTo(White));
        }

        [Test]
        public async Task AStencilImageCanBePaintedWithAPattern()
        {
            byte[] data = PdfTestDocument.Create("/Pattern cs /P scn q 100 0 0 100 0 0 cm /I Do Q",
                resources: "/Pattern << /P 5 0 R >> /XObject << /I 6 0 R >>", extraObjects: new[]
                {
                    Cell("1 0 0 rg 0 0 5 5 re f"),
                    PdfTestDocument.Stream(new byte[] { 0x40, 0x80 }, "/Subtype /Image /Width 2 /Height 2 /BitsPerComponent 1 /ImageMask true")
                });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 2, 92), Is.EqualTo(Red));
            Assert.That(At(image, 7, 92), Is.EqualTo(White));
            Assert.That(At(image, 52, 92), Is.EqualTo(White));
            Assert.That(At(image, 52, 42), Is.EqualTo(Red));
        }

        [Test]
        public async Task ANoDistortionPatternOnlyReplaysTilesInsideTheVisibleClip()
        {
            byte[] data = PatternPage("1 0 0 rg 0 0 1 1 re f", type: 2, bounds: "0 0 0.25 0.25", spacing: "0.25 0.25",
                paint: "/Pattern cs /P scn 1 1 1 1 re f");
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 1, 1), Is.EqualTo(Red));
            Assert.That(At(image, 2, 1), Is.EqualTo(White));
        }

        [Test]
        public async Task AuthoredFixtureRendersPatternsContainingImagesMasksAndNestedForms()
        {
            string file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Pdf", "patterns.pdf");
            using PdfDocument document = await PdfReader.ReadFileAsync(file);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = PdfRenderer.Render(page, Exact);
            Assert.That(image.Width, Is.EqualTo(600));
            Assert.That(image.Height, Is.EqualTo(400));
            Assert.That(At(image, 226, 42), Is.EqualTo(new Rgba32(255, 90, 20)));
            Assert.That(At(image, 226, 34), Is.EqualTo(new Rgba32(40, 90, 210)));
            Assert.That(At(image, 402, 34), Is.EqualTo(White));
            Assert.That(At(image, 408, 34).R, Is.InRange(178, 179));
            Assert.That(At(image, 408, 34).B, Is.EqualTo(204));
        }

        [Test]
        public async Task NestedPatternsUseTheOuterCellCoordinateSystem()
        {
            byte[] data = PdfTestDocument.Create("/Pattern cs /P scn 0 0 200 100 re f", resources: "/Pattern << /P 5 0 R >>", extraObjects: new[]
            {
                Cell("/Pattern cs /Q scn 0 0 10 10 re f", resources: "/Pattern << /Q 6 0 R >>", matrix: "1 0 0 1 3 4", spacing: "20 20"),
                Cell("1 0 0 rg 0 0 2 2 re f", bounds: "0 0 2 2", spacing: "5 5")
            });
            PixelBuffer image = await Render(data);
            Assert.That(At(image, 3, 4), Is.EqualTo(Red));
            Assert.That(At(image, 8, 9), Is.EqualTo(Red));
            Assert.That(At(image, 6, 7), Is.EqualTo(White));
            Assert.That(At(image, 15, 16), Is.EqualTo(White));
        }

        [TestCase("0 10")]
        [TestCase("10 0")]
        public async Task ZeroSpacingFailsDuringPageInterpretation(string spacing)
        {
            using PdfDocument document = await Read(PatternPage("0 0 5 5 re f", spacing: spacing));
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task RecursivePatternsFailBeforeRendering()
        {
            byte[] data = PatternPage("/Pattern cs /P scn 0 0 10 10 re f", cellResources: "/Pattern << /P 5 0 R >>");
            using PdfDocument document = await Read(data);
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task ReplayBudgetRejectsExtremelyDenseBackdropDependentPatterns()
        {
            byte[] data = PatternPage("/M gs 1 0 0 rg 0 0 1 1 re f", spacing: "0.01 0.01",
                cellResources: "/ExtGState << /M << /BM /Multiply >> >>");
            using PdfDocument document = await Read(data);
            PdfPage page = document.GetPage(1);
            Assert.Throws<InvalidDataException>(() => PdfRenderer.Render(page, Exact));
        }

        [TestCase("1 0 0 rg")]
        [TestCase("/RelativeColorimetric ri")]
        public async Task UncoloredCellsCannotSetTheirOwnColor(string operation)
        {
            byte[] data = PdfTestDocument.Create("/Ink cs 1 0 0 /P scn 0 0 200 100 re f",
                resources: "/Pattern << /P 5 0 R >> /ColorSpace << /Ink [/Pattern /DeviceRGB] >>",
                extraObjects: Cell(operation + " 0 0 5 5 re f", paintType: 2));
            using PdfDocument document = await Read(data);
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [TestCase("TR")]
        [TestCase("TR2")]
        [TestCase("BG")]
        [TestCase("BG2")]
        [TestCase("UCR")]
        [TestCase("UCR2")]
        [TestCase("HT")]
        public async Task UncoloredCellsCannotChangeColorRelatedGraphicsState(string parameter)
        {
            byte[] data = PdfTestDocument.Create("/Ink cs 1 0 0 /P scn 0 0 200 100 re f",
                resources: "/Pattern << /P 5 0 R >> /ColorSpace << /Ink [/Pattern /DeviceRGB] >>",
                extraObjects: Cell("/G gs 0 0 5 5 re f", paintType: 2, resources: "/ExtGState << /G << /" + parameter + " /Identity >> >>"));
            using PdfDocument document = await Read(data);
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task PatternInterpretationCountsAgainstThePageOperationBudget()
        {
            using MemoryStream? stream = new MemoryStream(PatternPage("0 0 5 5 re f"));
            using PdfDocument document = await PdfReader.ReadAsync(stream, new PdfReaderOptions { MaximumOperations = 4 });
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        [Test]
        public async Task CapturedPatternsRenderConcurrentlyAfterDocumentDisposal()
        {
            PdfPage page;
            using (PdfDocument document = await Read(PatternPage("1 0 0 rg 0 0 5 5 re f")))
            {
                page = document.GetPage(1);
                Assert.That(page.UnsupportedFeatures, Is.Empty);
            }

            PixelBuffer[] images = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => PdfRenderer.Render(page, Exact))));
            Assert.That(images.All(image => image.Pixels.Span.SequenceEqual(images[0].Pixels.Span)), Is.True);
        }

        private static byte[] PatternPage(string cell, string paint = "/Pattern cs /P scn 0 0 200 100 re f", string bounds = "0 0 10 10",
            string spacing = "10 10", int type = 1, string matrix = "1 0 0 1 0 0", string resources = "", string cellResources = "")
            => PdfTestDocument.Create(paint, resources: "/Pattern << /P 5 0 R >> " + resources,
                extraObjects: Cell(cell, bounds, spacing, type, matrix, cellResources));

        private static byte[] Cell(string content, string bounds = "0 0 10 10", string spacing = "10 10", int type = 1,
            string matrix = "1 0 0 1 0 0", string resources = "", int paintType = 1)
        {
            string[] steps = spacing.Split(' ');
            return PdfTestDocument.Stream(PdfTestDocument.Bytes(content), $"/Type /Pattern /PatternType 1 /PaintType {paintType} /TilingType {type} "
                + $"/BBox [{bounds}] /XStep {steps[0]} /YStep {steps[1]} /Matrix [{matrix}] /Resources << {resources} >>");
        }

        private static Rgba32 At(PixelBuffer image, int x, int y) => image[x, image.Height - 1 - y];

        private static async Task<PdfDocument> Read(byte[] data)
        {
            using MemoryStream? stream = new MemoryStream(data);
            return await PdfReader.ReadAsync(stream);
        }

        private static async Task<PixelBuffer> Render(byte[] data)
        {
            using PdfDocument document = await Read(data);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, Exact);
        }
    }
}
