using System.Diagnostics;
using System.Globalization;
using Render.NET;
using Render.NET.Svg;
using Render.NET.Tgs;

namespace Render.NET.Vector.Sample
{
    internal static class VectorSampleApplication
    {
        internal static async System.Threading.Tasks.Task<int> RunAsync(string[] args)
        {
            if (args.Length < 2)
            {
                await Console.Error.WriteLineAsync("Usage: Render.NET.Vector.Sample input.svg|input.tgs|input.json output.png [--frame 30|--time 0.5] [--width 512] [--height 512] [--supersampling 2] [--opaque] [--allow-partial]");
                return 1;
            }

            try
            {
                Settings? settings = Settings.Parse(args);
                using CancellationTokenSource? cancellation = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cancellation.Cancel();
                };
                Stopwatch? watch = Stopwatch.StartNew();
                PixelBuffer image = await Render(settings, cancellation.Token);
                await using FileStream? output = File.Create(args[1]);
                await PngEncoder.WriteAsync(image, output, cancellation.Token);
                await Console.Out.WriteLineAsync($"{image.Width} x {image.Height}; {watch.ElapsedMilliseconds} ms; {args[1]}");
                return 0;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Xml.XmlException or System.Text.Json.JsonException or OperationCanceledException or FormatException or OverflowException)
            {
                await Console.Error.WriteLineAsync(ex.Message);
                return 1;
            }

            async Task<PixelBuffer> Render(Settings settings, CancellationToken token)
            {
                if (Path.GetExtension(args[0]).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    SvgDocument doc = await SvgReader.ReadFileAsync(args[0], cancellationToken: token);
                    foreach (string warning in doc.UnsupportedFeatures)
                    {
                        await Console.Error.WriteLineAsync(warning);
                    }

                    return SvgRenderer.Render(doc, settings.Options, token);
                }

                TgsDocument animation = await TgsReader.ReadFileAsync(args[0], cancellationToken: token);
                foreach (string warning in animation.UnsupportedFeatures)
                {
                    await Console.Error.WriteLineAsync(warning);
                }

                double frame = settings.Frame ?? animation.InPoint + ((settings.Seconds ?? 0) * animation.FrameRate);
                await Console.Out.WriteLineAsync($"Frame {frame.ToString(CultureInfo.InvariantCulture)}; {animation.FrameRate} fps; {animation.Duration:F2} s");
                return TgsRenderer.RenderFrame(animation, frame, settings.Options, token);
            }
        }
    }
}
