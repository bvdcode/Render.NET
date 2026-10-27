using Render.NET.Svg.Benchmarks;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SVG-input-directory output-directory [size=512] [samples=11] [warmups=20]");
    return 1;
}

await SvgBenchmark.Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]),
    args.Length > 2 ? int.Parse(args[2]) : 512,
    args.Length > 3 ? int.Parse(args[3]) : 11,
    args.Length > 4 ? int.Parse(args[4]) : 20);
return 0;
