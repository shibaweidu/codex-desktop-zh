using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodexZhLauncher
{
    internal sealed class CodexProcessInfo
    {
        public int Id;
        public string Name;
        public string ExecutablePath;
        public long StartTimeUtcTicks;
        public bool HasMainWindow;
    }

    internal sealed class CodexProcessScan
    {
        public readonly List<CodexProcessInfo> Verified = new List<CodexProcessInfo>();
        public readonly List<int> UnverifiedCandidateIds = new List<int>();

        public int TotalPotentialCount
        {
            get { return Verified.Count + UnverifiedCandidateIds.Count; }
        }
    }

    internal sealed class CodexShutdownReport
    {
        public int InitialCount;
        public int GracefulRequested;
        public int GracefulExited;
        public int ForceAttempted;
        public int ForceTerminated;
        public int RemainingCount;
        public readonly List<int> FailedProcessIds = new List<int>();
        public readonly List<string> Errors = new List<string>();

        public bool Success
        {
            get { return RemainingCount == 0; }
        }
    }

    internal static class CodexProcessManager
    {
        private static readonly string[] CandidateNames = { "Codex", "ChatGPT" };

        public static CodexProcessScan Scan(CodexInstall install)
        {
            var scan = new CodexProcessScan();
            if (install == null) return scan;

            var seen = new HashSet<int>();
            foreach (var name in CandidateNames)
            {
                Process[] candidates;
                try
                {
                    candidates = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                foreach (var process in candidates)
                {
                    try
                    {
                        if (!seen.Add(process.Id)) continue;
                        CodexProcessInfo info;
                        bool pathUnavailable;
                        if (TryCreateVerifiedInfo(install, process, out info, out pathUnavailable))
                            scan.Verified.Add(info);
                        else if (pathUnavailable)
                            scan.UnverifiedCandidateIds.Add(process.Id);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            scan.Verified.Sort(delegate(CodexProcessInfo left, CodexProcessInfo right)
            {
                return left.Id.CompareTo(right.Id);
            });
            scan.UnverifiedCandidateIds.Sort();
            return scan;
        }

        public static int CountVerifiedRunning(CodexInstall install)
        {
            return Scan(install).Verified.Count;
        }

        public static async Task<CodexShutdownReport> ShutdownAsync(
            CodexInstall install,
            TimeSpan gracefulTimeout,
            TimeSpan forceTimeout,
            Action<string> onProgress)
        {
            if (install == null || !install.IsValid)
                throw new InvalidOperationException("未检测到可用的 Codex Desktop。");
            if (gracefulTimeout < TimeSpan.Zero || forceTimeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("关闭等待时间不能为负数。");

            var report = new CodexShutdownReport();
            var initial = Scan(install);
            report.InitialCount = initial.TotalPotentialCount;
            AddUnverifiedFailures(initial, report, "无法读取候选进程路径，已拒绝关闭");
            Report(onProgress, "已识别 " + initial.Verified.Count + " 个可安全关闭的 Codex 进程。");

            if (initial.Verified.Count == 0)
            {
                report.RemainingCount = initial.UnverifiedCandidateIds.Count;
                return report;
            }

            foreach (var snapshot in initial.Verified.Where(delegate(CodexProcessInfo item) { return item.HasMainWindow; }))
            {
                Process process;
                string error;
                if (!TryOpenRevalidatedProcess(install, snapshot, out process, out error))
                {
                    if (!String.IsNullOrWhiteSpace(error)) AddFailure(report, snapshot.Id, error);
                    continue;
                }
                using (process)
                {
                    try
                    {
                        if (process.CloseMainWindow()) report.GracefulRequested += 1;
                    }
                    catch (Exception ex)
                    {
                        AddFailure(report, snapshot.Id, "请求正常退出失败：" + ex.Message);
                    }
                }
            }

            if (report.GracefulRequested > 0)
                Report(onProgress, "已请求 Codex 正常退出，最多等待 " + FormatSeconds(gracefulTimeout) + " 秒。");
            else
                Report(onProgress, "未找到可响应关闭请求的主窗口，将在等待后处理后台进程。");

            await WaitForVerifiedProcessesAsync(install, gracefulTimeout);
            var afterGraceful = Scan(install);
            report.GracefulExited = CountSnapshotsNoLongerRunning(initial.Verified, afterGraceful.Verified);
            if (afterGraceful.Verified.Count == 0)
            {
                AddUnverifiedFailures(afterGraceful, report, "关闭后仍存在路径不可验证的候选进程");
                report.RemainingCount = afterGraceful.UnverifiedCandidateIds.Count;
                if (report.RemainingCount == 0)
                    Report(onProgress, "Codex 已正常退出。 ");
                else
                    Report(onProgress, "仍有 " + report.RemainingCount + " 个路径不可验证的候选进程，已停止自动重启。 ");
                return report;
            }

            Report(onProgress, "正常退出等待结束，准备强制终止 " + afterGraceful.Verified.Count + " 个剩余进程。");
            var forceSnapshots = new List<CodexProcessInfo>(afterGraceful.Verified);
            var forceRequested = new List<CodexProcessInfo>();
            foreach (var snapshot in forceSnapshots)
            {
                Process process;
                string error;
                if (!TryOpenRevalidatedProcess(install, snapshot, out process, out error))
                {
                    if (!String.IsNullOrWhiteSpace(error)) AddFailure(report, snapshot.Id, error);
                    continue;
                }
                using (process)
                {
                    try
                    {
                        report.ForceAttempted += 1;
                        process.Kill();
                        forceRequested.Add(snapshot);
                    }
                    catch (Exception ex)
                    {
                        AddFailure(report, snapshot.Id, "强制终止失败：" + ex.Message);
                    }
                }
            }

            await WaitForVerifiedProcessesAsync(install, forceTimeout);
            var finalScan = Scan(install);
            report.ForceTerminated = CountSnapshotsNoLongerRunning(forceRequested, finalScan.Verified);
            AddUnverifiedFailures(finalScan, report, "结束时仍存在路径不可验证的候选进程");
            foreach (var remaining in finalScan.Verified)
                AddFailure(report, remaining.Id, "进程仍在运行");
            report.RemainingCount = finalScan.TotalPotentialCount;

            if (report.RemainingCount == 0)
                Report(onProgress, "全部 Codex 进程已关闭。 ");
            else
                Report(onProgress, "仍有 " + report.RemainingCount + " 个候选进程未关闭，已停止自动重启。 ");
            return report;
        }

        internal static bool IsPathOwnedByInstall(CodexInstall install, string executablePath)
        {
            if (install == null || String.IsNullOrWhiteSpace(executablePath)) return false;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(executablePath);
            }
            catch
            {
                return false;
            }

            var fileName = Path.GetFileName(fullPath);
            if (!fileName.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
                return false;

            if (install.IsStorePackage)
                return IsWithinDirectory(fullPath, install.InstallLocation);

            string root;
            try
            {
                root = !String.IsNullOrWhiteSpace(install.InstallLocation)
                    ? Path.GetFullPath(install.InstallLocation)
                    : Path.GetDirectoryName(Path.GetFullPath(install.ExecutablePath));
            }
            catch
            {
                return false;
            }
            if (String.IsNullOrWhiteSpace(root)) return false;

            var parent = Path.GetDirectoryName(fullPath);
            if (String.Equals(parent, root, StringComparison.OrdinalIgnoreCase)) return true;
            return IsWithinDirectory(fullPath, Path.Combine(root, "resources"));
        }

        private static bool TryCreateVerifiedInfo(
            CodexInstall install,
            Process process,
            out CodexProcessInfo info,
            out bool pathUnavailable)
        {
            info = null;
            pathUnavailable = false;
            try
            {
                if (process.HasExited) return false;
                var path = process.MainModule == null ? null : process.MainModule.FileName;
                if (String.IsNullOrWhiteSpace(path))
                {
                    pathUnavailable = true;
                    return false;
                }
                if (!IsPathOwnedByInstall(install, path)) return false;
                info = new CodexProcessInfo
                {
                    Id = process.Id,
                    Name = process.ProcessName,
                    ExecutablePath = Path.GetFullPath(path),
                    StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                    HasMainWindow = process.MainWindowHandle != IntPtr.Zero
                };
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch
            {
                pathUnavailable = true;
                return false;
            }
        }

        private static bool TryOpenRevalidatedProcess(
            CodexInstall install,
            CodexProcessInfo expected,
            out Process process,
            out string error)
        {
            process = null;
            error = null;
            try
            {
                process = Process.GetProcessById(expected.Id);
                CodexProcessInfo current;
                bool pathUnavailable;
                if (!TryCreateVerifiedInfo(install, process, out current, out pathUnavailable))
                {
                    error = pathUnavailable
                        ? "PID " + expected.Id + " 的路径无法重新验证，已拒绝终止"
                        : "PID " + expected.Id + " 已不再属于当前 Codex 安装";
                    process.Dispose();
                    process = null;
                    return false;
                }
                if (current.StartTimeUtcTicks != expected.StartTimeUtcTicks ||
                    !String.Equals(current.ExecutablePath, expected.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    error = "PID " + expected.Id + " 已被其他进程复用，已拒绝终止";
                    process.Dispose();
                    process = null;
                    return false;
                }
                return true;
            }
            catch (ArgumentException)
            {
                if (process != null) process.Dispose();
                process = null;
                return false;
            }
            catch (Exception ex)
            {
                error = "PID " + expected.Id + " 重新验证失败：" + ex.Message;
                if (process != null) process.Dispose();
                process = null;
                return false;
            }
        }

        private static async Task WaitForVerifiedProcessesAsync(CodexInstall install, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (Scan(install).Verified.Count == 0) return;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return;
                var delayMilliseconds = Math.Max(1, Math.Min(250, (int)Math.Ceiling(remaining.TotalMilliseconds)));
                await Task.Delay(delayMilliseconds);
            }
        }

        private static int CountSnapshotsNoLongerRunning(
            IEnumerable<CodexProcessInfo> before,
            IEnumerable<CodexProcessInfo> after)
        {
            var current = new HashSet<string>(after.Select(ProcessIdentity));
            return before.Count(delegate(CodexProcessInfo item) { return !current.Contains(ProcessIdentity(item)); });
        }

        private static string ProcessIdentity(CodexProcessInfo info)
        {
            return info.Id + ":" + info.StartTimeUtcTicks;
        }

        private static bool IsWithinDirectory(string path, string directory)
        {
            if (String.IsNullOrWhiteSpace(path) || String.IsNullOrWhiteSpace(directory)) return false;
            try
            {
                var fullPath = Path.GetFullPath(path);
                var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void AddUnverifiedFailures(
            CodexProcessScan scan,
            CodexShutdownReport report,
            string message)
        {
            foreach (var id in scan.UnverifiedCandidateIds)
                AddFailure(report, id, message + "：PID " + id);
        }

        private static void AddFailure(CodexShutdownReport report, int processId, string error)
        {
            if (!report.FailedProcessIds.Contains(processId)) report.FailedProcessIds.Add(processId);
            if (!String.IsNullOrWhiteSpace(error) && !report.Errors.Contains(error)) report.Errors.Add(error);
        }

        private static string FormatSeconds(TimeSpan timeout)
        {
            return Math.Max(0, (int)Math.Ceiling(timeout.TotalSeconds)).ToString();
        }

        private static void Report(Action<string> callback, string message)
        {
            AppLog.Write("shutdown " + message);
            if (callback == null) return;
            try
            {
                callback(message);
            }
            catch
            {
            }
        }
    }
}
