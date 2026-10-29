using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsRoughenEdgesTests
    {
        [Test]
        public void RougheningChangesAlphaEdgesAndPreservesInteriorColor()
        {
            PixelBuffer original = Render(border: 0), rough = Render();
            Assert.That(rough[50, 50], Is.EqualTo(new Rgba32(51, 153, 230)));
            Assert.That(rough[5, 5].A, Is.Zero);
            Assert.That(rough.Pixels.Span.SequenceEqual(original.Pixels.Span), Is.False);
            Assert.That(rough.Pixels.ToArray().Where((_, i) => i % 4 == 3).Sum(a => (long)a), Is.LessThan(original.Pixels.ToArray().Where((_, i) => i % 4 == 3).Sum(a => (long)a)));
        }

        [Test]
        public void SeedIsDeterministicAndAffectsTheRoughness()
        {
            PixelBuffer a = Render(seed: 1), repeated = Render(seed: 1), b = Render(seed: 2);
            Assert.That(a.Pixels.Span.SequenceEqual(repeated.Pixels.Span), Is.True);
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.False);
        }

        [Test]
        public void ZeroInfluenceRemovesSeedDependence()
        {
            PixelBuffer a = Render(seed: 1, influence: 0), b = Render(seed: 2, influence: 0);
            Assert.That(a.Pixels.Span.SequenceEqual(b.Pixels.Span), Is.True);
        }

        [Test]
        public void OtherEdgeModesAreReportedInsteadOfIgnored()
        {
            Assert.Throws<NotSupportedException>(() => Render(mode: 2));
            Assert.Throws<NotSupportedException>(() => Render(evolution: 10));
        }

        [Test]
        public void NoiseComplexityHasABudget()
        {
            Assert.Throws<InvalidDataException>(() => Render(complexity: 13));
        }

        private static PixelBuffer Render(double border = 8, double seed = 0, double influence = 1, double mode = 1, double evolution = 0, double complexity = 5)
        {
            object[] values = { mode, new[] { .6, .2, 0, 1 }, border, 1.09, influence, 30, 0, new[] { 0, 0 }, complexity, evolution, 0, 0, 1, seed, 0 };
            string effects = JsonSerializer.Serialize(values.Select(value => new { v = new { k = value } }));
            string json = """
                {"w":100,"h":100,"fr":30,"ip":0,"op":60,"layers":[{"ty":4,
                 "shapes":[{"ty":"rc","p":{"k":[50,50]},"s":{"k":[70,70]}},{"ty":"fl","c":{"k":[0.2,0.6,0.9]}}],
                 "ef":[{"ty":5,"mn":"ADBE Roughen Edges","ef":PARAMETERS}]}]}
                """.Replace("PARAMETERS", effects);
            TgsDocument document = TgsReader.Read(Encoding.UTF8.GetBytes(json));
            return TgsRenderer.RenderFrame(document, 0, new SvgRenderOptions { Supersampling = 1 });
        }
    }
}
