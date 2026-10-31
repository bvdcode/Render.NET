using System.Diagnostics;

namespace Render.NET.DevelopmentTools
{
    public static class ProcessRunner
    {
        public static async Task<ProcessResult> RunAsync(IReadOnlyList<string> command, string directory, IReadOnlyDictionary<string, string>? environment = null)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
            ProcessStartInfo start = new(command[0])
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in command.Skip(1))
            {
                start.ArgumentList.Add(argument);
            }
            if (environment is not null)
            {
                foreach ((string key, string value) in environment)
                {
                    start.Environment[key] = value;
                }
            }

            using Process process = new() { StartInfo = start };
            Stopwatch timer = Stopwatch.StartNew();
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                double elapsed = timer.Elapsed.TotalMilliseconds;
                string stdout = await output;
                string stderr = await error;
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"{command[0]} failed ({process.ExitCode}): {stdout}\n{stderr}");
                }
                return new ProcessResult(elapsed, stdout, stderr);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
    }
}
