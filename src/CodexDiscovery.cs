using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace CodexZhLauncher
{
    internal static class CodexDiscovery
    {
        private const string PackageName = "OpenAI.Codex";
        private static readonly string StateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexZhLauncher");
        private static readonly string PortablePathFile = Path.Combine(StateDirectory, "portable-path.txt");

        public static async Task<CodexInstall> DetectAsync()
        {
            var custom = LoadPortableInstall();
            if (custom != null) return custom;

            var store = await DetectStorePackageAsync();
            if (store != null) return store;

            return DetectCommonPortableInstall();
        }

        public static CodexInstall UsePortableExecutable(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("请选择 Codex 可执行文件。", "executablePath");

            var fullPath = Path.GetFullPath(executablePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("找不到所选文件。", fullPath);

            var fileName = Path.GetFileName(fullPath);
            if (!fileName.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("请选择 Codex.exe 或 Codex 应用目录中的 ChatGPT.exe。");
            }

            Directory.CreateDirectory(StateDirectory);
            File.WriteAllText(PortablePathFile, fullPath, System.Text.Encoding.UTF8);
            return BuildPortableInstall(fullPath, "手动选择");
        }

        public static void ClearPortableOverride()
        {
            if (File.Exists(PortablePathFile)) File.Delete(PortablePathFile);
        }

        public static int CountRunningCodexProcesses(CodexInstall install)
        {
            return CodexProcessManager.Scan(install).TotalPotentialCount;
        }

        private static async Task<CodexInstall> DetectStorePackageAsync()
        {
            var command =
                "$p = Get-AppxPackage -Name '" + PackageName + "' | Sort-Object Version -Descending | Select-Object -First 1; " +
                "if ($null -ne $p) { [pscustomobject]@{ Version=$p.Version.ToString(); PackageFamilyName=$p.PackageFamilyName; InstallLocation=$p.InstallLocation } | ConvertTo-Json -Compress }";
            var result = await ProcessRunner.RunAsync(
                "powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + ProcessRunner.QuoteArgument(command));

            if (!result.Success || String.IsNullOrWhiteSpace(result.Output)) return null;

            var serializer = new JavaScriptSerializer();
            var values = serializer.Deserialize<Dictionary<string, object>>(result.Output.Trim());
            if (values == null) return null;

            var installLocation = GetString(values, "InstallLocation");
            var familyName = GetString(values, "PackageFamilyName");
            if (String.IsNullOrWhiteSpace(installLocation) || String.IsNullOrWhiteSpace(familyName)) return null;

            var applicationId = ReadApplicationId(Path.Combine(installLocation, "AppxManifest.xml"));
            if (String.IsNullOrWhiteSpace(applicationId)) applicationId = "App";

            var executable = FindPackagedExecutable(installLocation);
            return new CodexInstall
            {
                Kind = "Microsoft Store",
                DisplayName = "Codex Desktop（Microsoft Store）",
                Version = GetString(values, "Version") ?? "-",
                InstallLocation = installLocation,
                ExecutablePath = executable,
                AppUserModelId = familyName + "!" + applicationId
            };
        }

        private static CodexInstall LoadPortableInstall()
        {
            try
            {
                if (!File.Exists(PortablePathFile)) return null;
                var path = File.ReadAllText(PortablePathFile, System.Text.Encoding.UTF8).Trim();
                return File.Exists(path) ? BuildPortableInstall(path, "已保存路径") : null;
            }
            catch
            {
                return null;
            }
        }

        private static CodexInstall DetectCommonPortableInstall()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Codex", "Codex.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Codex", "Codex.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Codex", "Codex.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return BuildPortableInstall(candidate, "自动检测");
            }
            return null;
        }

        private static CodexInstall BuildPortableInstall(string executablePath, string source)
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return new CodexInstall
            {
                Kind = "便携版",
                DisplayName = "Codex Desktop（" + source + "）",
                Version = String.IsNullOrWhiteSpace(info.FileVersion) ? "-" : info.FileVersion,
                InstallLocation = Path.GetDirectoryName(executablePath),
                ExecutablePath = executablePath,
                AppUserModelId = null
            };
        }

        private static string ReadApplicationId(string manifestPath)
        {
            try
            {
                var document = XDocument.Load(manifestPath);
                var application = document.Descendants().FirstOrDefault(
                    delegate(XElement element) { return element.Name.LocalName == "Application"; });
                if (application == null) return null;
                var id = application.Attributes().FirstOrDefault(
                    delegate(XAttribute attribute) { return attribute.Name.LocalName == "Id"; });
                return id == null ? null : id.Value;
            }
            catch
            {
                return null;
            }
        }

        private static string FindPackagedExecutable(string installLocation)
        {
            var candidates = new[]
            {
                Path.Combine(installLocation, "app", "ChatGPT.exe"),
                Path.Combine(installLocation, "app", "Codex.exe"),
                Path.Combine(installLocation, "Codex.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private static string GetString(Dictionary<string, object> values, string key)
        {
            object value;
            return values.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }
    }
}
