using NUnit.Framework;
using Render.NET.Pdf;

namespace Render.NET.Tests
{
    public class PdfOptionalContentTests
    {
        [TestCase("", true)]
        [TestCase("/BaseState /ON", true)]
        [TestCase("/BaseState /OFF", false)]
        [TestCase("/BaseState /OFF /ON [5 0 R]", true)]
        [TestCase("/BaseState /ON /OFF [5 0 R]", false)]
        public async Task DefaultConfigurationControlsNamedGroups(string configuration, bool visible)
        {
            PixelBuffer image = await Render("/OC /Layer BDC 0 1 0 rg 0 0 200 100 re f EMC", configuration);
            Assert.That(image[100, 50], Is.EqualTo(visible ? new Rgba32(0, 255, 0) : new Rgba32(255, 255, 255)));
        }

        [TestCase("AllOn", false)]
        [TestCase("AnyOn", true)]
        [TestCase("AnyOff", true)]
        [TestCase("AllOff", false)]
        public async Task MembershipPoliciesCombineGroupStates(string policy, bool visible)
        {
            PixelBuffer image = await Render("/OC /Member BDC 1 0 0 rg 0 0 200 100 re f EMC", "/OFF [6 0 R]", "/P /" + policy);
            Assert.That(image[100, 50], Is.EqualTo(visible ? new Rgba32(255, 0, 0) : new Rgba32(255, 255, 255)));
        }

        [TestCase("[/And 5 0 R 6 0 R]", false)]
        [TestCase("[/Or 5 0 R 6 0 R]", true)]
        [TestCase("[/Not 6 0 R]", true)]
        [TestCase("[/And 5 0 R [/Not 6 0 R]]", true)]
        public async Task VisibilityExpressionsOverrideTheMembershipPolicy(string expression, bool visible)
        {
            PixelBuffer image = await Render("/OC /Member BDC 1 0 0 rg 0 0 200 100 re f EMC", "/OFF [6 0 R]", "/VE " + expression);
            Assert.That(image[100, 50], Is.EqualTo(visible ? new Rgba32(255, 0, 0) : new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task NestedMarkedContentCannotRevealAHiddenParent()
        {
            PixelBuffer image = await Render("/OC /Layer BDC /Span BMC 0 1 0 rg 0 0 200 100 re f EMC EMC 1 0 0 rg 0 0 50 100 re f", "/OFF [5 0 R]");
            Assert.That(image[25, 50], Is.EqualTo(new Rgba32(255, 0, 0)));
            Assert.That(image[100, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [TestCase("View", false)]
        [TestCase("Print", true)]
        public async Task OnlyViewUsageApplicationsAffectThePreview(string purpose, bool visible)
        {
            PixelBuffer image = await Render("/OC /Layer BDC 0 1 0 rg 0 0 200 100 re f EMC",
                "/AS [<< /Event /" + purpose + " /Category [/View] >>]", firstGroupProperties: "/Usage << /View << /ViewState /OFF >> >>");
            Assert.That(image[100, 50], Is.EqualTo(visible ? new Rgba32(0, 255, 0) : new Rgba32(255, 255, 255)));
        }

        [Test]
        public async Task HiddenFormXObjectsDoNotPaint()
        {
            Assert.That((await Render("/Form Do", "/OFF [5 0 R]"))[100, 50], Is.EqualTo(new Rgba32(255, 255, 255)));
        }

        [TestCase("", true)]
        [TestCase("/OFF [6 0 R]", false)]
        public async Task AnnotationMembershipControlsItsAppearance(string configuration, bool visible)
        {
            PixelBuffer image = await Render("", configuration, annotate: true);
            Assert.That(image[100, 50], Is.EqualTo(visible ? new Rgba32(0, 255, 0) : new Rgba32(255, 255, 255)));
        }

        private static async Task<PixelBuffer> Render(string content, string configuration, string membership = "", string firstGroupProperties = "", bool annotate = false)
        {
            List<byte[]> objects = new()
            {
                PdfTestDocument.Bytes("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R 6 0 R] /D << " + configuration + " >> >> >>"),
                PdfTestDocument.Bytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
                PdfTestDocument.Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << /Properties << /Layer 5 0 R /Member 7 0 R >> /XObject << /Form 8 0 R >> >> /Contents 4 0 R " + (annotate ? "/Annots [9 0 R]" : "") + " >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes(content)),
                PdfTestDocument.Bytes("<< /Type /OCG /Name (First) " + firstGroupProperties + " >>"),
                PdfTestDocument.Bytes("<< /Type /OCG /Name (Second) >>"),
                PdfTestDocument.Bytes("<< /Type /OCMD /OCGs [5 0 R 6 0 R] " + membership + " >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes("0 1 0 rg 0 0 200 100 re f"), "/Type /XObject /Subtype /Form /BBox [0 0 200 100] /OC 5 0 R /Resources << >>"),
                PdfTestDocument.Bytes("<< /Type /Annot /Subtype /Stamp /Rect [0 0 200 100] /OC 6 0 R /AP << /N 8 0 R >> >>")
            };
            using MemoryStream input = new MemoryStream(PdfTestDocument.Write(objects));
            using PdfDocument document = await PdfReader.ReadAsync(input);
            PdfPage page = document.GetPage(1);
            Assert.That(page.UnsupportedFeatures, Is.Empty);
            return PdfRenderer.Render(page, new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
        }
    }
}
