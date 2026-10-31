using System.Globalization;

namespace Render.NET.DevelopmentTools
{
    public class ComparisonOptions
    {
        public List<int> Sizes { get; } = [512, 1024];
        public int Repeats { get; private set; } = 5;
        public string Output { get; private set; } = "artifacts/benchmark";
        public string F3d { get; private set; } = "f3d";
        public bool Xvfb { get; private set; }

        public static ComparisonOptions Parse(string[] arguments)
        {
            ComparisonOptions options = new();
            for (int i = 0; i < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case "--sizes":
                        options.Sizes.Clear();
                        while (i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            options.Sizes.Add(int.Parse(arguments[++i], CultureInfo.InvariantCulture));
                        }
                        break;
                    case "--repeats":
                        options.Repeats = int.Parse(arguments[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--output":
                        options.Output = arguments[++i];
                        break;
                    case "--f3d":
                        options.F3d = arguments[++i];
                        break;
                    case "--xvfb":
                        options.Xvfb = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown comparison option: {arguments[i]}");
                }
            }
            if (options.Repeats < 2 || options.Sizes.Count == 0 || options.Sizes.Any(size => size <= 0))
            {
                throw new ArgumentException("Specify positive image sizes and at least two measured repetitions.");
            }
            return options;
        }
    }
}
