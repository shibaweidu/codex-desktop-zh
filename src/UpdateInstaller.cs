using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexZhLauncher
{
    internal static class UpdateInstaller
    {
        private const int MaxExecutableBytes = 50 * 1024 * 1024;
        private const int MaxChecksumBytes = 1024 * 1024;

        public static async Task<bool> PrepareAndLaunchAsync(
            UpdateCheckResult update,
            Action<string> progress)
        {
            if (update == null || !update.UpdateAvailable ||
                String.IsNullOrWhiteSpace(update.AssetUrl) ||
                String.IsNullOrWhiteSpace(update.ChecksumUrl))
                throw new InvalidOperationException("没有可安装的更新。");

            var targetPath = Process.GetCurrentProcess().MainModule.FileName;
            EnsureWritableTarget(targetPath);
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexZhLauncher",
                "updates",
                update.LatestVersion + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var stagedPath = Path.Combine(root, "CodexZhLauncher.Update.exe");

            progress("正在下载 v" + update.LatestVersion + "...");
            var executable = await DownloadAsync(update.AssetUrl, MaxExecutableBytes);
            if (update.AssetSize > 0 && executable.LongLength != update.AssetSize)
                throw new InvalidOperationException("更新文件大小与 GitHub Release 不一致。");
            var checksums = await DownloadAsync(update.ChecksumUrl, MaxChecksumBytes);
            var expectedHash = ParseExpectedHash(Encoding.UTF8.GetString(checksums), update.AssetName);
            var actualHash = ComputeSha256(executable);
            if (!String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("更新文件 SHA-256 校验失败。");

            File.WriteAllBytes(stagedPath, executable);
            var stagedVersion = FileVersionInfo.GetVersionInfo(stagedPath).FileVersion;
            if (!VersionMatches(stagedVersion, update.LatestVersion))
                throw new InvalidOperationException("更新文件版本与 GitHub Release 不一致。");

            progress("校验完成，正在准备覆盖并重启...");
            var current = Process.GetCurrentProcess();
            var arguments = String.Join(" ", new[]
            {
                "--apply-update-windows",
                current.Id.ToString(CultureInfo.InvariantCulture),
                current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                Quote(targetPath),
                Quote(root),
                expectedHash
            });
            Process.Start(new ProcessStartInfo
            {
                FileName = stagedPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = root
            });
            return true;
        }

        public static int ApplyWindowsUpdate(string[] args)
        {
            if (args.Length != 6) return 2;
            try
            {
                int parentId;
                long parentStartTicks;
                if (!Int32.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out parentId) ||
                    !Int64.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out parentStartTicks))
                    throw new InvalidOperationException("更新进程参数无效。");

                WaitForOriginalProcess(parentId, parentStartTicks);
                var sourcePath = Process.GetCurrentProcess().MainModule.FileName;
                var targetPath = Path.GetFullPath(args[3]);
                var updateRoot = Path.GetFullPath(args[4]);
                var expectedHash = args[5];
                EnsureUpdaterPaths(sourcePath, targetPath, updateRoot);
                var backupPath = targetPath + ".update-backup";
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Copy(targetPath, backupPath, true);
                try
                {
                    CopyWithRetry(sourcePath, targetPath);
                    if (!String.Equals(ComputeSha256(File.ReadAllBytes(targetPath)), expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("覆盖后的更新文件校验失败。");
                    StartUpdatedApplication(targetPath, updateRoot);
                    TryDelete(backupPath);
                    return 0;
                }
                catch
                {
                    if (File.Exists(backupPath)) CopyWithRetry(backupPath, targetPath);
                    throw;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("update.apply.failed " + ex);
                try
                {
                    if (args.Length > 4 && File.Exists(args[3]))
                        StartUpdatedApplication(args[3], args[4]);
                }
                catch { }
                return 1;
            }
        }

        public static void ScheduleCleanup()
        {
            Task.Run(delegate
            {
                Thread.Sleep(5000);
                try
                {
                    var updatesRoot = Path.GetFullPath(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CodexZhLauncher",
                        "updates"));
                    var requested = Environment.GetEnvironmentVariable("CODEX_ZH_CLEANUP_DIR");
                    if (String.IsNullOrWhiteSpace(requested)) return;
                    var fullPath = Path.GetFullPath(requested);
                    if (!fullPath.StartsWith(updatesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        !Directory.Exists(fullPath)) return;
                    try { Directory.Delete(fullPath, true); }
                    catch { }
                }
                catch { }
            });
        }

        internal static string ParseExpectedHash(string contents, string assetName)
        {
            foreach (var rawLine in (contents ?? String.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                var separator = line.IndexOfAny(new[] { ' ', '\t' });
                if (separator <= 0) continue;
                var hash = line.Substring(0, separator).Trim();
                var name = line.Substring(separator).Trim().TrimStart('*');
                if (String.Equals(name, assetName, StringComparison.Ordinal) &&
                    hash.Length == 64 && IsHex(hash)) return hash.ToUpperInvariant();
            }
            throw new InvalidOperationException("SHA256SUMS.txt 中没有目标更新文件。");
        }

        internal static string SelfTest()
        {
            var hash = new String('A', 64);
            var parsed = ParseExpectedHash(hash + "  Codex-Zh-Launcher-Windows-x64.exe\n", "Codex-Zh-Launcher-Windows-x64.exe");
            if (parsed != hash) throw new InvalidOperationException("更新校验文件解析自检失败。");
            return "update-installer=ok";
        }

        private static async Task<byte[]> DownloadAsync(string url, int maxBytes)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(90);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexZhLauncher/" + AppInfo.Version);
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > maxBytes)
                    throw new InvalidOperationException("更新文件超过允许的大小。");
                var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (data.Length == 0 || data.Length > maxBytes)
                    throw new InvalidOperationException("更新文件大小无效。");
                return data;
            }
        }

        private static void EnsureWritableTarget(string targetPath)
        {
            if (String.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath) ||
                !String.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前程序路径无法用于自动更新。");
            var directory = Path.GetDirectoryName(targetPath);
            var probe = Path.Combine(directory, ".codex-zh-update-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllText(probe, "probe", Encoding.ASCII); }
            catch (Exception ex) { throw new InvalidOperationException("当前目录没有覆盖更新权限。", ex); }
            finally { TryDelete(probe); }
        }

        private static void EnsureUpdaterPaths(string sourcePath, string targetPath, string updateRoot)
        {
            var localRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexZhLauncher",
                "updates")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(sourcePath).StartsWith(localRoot, StringComparison.OrdinalIgnoreCase) ||
                !updateRoot.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(sourcePath).StartsWith(updateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(targetPath))
                throw new InvalidOperationException("更新路径验证失败。");
        }

        private static void WaitForOriginalProcess(int processId, long startTicks)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process.StartTime.ToUniversalTime().Ticks != startTicks) return;
                if (!process.WaitForExit(30000)) throw new TimeoutException("等待旧版本退出超时。");
            }
            catch (ArgumentException) { }
        }

        private static void CopyWithRetry(string source, string target)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try { File.Copy(source, target, true); return; }
                catch (Exception ex) { last = ex; Thread.Sleep(250); }
            }
            throw new IOException("无法覆盖更新文件。", last);
        }

        private static void StartUpdatedApplication(string targetPath, string updateRoot)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(targetPath)
            };
            startInfo.EnvironmentVariables["CODEX_ZH_CLEANUP_DIR"] = updateRoot;
            Process.Start(startInfo);
        }

        private static string ComputeSha256(byte[] data)
        {
            using (var algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(data)).Replace("-", String.Empty);
            }
        }

        private static bool VersionMatches(string fileVersion, string releaseVersion)
        {
            Version file;
            Version release;
            return Version.TryParse(fileVersion, out file) && Version.TryParse(releaseVersion, out release) &&
                file.Major == release.Major && file.Minor == release.Minor && file.Build == release.Build;
        }

        private static bool IsHex(string value)
        {
            foreach (var character in value)
            {
                if (!Uri.IsHexDigit(character)) return false;
            }
            return true;
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '\"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('\"');
                    backslashes = 0;
                    continue;
                }
                result.Append('\\', backslashes);
                backslashes = 0;
                result.Append(character);
            }
            result.Append('\\', backslashes * 2);
            result.Append('\"');
            return result.ToString();
        }

        private static void TryDelete(string path)
        {
            try { if (!String.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
