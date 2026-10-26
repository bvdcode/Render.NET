using System.Diagnostics;
using System.Text.Json;
using Render.NET;
using Render.NET.Mesh;

if (args.Length != 5)
{
    Console.Error.WriteLine("Usage: benchmark <input> <output.png> <size> <iterations> <quality|fast>");
    return 1;
}

int size = int.Parse(args[2]);
int iterations = int.Parse(args[3]);
bool quality = args[4] == "quality";
for (int iteration = 0; iteration < iterations; iteration++)
{
    Stopwatch timer = Stopwatch.StartNew();
    TriangleMesh mesh = await MeshReader.ReadFileAsync(args[0]);
    double load = timer.Elapsed.TotalMilliseconds;
    PixelBuffer image = MeshRenderer.Render(mesh, new MeshRenderOptions
    {
        Width = size,
        Height = size,
        Supersampling = quality ? 2 : 1,
        Shadows = quality,
        AmbientOcclusion = quality
    });
    double renderEnd = timer.Elapsed.TotalMilliseconds;
    await using (FileStream destination = File.Create(args[1]))
    {
        await PngEncoder.WriteAsync(image, destination);
    }

    double total = timer.Elapsed.TotalMilliseconds;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        iteration, triangles = mesh.TriangleCount, loadMs = load, renderMs = renderEnd - load,
        pngMs = total - renderEnd, totalMs = total
    }));
}

return 0;
