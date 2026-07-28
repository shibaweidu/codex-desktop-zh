using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace CodexZhLauncher
{
    internal static class ProcessRunner
    {
        public static Task<ProcessResult> RunAsync(string fileName, string arguments)
        {
            var completion = new TaskCompletionSource<ProcessResult>();
            var output = new StringBuilder();
            var error = new StringBuilder();
            var process = new Process();

            process.StartInfo = new ProcessStartInfo
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
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null) output.AppendLine(args.Data);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null) error.AppendLine(args.Data);
            };
            process.Exited += delegate
            {
                process.WaitForExit();
                completion.TrySetResult(new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = output.ToString(),
                    Error = error.ToString()
                });
                process.Dispose();
            };

            try
            {
                if (!process.Start())
                {
                    completion.SetException(new InvalidOperationException("无法启动进程：" + fileName));
                }
                else
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
            }
            catch (Exception ex)
            {
                process.Dispose();
                completion.SetException(ex);
            }

            return completion.Task;
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

