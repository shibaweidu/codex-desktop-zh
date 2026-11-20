using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace CodexZhLauncher
{
    internal static class ProcessRunner
    {
        public static async Task<ProcessResult> RunAsync(string fileName, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("无法启动进程：" + fileName);
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = await output.ConfigureAwait(false),
                    Error = await error.ConfigureAwait(false)
                };
            }
        }

        public static string PowerShellLiteral(string value)
        {
            return "'" + (value ?? String.Empty).Replace("'", "''") + "'";
        }

        public static string QuoteArgument(string value)
        {
            return "\"" + (value ?? String.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}

