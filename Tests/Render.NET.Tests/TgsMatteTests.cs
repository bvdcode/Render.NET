using System.Text;
using NUnit.Framework;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Tests
{
    public class TgsMatteTests
    {
        [TestCase(0, 50, 128, 0)]
        [TestCase(1, 100, 0, 255)]
        public void SetMatteUsesHiddenLayerAlphaAndSupportsInversion(int inverse, int opacity, int leftAlpha, int rightAlpha)
        {
            string mask = "{\"ty\":4,\"ind\":2,\"hd\":true,\"ks\":{\"o\":{\"k\":" + opacity + "}},\"shapes\":[{\"ty\":\"rc\",\"p\":{\"k\":[25,50]},\"s\":{\"k\":[50,100]}},{\"ty\":\"fl\",\"c\":{\"k\":[0,0,1]}}]}";
            string target = "{\"ty\":1,\"ind\":1,\"sw\":100,\"sh\":100,\"sc\":\"#ff0000\",\"ef\":[" + Effect(2, inverse) + "]}";
            PixelBuffer image = TgsRenderer.RenderFrame(Read(target + "," + mask), 0, new SvgRenderOptions { Supersampling = 1 });
            Assert.That(image[25, 50].A, Is.EqualTo(leftAlpha));
            Assert.That(image[75, 50].A, Is.EqualTo(rightAlpha));
            if (leftAlpha > 0)
            {
                Assert.That(image[25, 50].R, Is.EqualTo(255));
                Assert.That(image[25, 50].B, Is.Zero);
            }
        }

        [Test]
        public void CyclicSetMatteLayersFailWithinTheNestingBudget()
        {
            string Layer(int index, int source) => "{\"ty\":1,\"ind\":" + index + ",\"sw\":100,\"sh\":100,\"ef\":[" + Effect(source, 0) + "]}";
            Assert.Throws<InvalidDataException>(() => TgsRenderer.RenderFrame(Read(Layer(1, 2) + "," + Layer(2, 1)), 0));
        }

        private static string Effect(int source, int inverse) => "{\"ty\":28,\"ef\":[{\"v\":{\"k\":" + source + "}},{\"v\":{\"k\":4}},{\"v\":{\"k\":" + inverse + "}},{\"v\":{\"k\":1}},{\"v\":{\"k\":1}},{\"v\":{\"k\":1}}]}";

        private static TgsDocument Read(string layers) => TgsReader.Read(Encoding.UTF8.GetBytes("{\"w\":100,\"h\":100,\"fr\":30,\"ip\":0,\"op\":60,\"layers\":[" + layers + "]}"));
    }
}
