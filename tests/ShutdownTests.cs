using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CodexZhLauncher
{
    internal static class ShutdownTests
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1 || !File.Exists(args[0]))
            {
                Console.Error.WriteLine("Fixture executable is required.");
                return 2;
            }

            var root = Path.Combine(Path.GetTempPath(), "CodexZhShutdownTests_" + Guid.NewGuid().ToString("N"));
            var targetDirectory = Path.Combine(root, "target");
            var foreignDirectory = Path.Combine(root, "foreign");
            Directory.CreateDirectory(targetDirectory);
            Directory.CreateDirectory(foreignDirectory);
            var targetExecutable = Path.Combine(targetDirectory, "Codex.exe");
            var secondaryTargetExecutable = Path.Combine(targetDirectory, "ChatGPT.exe");
            var foreignExecutable = Path.Combine(foreignDirectory, "ChatGPT.exe");
            File.Copy(args[0], targetExecutable);
            File.Copy(args[0], secondaryTargetExecutable);
            File.Copy(args[0], foreignExecutable);

            Process foreign = null;
            Process target = null;
            Process spawned = null;
            try
            {
                var install = new CodexInstall
                {
                    Kind = "测试便携版",
                    DisplayName = "Codex test fixture",
                    Version = "test",
                    InstallLocation = targetDirectory,
                    ExecutablePath = targetExecutable
                };

                Assert(CodexProcessManager.IsPathOwnedByInstall(install, targetExecutable), "target executable should match");
                Assert(CodexProcessManager.IsPathOwnedByInstall(install, Path.Combine(targetDirectory, "resources", "codex.exe")), "resource child should match");
                Assert(!CodexProcessManager.IsPathOwnedByInstall(install, foreignExecutable), "foreign executable must not match");
                Assert(!CodexProcessManager.IsPathOwnedByInstall(install, Path.Combine(targetDirectory, "Other.exe")), "unexpected filename must not match");
                Console.WriteLine("path_filter=ok");

                foreign = StartFixture(foreignExecutable, "resist");
                target = StartFixture(targetExecutable, "graceful");
                var scan = CodexProcessManager.Scan(install);
                Assert(scan.Verified.Count == 1, "only the target-directory process should be verified");
                var graceful = CodexProcessManager.ShutdownAsync(
                    install,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2),
                    Console.WriteLine).GetAwaiter().GetResult();
                Assert(graceful.Success, "graceful shutdown should succeed");
                Assert(graceful.GracefulExited >= 1, "graceful process should exit before force phase");
                Assert(graceful.ForceAttempted == 0, "graceful process must not be force killed");
                foreign.Refresh();
                Assert(!foreign.HasExited, "foreign same-name process must remain running");
                target.Dispose();
                target = null;
                Console.WriteLine("graceful_shutdown=ok");

                target = StartFixture(targetExecutable, "resist");
                var forced = CodexProcessManager.ShutdownAsync(
                    install,
                    TimeSpan.FromMilliseconds(300),
                    TimeSpan.FromSeconds(3),
                    Console.WriteLine).GetAwaiter().GetResult();
                Assert(forced.Success, "forced shutdown should succeed");
                Assert(forced.ForceAttempted >= 1, "resistant process should reach force phase");
                Assert(forced.ForceTerminated >= 1, "resistant process should be force terminated");
                foreign.Refresh();
                Assert(!foreign.HasExited, "foreign process must survive force phase");
                target.Dispose();
                target = null;
                Console.WriteLine("forced_shutdown=ok");

                target = StartFixture(targetExecutable, "resist");
                var respawnShutdown = CodexProcessManager.ShutdownAsync(
                    install,
                    TimeSpan.FromMilliseconds(800),
                    TimeSpan.FromSeconds(3),
                    Console.WriteLine);
                Thread.Sleep(200);
                spawned = StartFixture(secondaryTargetExecutable, "resist");
                var respawned = respawnShutdown.GetAwaiter().GetResult();
                Assert(respawned.Success, "process spawned during graceful wait should also be closed");
                Assert(respawned.ForceTerminated >= 2, "both original and spawned processes should be terminated");
                foreign.Refresh();
                Assert(!foreign.HasExited, "foreign process must survive respawn handling");
                target.Dispose();
                target = null;
                spawned.Dispose();
                spawned = null;
                Console.WriteLine("respawn_during_wait=ok");

                target = StartFixture(targetExecutable, "autoexit");
                var natural = CodexProcessManager.ShutdownAsync(
                    install,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2),
                    Console.WriteLine).GetAwaiter().GetResult();
                Assert(natural.Success, "naturally exiting process should be treated as success");
                target.Dispose();
                target = null;
                Console.WriteLine("natural_exit=ok");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                StopFixture(target);
                StopFixture(spawned);
                StopFixture(foreign);
                TryDeleteDirectory(root);
            }
        }

        private static Process StartFixture(string executable, string mode)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = mode,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
            });
            if (process == null) throw new InvalidOperationException("Unable to start fixture: " + executable);
            process.WaitForInputIdle(5000);
            Thread.Sleep(100);
            return process;
        }

        private static void StopFixture(Process process)
        {
            if (process == null) return;
            try
            {
                process.Refresh();
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }
    }
}
