using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Render.NET.Svg.Benchmarks
{
    internal static class SvgBenchmark
    {
        internal static async Task Run(string input, string output, int size, int samples, int warmups)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
            ArgumentOutOfRangeException.ThrowIfNegative(warmups);
            Directory.CreateDirectory(output);
            using StreamWriter results = new(Path.Combine(output, "results.jsonl"));
            await results.WriteLineAsync(JsonSerializer.Serialize(new
            {
                kind = "environment", timestamp = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
                renderNet = typeof(SvgRenderer).Assembly.GetName().Version?.ToString(),
                svgSkia = typeof(global::Svg.Skia.SKSvg).Assembly.GetName().Version?.ToString(),
                skiaSharp = typeof(SkiaSharp.SKSurface).Assembly.GetName().Version?.ToString(),
                size, samples, warmups, supersampling = 1, gpu = false,
                tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                processorCount = Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT")
            }));
            foreach (string path in Directory.GetFiles(input, "*.svg").Order())
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string xml = await File.ReadAllTextAsync(path);
                string hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
                await Save(ManagedRaster(xml, size), Path.Combine(output, name + "-managed.png"));
                await Save(SkiaReference.Raster(xml, size), Path.Combine(output, name + "-skia.png"));
                foreach (string phase in new[] { "fresh_rgba", "total_png" })
                {
                    await Measure(results, name, hash, xml, size, samples, warmups, phase);
                    Console.WriteLine($"{name} {size} {phase} complete");
                }
            }
        }

        private static async Task Measure(StreamWriter output, string name, string hash, string xml, int size, int samples, int warmups, string phase)
        {
            using Process process = Process.GetCurrentProcess();
            for (int iteration = -warmups; iteration < samples; iteration++)
            {
                string[] order = iteration % 2 == 0 ? new[] { "managed", "skia" } : new[] { "skia", "managed" };
                foreach (string engine in order)
                {
                    long allocated = GC.GetTotalAllocatedBytes(true);
                    TimeSpan cpu = process.TotalProcessorTime;
                    Stopwatch watch = Stopwatch.StartNew();
                    long checksum = await Render(engine, phase, xml, size);
                    double milliseconds = watch.Elapsed.TotalMilliseconds;
                    double cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                    allocated = GC.GetTotalAllocatedBytes(true) - allocated;
                    if (iteration >= 0)
                    {
                        await output.WriteLineAsync(JsonSerializer.Serialize(new { kind = "sample", name, size, phase, engine, iteration, milliseconds, cpuMilliseconds, managedAllocatedBytes = allocated, checksum, sha256 = hash }));
                        await output.FlushAsync();
                    }
                }
            }
        }

        private static async Task<long> Render(string engine, string phase, string xml, int size)
        {
            if (engine == "skia")
            {
                return phase == "total_png" ? SkiaReference.Png(xml, size) : Sample(SkiaReference.Raster(xml, size));
            }

            PixelBuffer image = ManagedRaster(xml, size);
            if (phase != "total_png")
            {
                return Sample(image);
            }

            using MemoryStream png = new();
            await PngEncoder.WriteAsync(image, png);
            return png.Length;
        }

        private static PixelBuffer ManagedRaster(string xml, int size)
        {
            SvgDocument document = SvgReader.Read(xml);
            if (document.UnsupportedFeatures.Count != 0)
            {
                throw new InvalidDataException(string.Join("; ", document.UnsupportedFeatures));
            }

            return SvgRenderer.Render(document, new SvgRenderOptions { Width = size, Height = size, Supersampling = 1 });
        }

        private static long Sample(PixelBuffer image) => image[image.Width / 2, image.Height / 2].A;

        private static async Task Save(PixelBuffer image, string path)
        {
            await using Stream file = File.Create(path);
            await PngEncoder.WriteAsync(image, file);
        }
    }
}
