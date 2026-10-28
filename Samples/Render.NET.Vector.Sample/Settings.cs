using System.Diagnostics;
using System.Globalization;
using Render.NET;
using Render.NET.Svg;
using Render.NET.Tgs;

internal record Settings(SvgRenderOptions Options, double? Frame, double? Seconds)
{
    internal static Settings Parse(string[] args)
    {
        SvgRenderOptions? options = new SvgRenderOptions();
        double? frame = null, seconds = null;
        for (int i = 2; i < args.Length; i++)
        {
            string flag = args[i];
            if (flag == "--allow-partial")
            {
                options = options with
                {
                    AllowPartialRendering = true
                };
                continue;
            }

            if (flag == "--opaque")
            {
                options = options with
                {
                    Background = new Rgba32(255, 255, 255)
                };
                continue;
            }

            if (++i >= args.Length)
            {
                throw new ArgumentException("Missing value for " + flag);
            }

            double value = double.Parse(args[i], CultureInfo.InvariantCulture);
            switch (flag)
            {
                case "--frame":
                    frame = value;
                    break;
                case "--time":
                    seconds = value;
                    break;
                case "--width":
                    options = options with
                    {
                        Width = checked((int)value)
                    };
                    break;
                case "--height":
                    options = options with
                    {
                        Height = checked((int)value)
                    };
                    break;
                case "--supersampling":
                    options = options with
                    {
                        Supersampling = checked((int)value)
                    };
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + flag);
            }
        }

        if (frame.HasValue && seconds.HasValue)
        {
            throw new ArgumentException("Specify either --frame or --time.");
        }

        return new Settings(options, frame, seconds);
    }
}
