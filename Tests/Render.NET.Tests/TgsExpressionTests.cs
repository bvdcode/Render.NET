using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsExpressionTests
    {
        private const string Rectangle = """
            {"ty":"rc","p":{"k":[0,0]},"s":{"k":[8,8]}},{"ty":"fl","c":{"k":[1,0,0]}}
            """;

        [Test]
        public void ExpressionsRequireExplicitOptIn()
        {
            TgsDocument document = Read(Layer("$bm_rt=[20,50];"), enabled: false);
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = TgsRenderer.RenderFrame(document, 0, Options());
            Assert.That(image[1, 1].R, Is.EqualTo(255));
            Assert.That(image[20, 50].A, Is.Zero);
        }

        [TestCase(0, 20)]
        [TestCase(15, 35)]
        [TestCase(30, 50)]
        public void ExpressionTimeChangesRenderedPosition(double frame, int x)
        {
            PixelBuffer image = Render(Layer("$bm_rt=[20+time*30,50];"), frame);
            Assert.That(image[x, 50].R, Is.EqualTo(255));
            Assert.That(image[x + 10, 50].A, Is.Zero);
        }

        [Test]
        public void EffectPropertiesResolveExpressionsLazily()
        {
            string effect = """
                ,"ef":[{"ty":5,"mn":"ADBE Slider Control","nm":"distance","ef":[
                  {"nm":"Slider","mn":"ADBE Slider Control-0001","v":{"k":1,"x":"$bm_rt=20+time*30;"}},
                  {"nm":"unused","v":{"k":0,"x":"$bm_rt=thisComp.layer('missing').position;"}}
                ]}]
                """;
            PixelBuffer image = Render(Layer("$bm_rt=[effect('distance')(1),50];", effect), 15);
            Assert.That(image[35, 50].A, Is.EqualTo(255));
        }

        [Test]
        public void CompositionTimeIsRemappedBeforeEvaluatingExpressions()
        {
            string child = Layer("$bm_rt=[time*30,50];");
            string json = """
                {"w":100,"h":100,"fr":30,"ip":0,"op":60,"layers":[
                  {"ty":0,"refId":"child","st":10,"sr":2,"w":100,"h":100}
                ],"assets":[{"id":"child","layers":[CHILD]}]}
                """.Replace("CHILD", child);
            PixelBuffer image = TgsRenderer.RenderFrame(ReadJson(json), 30, Options());
            Assert.That(image[10, 50].A, Is.EqualTo(255));
            Assert.That(image[30, 50].A, Is.Zero);
        }

        [Test]
        public void LayerToCompIncludesAnchorScaleRotationAndParents()
        {
            string references = """
                ,{"ty":3,"nm":"reference","ind":2,"parent":3,"ks":{"a":{"k":[5,0]},"p":{"k":[10,0]},"s":{"k":[200,200]}}}
                ,{"ty":3,"ind":3,"ks":{"p":{"k":[50,50]},"r":{"k":90}}}
                """;
            PixelBuffer image = Render(Layer("$bm_rt=thisComp.layer('reference').toComp([10,0]).slice(0,2);") + references, 0);
            Assert.That(image[50, 70].A, Is.EqualTo(255));
        }

        [Test]
        public void CyclicPropertyDependenciesFail()
        {
            string effect = """
                ,"ef":[{"ty":5,"mn":"ADBE Slider Control","nm":"distance","ef":[
                  {"v":{"k":1,"x":"$bm_rt=effect('distance')(1).value;"}}
                ]}]
                """;
            Assert.Throws<InvalidDataException>(() => Render(Layer("$bm_rt=[effect('distance')(1),50];", effect), 0));
        }

        [Test]
        public void MissingLayerIsAnError()
        {
            Assert.That(() => Render(Layer("$bm_rt=thisComp.layer('Setting').position;"), 0), Throws.Exception.With.Message.Contains("Missing composition layer"));
        }

        [Test]
        public async Task MissingStickerControllerReportsTheUnresolvedLayer()
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers", "missing-color-controller.tgs");
            TgsDocument document = await TgsReader.ReadFileAsync(path, new TgsReaderOptions { Expressions = new TgsExpressionOptions() });
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            Assert.That(() => TgsRenderer.RenderFrame(document, 0), Throws.Exception.With.Message.Contains("Missing composition layer Setting"));
        }

        [Test]
        public void InfiniteLoopAndLargeAllocationAreBounded()
        {
            TgsExpressionOptions limits = new() { MaximumStatements = 2_000, MaximumMemoryBytes = 2 * 1024 * 1024 };
            Assert.Throws<Jint.Runtime.StatementsCountOverflowException>(() => Render(Layer("while(true){}"), 0, limits));
            Assert.Throws<Jint.Runtime.MemoryLimitExceededException>(() => Render(Layer("$bm_rt='x'.repeat(4000000);"), 0, limits));
        }

        [Test]
        public void RunningExpressionObservesCancellation()
        {
            TgsDocument document = ReadJson("{\"w\":100,\"h\":100,\"fr\":30,\"ip\":0,\"op\":60,\"layers\":[" + Layer("while(true){}") + "]}", new TgsExpressionOptions { MaximumStatements = 100_000_000 });
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(30));
            Assert.Catch<OperationCanceledException>(() => TgsRenderer.RenderFrame(document, 0, Options(), cancellation.Token));
        }

        [Test]
        public void RuntimeObjectsAreNotExposedToExpressions()
        {
            PixelBuffer image = Render(Layer("$bm_rt=[typeof System==='undefined' && typeof importNamespace==='undefined' && typeof fetch==='undefined' ? 20 : 80,50];"), 0);
            Assert.That(image[20, 50].A, Is.EqualTo(255));
        }

        [Test]
        public void InvalidOutputDoesNotBecomeDefaultCoordinates()
        {
            Assert.Throws<InvalidDataException>(() => Render(Layer("$bm_rt=[NaN,50];"), 0));
            Assert.Throws<InvalidDataException>(() => Render(Layer("$bm_rt=20;"), 0));
        }

        [TestCase("{}")]
        [TestCase("{v:[[0,0]],i:[],o:[[0,0]],c:true}")]
        public void MalformedPathOutputDoesNotEraseContentSilently(string result)
        {
            string json = """
                {"w":100,"h":100,"fr":30,"ip":0,"op":60,"layers":[{"ty":4,"shapes":[
                  {"ty":"sh","ks":{"k":{"v":[[10,10]],"i":[[0,0]],"o":[[0,0]],"c":false},"x":EXPRESSION}},
                  {"ty":"st","c":{"k":[1,0,0]},"w":{"k":2}}
                ]}]}
                """.Replace("EXPRESSION", JsonSerializer.Serialize("$bm_rt=" + result + ";"));
            Assert.Throws<InvalidDataException>(() => TgsRenderer.RenderFrame(ReadJson(json), 0));
        }

        [Test]
        public void ExpressionDependencyBudgetIsEnforced()
        {
            string effect = """
                ,"ef":[{"ty":5,"mn":"ADBE Slider Control","nm":"distance","ef":[{"v":{"k":1,"x":"$bm_rt=20;"}}]}]
                """;
            Assert.Throws<InvalidDataException>(() => Render(Layer("$bm_rt=[effect('distance')(1),50];", effect), 0, new TgsExpressionOptions { MaximumEvaluations = 1 }));
        }

        [Test]
        public void TerminalTimeOnlyKeyRetainsItsPreviousValue()
        {
            string json = """
                {"w":100,"h":100,"fr":30,"ip":0,"op":60,"layers":[
                 {"ty":4,"ks":{"p":{"a":1,"k":[{"t":0,"s":[20,50]},{"t":30,"s":[60,50]},{"t":40}]}},"shapes":[SHAPES]}
                ]}
                """.Replace("SHAPES", Rectangle);
            PixelBuffer image = TgsRenderer.RenderFrame(ReadJson(json), 50, Options());
            Assert.That(image[60, 50].A, Is.EqualTo(255));
        }

        [TestCase("elastic-expressions.tgs", 0)]
        [TestCase("elastic-expressions.tgs", 30)]
        [TestCase("elastic-expressions.tgs", 60)]
        [TestCase("bounce-path-expression.tgs", 0)]
        [TestCase("bounce-path-expression.tgs", 45)]
        [TestCase("bounce-path-expression.tgs", 90)]
        public async Task StickerExpressionsRenderSelectedFrames(string name, double frame)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Tgs", "Stickers", name);
            TgsDocument document = await TgsReader.ReadFileAsync(path, new TgsReaderOptions { Expressions = new TgsExpressionOptions() });
            Assert.That(document.UnsupportedFeatures, Is.Empty);
            PixelBuffer image = TgsRenderer.RenderFrame(document, frame, new SvgRenderOptions { Width = 128, Height = 128, Supersampling = 1 });
            Assert.That(image.Pixels.ToArray().Where((_, i) => i % 4 == 3).Any(a => a > 0), Is.True);
        }

        private static string Layer(string expression, string extra = "") => """
            {"ty":4,"ind":1,"ks":{"p":{"k":[0,0],"x":EXPRESSION}},"shapes":[SHAPES]EXTRA}
            """.Replace("EXPRESSION", JsonSerializer.Serialize(expression)).Replace("SHAPES", Rectangle).Replace("EXTRA", extra);
        private static TgsDocument Read(string layers, bool enabled = true) => TgsReader.Read(Encoding.UTF8.GetBytes("{\"w\":100,\"h\":100,\"fr\":30,\"ip\":0,\"op\":60,\"layers\":[" + layers + "]}"), new TgsReaderOptions { Expressions = enabled ? new TgsExpressionOptions() : null });
        private static TgsDocument ReadJson(string json, TgsExpressionOptions? expressions = null) => TgsReader.Read(Encoding.UTF8.GetBytes(json), new TgsReaderOptions { Expressions = expressions ?? new TgsExpressionOptions() });
        private static SvgRenderOptions Options() => new() { Width = 100, Height = 100, Supersampling = 1 };
        private static PixelBuffer Render(string layers, double frame, TgsExpressionOptions? expressions = null) => TgsRenderer.RenderFrame(ReadJson("{\"w\":100,\"h\":100,\"fr\":30,\"ip\":0,\"op\":60,\"layers\":[" + layers + "]}", expressions), frame, Options());
    }
}
