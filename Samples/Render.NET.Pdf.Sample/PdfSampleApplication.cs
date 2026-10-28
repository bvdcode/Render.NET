using System.Diagnostics;
using System.Globalization;
using Render.NET;
using Render.NET.Pdf;

namespace Render.NET.Pdf.Sample
{
    internal static class PdfSampleApplication
    {
        internal static async System.Threading.Tasks.Task<int> RunAsync(string[] args)
        {
            if (args.Length < 2)
            {
                await Console.Error.WriteLineAsync("Usage: Render.NET.Pdf.Sample input.pdf output.png [--page 1] [--dpi 96] [--width 1200] [--supersampling 2] [--transparent] [--allow-partial]");
                return 1;
            }

            try
            {
                int pageNumber = Integer("--page", 1);
                PdfRenderOptions? options = new PdfRenderOptions
                {
                    Dpi = Integer("--dpi", 96),
                    MaximumWidth = Array.IndexOf(args, "--width") >= 0 ? Integer("--width", 1200) : null,
                    Supersampling = Integer("--supersampling", 2),
                    Background = args.Contains("--transparent") ? default : new Rgba32(255, 255, 255),
                    AllowPartialRendering = args.Contains("--allow-partial")
                };
                Stopwatch? timer = Stopwatch.StartNew();
                using PdfDocument document = await PdfReader.ReadFileAsync(args[0]);
                PdfPage page = document.GetPage(pageNumber);
                foreach (string feature in page.UnsupportedFeatures)
                {
                    await Console.Error.WriteLineAsync("Unsupported: " + feature);
                }

                PixelBuffer image = PdfRenderer.Render(page, options);
                await using FileStream output = File.Create(args[1]);
                await PngEncoder.WriteAsync(image, output);
                await Console.Out.WriteLineAsync($"Page {pageNumber}/{document.PageCount}: {image.Width} x {image.Height}, {timer.Elapsed.TotalMilliseconds:F0} ms; substituted fonts: {string.Join(", ", page.SubstitutedFonts)}");
                return 0;
            }
            catch (Exception error)
            {
                await Console.Error.WriteLineAsync(error.Message);
                return 2;
            }

            int Integer(string name, int fallback)
            {
                int index = Array.IndexOf(args, name);
                return index < 0 ? fallback : int.Parse(args[index + 1], CultureInfo.InvariantCulture);
            }
        }
    }
}
