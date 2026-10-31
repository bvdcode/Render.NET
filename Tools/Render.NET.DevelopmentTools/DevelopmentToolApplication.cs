namespace Render.NET.DevelopmentTools
{
    internal static class DevelopmentToolApplication
    {
        internal static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 0)
            {
                await Console.Error.WriteLineAsync("Usage: release-notes <repository> <version> <commit> <packages> <output> | compare [--sizes <pixels> ...] [--repeats <count>] [--f3d <path>] [--output <directory>] [--xvfb]");
                return 1;
            }

            switch (args[0])
            {
                case "release-notes" when args.Length == 6:
                    string body = await ReleaseNotesGenerator.GenerateAsync(args[1], args[2], args[3], args[4], Directory.GetCurrentDirectory());
                    await File.WriteAllTextAsync(args[5], body);
                    return 0;
                case "compare":
                    await MeshComparison.RunAsync(ComparisonOptions.Parse(args[1..]));
                    return 0;
                default:
                    throw new ArgumentException("Unknown command or invalid arguments.", nameof(args));
            }
        }
    }
}
