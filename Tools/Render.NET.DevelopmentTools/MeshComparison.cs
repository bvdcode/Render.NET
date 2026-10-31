using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Render.NET.DevelopmentTools
{
    internal static class MeshComparison
    {
        private static readonly string[] Models = ["spot_triangulated.obj", "mug.3mf", "mech.3mf", "dog.3mf", "benchy.stl"];
        private static readonly Dictionary<string, string> GraphicsEnvironment = new()
        {
            ["LIBGL_ALWAYS_SOFTWARE"] = "1",
            ["MESA_LOADER_DRIVER_OVERRIDE"] = "llvmpipe",
            ["GALLIUM_DRIVER"] = "llvmpipe"
        };

        internal static async Task RunAsync(ComparisonOptions options)
        {
            string root = Directory.GetCurrentDirectory();
            string project = Path.Combine(root, "Benchmarks/Render.NET.Mesh.Benchmarks/Render.NET.Mesh.Benchmarks.csproj");
            string dll = Path.Combine(Path.GetDirectoryName(project)!, "bin/Release/net10.0/Render.NET.Mesh.Benchmarks.dll");
            string output = Path.GetFullPath(options.Output);
            Directory.CreateDirectory(output);
            await ProcessRunner.RunAsync(["dotnet", "build", project, "-c", "Release"], root);
            ProcessResult dotnet = await ExecuteAsync(["dotnet", "--version"], root);
            ProcessResult f3d = await ExecuteAsync([options.F3d, "--version"], root);
            JsonArray results = [];
            byte[] manifest = await File.ReadAllBytesAsync(Path.Combine(root, "Tests/Fixtures/manifest.json"));
            JsonObject report = new()
            {
                ["utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["platform"] = RuntimeInformation.OSDescription,
                ["cpuArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["logicalCpus"] = Environment.ProcessorCount,
                ["dotnet"] = dotnet.Output.Trim(),
                ["f3d"] = f3d.Output,
                ["softwareGraphics"] = true,
                ["xvfbPerInvocation"] = options.Xvfb,
                ["repeats"] = options.Repeats,
                ["inputManifestSha256"] = Convert.ToHexStringLower(SHA256.HashData(manifest)),
                ["results"] = results
            };
            foreach (string model in Models)
            {
                string source = Path.Combine(root, "Tests/Fixtures", model);
                foreach (int size in options.Sizes)
                {
                    JsonObject profiles = [];
                    JsonObject row = new() { ["model"] = model, ["size"] = size, ["profiles"] = profiles };
                    foreach (string profile in new[] { "fast", "quality" })
                    {
                        string stem = $"{Path.GetFileNameWithoutExtension(source)}-{size}-{profile}";
                        string managedPng = Path.Combine(output, $"{stem}-managed.png");
                        string f3dPng = Path.Combine(output, $"{stem}-f3d.png");
                        string[] managed = ["dotnet", dll, source, managedPng, size.ToString(CultureInfo.InvariantCulture), "1", profile];
                        string[] native = NativeCommand(options, source, f3dPng, size, profile);
                        JsonObject samples = await MeasureAsync(root, managed, native, options.Repeats);
                        JsonArray stages = samples["managedStages"]!.AsArray();
                        row["triangles"] = stages[0]!["triangles"]!.GetValue<int>();
                        profiles[profile] = samples;
                        await PngMetadata.StripAsync(managedPng);
                        await PngMetadata.StripAsync(f3dPng);
                        double managedMedian = Median(samples["managedProcessMs"]!.AsArray().Select(item => item!.GetValue<double>()));
                        double nativeMedian = Median(samples["f3dProcessMs"]!.AsArray().Select(item => item!.GetValue<double>()));
                        double warmMedian = Median(samples["managedWarmStages"]!.AsArray().Select(item => item!["totalMs"]!.GetValue<double>()));
                        await Console.Out.WriteLineAsync($"{model} {size}px {profile}: managed process {managedMedian:F0} ms; warm {warmMedian:F0} ms; F3D process {nativeMedian:F0} ms");
                    }
                    results.Add(row);
                    await File.WriteAllTextAsync(Path.Combine(output, "results.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                }
            }
        }

        private static async Task<JsonObject> MeasureAsync(string root, string[] managed, string[] native, int repeats)
        {
            await ExecuteAsync(managed, root);
            await ExecuteAsync(native, root);
            JsonArray managedTimes = [];
            JsonArray nativeTimes = [];
            JsonArray stages = [];
            JsonObject samples = new()
            {
                ["managedProcessMs"] = managedTimes,
                ["f3dProcessMs"] = nativeTimes,
                ["managedStages"] = stages
            };
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                bool[] order = repeat % 2 == 0 ? [true, false] : [false, true];
                foreach (bool isManaged in order)
                {
                    ProcessResult result = await ExecuteAsync(isManaged ? managed : native, root);
                    if (isManaged)
                    {
                        managedTimes.Add(result.ElapsedMilliseconds);
                        string lastLine = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
                        stages.Add(JsonNode.Parse(lastLine)!.AsObject());
                    }
                    else
                    {
                        nativeTimes.Add(result.ElapsedMilliseconds);
                        if (!string.IsNullOrWhiteSpace(result.Error))
                        {
                            samples["f3dWarnings"] = result.Error;
                        }
                    }
                }
            }
            string[] warm = (string[])managed.Clone();
            warm[^2] = (repeats + 2).ToString(CultureInfo.InvariantCulture);
            ProcessResult warmResult = await ExecuteAsync(warm, root);
            JsonArray warmStages = [];
            foreach (string line in warmResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(2))
            {
                warmStages.Add(JsonNode.Parse(line)!.AsObject());
            }
            if (warmStages.Count != repeats)
            {
                throw new InvalidDataException("The managed benchmark returned an unexpected number of frame samples.");
            }
            samples["managedWarmStages"] = warmStages;
            samples["commands"] = new JsonObject { ["managed"] = CommandJson(managed), ["f3d"] = CommandJson(native) };
            return samples;
        }

        private static string[] NativeCommand(ComparisonOptions options, string source, string png, int size, string profile)
        {
            List<string> command = [options.F3d, "--no-config", $"--input={source}", $"--output={png}",
                $"--resolution={size},{size}", "--color=0.55,0.65,0.88", "--roughness=0.32", "--metallic=0.15",
                "--no-background", "--camera-direction=-1.6,2.4,-1.5", "--camera-view-up=0,0,1", "--up=+Z", "--camera-orthographic"];
            switch (profile)
            {
                case "quality":
                    command.AddRange(["--ambient-occlusion", "--anti-aliasing=fxaa", "--tone-mapping"]);
                    break;
                case "fast":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile));
            }
            if (options.Xvfb)
            {
                command.Add("--rendering-backend=glx");
                command.InsertRange(0, ["xvfb-run", "-a", "-s", "-screen 0 1024x768x24"]);
            }
            return command.ToArray();
        }

        private static JsonArray CommandJson(string[] command)
        {
            JsonArray array = [];
            foreach (string argument in command)
            {
                array.Add(argument);
            }
            return array;
        }

        private static double Median(IEnumerable<double> values)
        {
            double[] sorted = values.Order().ToArray();
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
        }

        private static Task<ProcessResult> ExecuteAsync(string[] command, string root) => ProcessRunner.RunAsync(command, root, GraphicsEnvironment);
    }
}
