using System.Diagnostics;
using System.Globalization;
using Render.NET;
using Render.NET.Svg;
using Render.NET.Tgs;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Render.NET.Vector.Sample input.svg|input.tgs|input.json output.png [--frame 30|--time 0.5] [--width 512] [--height 512] [--supersampling 2] [--opaque] [--allow-partial]");
    return 1;
}

try
{
    var settings = Settings.Parse(args);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var watch = Stopwatch.StartNew();
    PixelBuffer image = await Render(settings, cancellation.Token);
    await using var output = File.Create(args[1]);
    await PngEncoder.WriteAsync(image, output, cancellation.Token);
    Console.WriteLine($"{image.Width} x {image.Height}; {watch.ElapsedMilliseconds} ms; {args[1]}");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Xml.XmlException or System.Text.Json.JsonException or OperationCanceledException or FormatException or OverflowException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

async Task<PixelBuffer> Render(Settings settings, CancellationToken token)
{
    if (Path.GetExtension(args[0]).Equals(".svg", StringComparison.OrdinalIgnoreCase))
    {
        SvgDocument doc = await SvgReader.ReadFileAsync(args[0], cancellationToken: token);
        foreach (string warning in doc.UnsupportedFeatures) { Console.Error.WriteLine(warning); }
        return SvgRenderer.Render(doc, settings.Options, token);
    }
    TgsDocument animation = await TgsReader.ReadFileAsync(args[0], cancellationToken: token);
    foreach (string warning in animation.UnsupportedFeatures) { Console.Error.WriteLine(warning); }
    double frame = settings.Frame ?? animation.InPoint + ((settings.Seconds ?? 0) * animation.FrameRate);
    Console.WriteLine($"Frame {frame.ToString(CultureInfo.InvariantCulture)}; {animation.FrameRate} fps; {animation.Duration:F2} s");
    return TgsRenderer.RenderFrame(animation, frame, settings.Options, token);
}

internal sealed record Settings(SvgRenderOptions Options, double? Frame, double? Seconds)
{
    internal static Settings Parse(string[] args)
    {
        var options = new SvgRenderOptions();
        double? frame = null, seconds = null;
        for (int i = 2; i < args.Length; i++)
        {
            string flag = args[i];
            if (flag == "--allow-partial") { options = options with { AllowPartialRendering = true }; continue; }
            if (flag == "--opaque") { options = options with { Background = new Rgba32(255, 255, 255) }; continue; }
            if (++i >= args.Length) { throw new ArgumentException("Missing value for " + flag); }
            double value = double.Parse(args[i], CultureInfo.InvariantCulture);
            switch (flag)
            {
                case "--frame": frame = value; break;
                case "--time": seconds = value; break;
                case "--width": options = options with { Width = checked((int)value) }; break;
                case "--height": options = options with { Height = checked((int)value) }; break;
                case "--supersampling": options = options with { Supersampling = checked((int)value) }; break;
                default: throw new ArgumentException("Unknown option: " + flag);
            }
        }
        if (frame.HasValue && seconds.HasValue) { throw new ArgumentException("Specify either --frame or --time."); }
        return new Settings(options, frame, seconds);
    }
}
