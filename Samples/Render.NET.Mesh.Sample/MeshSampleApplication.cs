using System.Diagnostics;
using Render.NET;
using Render.NET.Mesh;
using Render.NET.Mesh.Sample;

namespace Render.NET.Mesh.Sample
{
    internal static class MeshSampleApplication
    {
        internal static async System.Threading.Tasks.Task<int> RunAsync(string[] args)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                await Console.Error.WriteLineAsync("Usage: Render.NET.Mesh.Sample <input.stl|input.obj|input.3mf|--demo> <output.png> [size]");
                return 1;
            }

            using CancellationTokenSource cancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            try
            {
                int size = args.Length == 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 768;
                string inputPath = args[0];
                if (inputPath == "--demo")
                {
                    inputPath = Path.ChangeExtension(args[1], ".stl");
                    DemoModel.WriteStl(inputPath);
                }

                Stopwatch timer = Stopwatch.StartNew();
                TriangleMesh mesh = await MeshReader.ReadFileAsync(inputPath, cancellation.Token);
                TimeSpan loadTime = timer.Elapsed;
                PixelBuffer image = MeshRenderer.Render(mesh, new MeshRenderOptions { Width = size, Height = size }, cancellation.Token);
                TimeSpan renderTime = timer.Elapsed - loadTime;
                await using FileStream destination = File.Create(args[1]);
                await PngEncoder.WriteAsync(image, destination, cancellation.Token);
                await Console.Out.WriteLineAsync($"{mesh.TriangleCount:N0} triangles; load {loadTime.TotalMilliseconds:N0} ms; render {renderTime.TotalMilliseconds:N0} ms; PNG {timer.Elapsed.TotalMilliseconds:N0} ms total.");
                await Console.Out.WriteLineAsync(Path.GetFullPath(args[1]));
                return 0;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or OperationCanceledException or FormatException or OverflowException or NotSupportedException)
            {
                await Console.Error.WriteLineAsync(exception.Message);
                return 1;
            }
        }
    }
}
