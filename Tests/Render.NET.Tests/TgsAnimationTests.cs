using System.Text;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsAnimationTests
    {
        private static TgsDocument Document() => TgsReader.Read(Encoding.UTF8.GetBytes("""
            {"w":100,"h":100,"fr":10,"ip":10,"op":20,"layers":[
              {"ty":4,"ks":{"p":{"k":[0,0],"x":"$bm_rt=[time*20,50];"}},"shapes":[
                {"ty":"rc","p":{"k":[0,0]},"s":{"k":[8,8]}},{"ty":"fl","c":{"k":[1,0,0]}}
              ]}
            ]}
            """), new TgsReaderOptions { Expressions = new TgsExpressionOptions() });
        private static SvgRenderOptions Options() => new() { Width = 100, Height = 100, Supersampling = 1 };

        [Test]
        public void SequenceResamplesFpsAndPreservesCompositionTiming()
        {
            TgsFrame[] frames = TgsRenderer.RenderFrames(Document(), new TgsAnimationOptions { FrameRate = 4 }, Options()).ToArray();
            Assert.That(frames.Select(f => f.SourceFrame), Is.EqualTo(new[] { 10d, 12.5, 15, 17.5 }));
            Assert.That(frames.Select(f => f.Timestamp.TotalSeconds), Is.EqualTo(new[] { 0d, .25, .5, .75 }));
            Assert.That(frames.All(f => f.Duration.TotalSeconds == .25 && f.Warnings.Count == 0), Is.True);
            Assert.That(frames[0].Image[20, 50].A, Is.EqualTo(255));
            Assert.That(frames[1].Image[25, 50].A, Is.EqualTo(255));
            Assert.That(frames[0].Image.Pixels.Span.SequenceEqual(frames[1].Image.Pixels.Span), Is.False);
        }

        [Test]
        public void LastFrameHasOnlyRemainingDuration()
        {
            TgsFrame[] frames = TgsRenderer.RenderFrames(Document(), new TgsAnimationOptions { StartTime = TimeSpan.FromSeconds(.2), Duration = TimeSpan.FromSeconds(.35) }, Options()).ToArray();
            Assert.That(frames.Length, Is.EqualTo(4));
            Assert.That(frames[0].Timestamp.TotalSeconds, Is.EqualTo(.2).Within(1e-7));
            Assert.That(frames[^1].Duration.TotalSeconds, Is.EqualTo(.05).Within(1e-7));
        }

        [Test]
        public void FrameCountAndTimingAreValidatedBeforeRendering()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrames(Document(), new TgsAnimationOptions { MaximumFrames = 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrames(Document(), new TgsAnimationOptions { FrameRate = double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrames(Document(), new TgsAnimationOptions { StartTime = TimeSpan.FromSeconds(1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => TgsRenderer.RenderFrames(Document(), new TgsAnimationOptions { Duration = TimeSpan.FromSeconds(2) }));
        }

        [Test]
        public void EnumerationStopsOnCancellation()
        {
            using CancellationTokenSource cancellation = new();
            using IEnumerator<TgsFrame> frames = TgsRenderer.RenderFrames(Document(), options: Options(), cancellationToken: cancellation.Token).GetEnumerator();
            Assert.That(frames.MoveNext(), Is.True);
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => frames.MoveNext());
        }

        [Test]
        public void PartialRenderingReturnsFeatureDiagnostics()
        {
            TgsDocument document = TgsReader.Read(Encoding.UTF8.GetBytes("""
                {"w":10,"h":10,"fr":30,"ip":0,"op":2,"layers":[{"ty":5}]}
                """));
            TgsFrame frame = TgsRenderer.RenderFrameDetailed(document, 0, new SvgRenderOptions { AllowPartialRendering = true });
            Assert.That(frame.Warnings, Does.Contain("Lottie layer type 5"));
        }

        [Test]
        public async Task ExpressionFramesAreIndependentAndConcurrent()
        {
            TgsDocument document = Document();
            Task<TgsFrame>[] jobs = new[] { 10d, 15, 10, 15 }.Select(f => Task.Run(() => TgsRenderer.RenderFrameDetailed(document, f, Options()))).ToArray();
            TgsFrame[] frames = await Task.WhenAll(jobs);
            Assert.That(frames[0].Image.Pixels.Span.SequenceEqual(frames[2].Image.Pixels.Span), Is.True);
            Assert.That(frames[1].Image.Pixels.Span.SequenceEqual(frames[3].Image.Pixels.Span), Is.True);
            Assert.That(frames[0].Image.Pixels.Span.SequenceEqual(frames[1].Image.Pixels.Span), Is.False);
        }
    }
}
