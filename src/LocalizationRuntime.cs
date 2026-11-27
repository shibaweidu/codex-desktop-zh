using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexZhLauncher
{
    internal static class LocalizationRuntime
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static async Task<LaunchReport> LocalizeRunningAsync(CodexInstall install, Action<string> onProgress = null)
        {
            var candidates = CodexProcessManager.Scan(install).Verified.FindAll(p => p.HasMainWindow);
            foreach (var process in candidates)
            {
                var query = "$ErrorActionPreference='Stop'; $p=Get-CimInstance Win32_Process -Filter 'ProcessId=" + process.Id +
                    "'; if($p){ [pscustomobject]@{commandLine=$p.CommandLine} | ConvertTo-Json -Compress }";
                var result = await ProcessRunner.RunAsync("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(query)));
                var info = ParseObject(result.Output);
                object line;
                if (!result.Success || info == null || !info.TryGetValue("commandLine", out line)) continue;
                var commandLine = Convert.ToString(line);
                if (commandLine.IndexOf("--remote-debugging-address=127.0.0.1", StringComparison.Ordinal) < 0) continue;
                var match = System.Text.RegularExpressions.Regex.Match(commandLine, @"(?:^|\s)--remote-debugging-port=(\d+)(?=\s|$)");
                int port;
                if (!match.Success || !Int32.TryParse(match.Groups[1].Value, out port) || port < 1 || port > 65535) continue;
                var listenerQuery = "$ErrorActionPreference='Stop'; $x=Get-NetTCPConnection -State Listen -LocalPort " + port +
                    " | Where-Object {$_.LocalAddress -eq '127.0.0.1' -and $_.OwningProcess -eq " + process.Id +
                    "}; if($x){Write-Output 'owned'}";
                var listener = await ProcessRunner.RunAsync("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(listenerQuery)));
                if (!listener.Success || listener.Output.Trim() != "owned") continue;
                var stillCurrent = CodexProcessManager.Scan(install).Verified.Exists(p =>
                    p.Id == process.Id && p.StartTimeUtcTicks == process.StartTimeUtcTicks &&
                    String.Equals(p.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase));
                if (!stillCurrent) continue;
                // Live repair uses only the listener whose owner was verified above.
                // Do not attach to an independently configured inspector endpoint.
                var report = new LaunchReport { Started = true, ProcessId = (uint)process.Id, RendererPort = port,
                    InspectorPort = 0, SessionInstall = install, SessionProcess = process };
                ReportProgress(onProgress, "已验证当前 Codex 进程与回环接口，开始修复并验证；不关闭或重载会话。");
                return await ApplyAndVerifyAsync(report, "zh-CN", onProgress, false);
            }
            throw new InvalidOperationException("当前 Codex 没有可验证的本地汉化接口。请保存工作后，通过“关闭并汉化重启”启动一次。");
        }

        public static async Task<LaunchReport> LaunchAsync(
            CodexInstall install,
            string locale,
            Action<string> onProgress = null)
        {
            if (install == null || !install.IsValid)
                throw new InvalidOperationException("未检测到可用的 Codex Desktop。 ");

            var running = CodexDiscovery.CountRunningCodexProcesses(install);
            if (running > 0)
                throw new InvalidOperationException("Codex Desktop 仍在运行（检测到 " + running + " 个相关进程）。请从托盘完全退出后重试。");

            var report = new LaunchReport();
            report.RendererPort = AppActivation.ReserveLoopbackPort();
            do
            {
                report.InspectorPort = AppActivation.ReserveLoopbackPort();
            }
            while (report.InspectorPort == report.RendererPort);

            var arguments = AppActivation.BuildArguments(report.RendererPort, report.InspectorPort, locale);
            AppLog.Write("launch.begin kind=" + install.Kind + " locale=" + locale +
                " renderer_port=" + report.RendererPort + " inspector_port=" + report.InspectorPort);
            ReportProgress(onProgress, "正在启动 Codex 进程。");

            report.ProcessId = AppActivation.Launch(install, arguments);
            report.Started = true;
            report.SessionInstall = install;
            report.SessionProcess = CodexProcessManager.Scan(install).Verified.Find(p => p.Id == (int)report.ProcessId);
            AppLog.Write("launch.started pid=" + report.ProcessId);
            ReportProgress(onProgress, "Codex 已启动，正在连接本地汉化接口。");

            return await ApplyAndVerifyAsync(report, locale, onProgress);
        }

        private static async Task<LaunchReport> ApplyAndVerifyAsync(LaunchReport report, string locale, Action<string> onProgress, bool allowReload = true)
        {
            var localeTask = ApplyLocaleAsync(report.RendererPort, locale, allowReload);
            Task<string> menuTask = null;
            if (locale.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                menuTask = ApplyMenuAsync(report.RendererPort, report.InspectorPort);

            try
            {
                report.LocaleDetail = await localeTask;
            }
            catch (Exception ex)
            {
                report.LocaleDetail = "setting-error=" + ex.Message;
                AppLog.Write("locale.failed " + ex);
            }

            try
            {
                var verification = await VerifyLocaleAsync(report.RendererPort, locale);
                report.LocaleApplied = HasStatus(verification, "ok");
                report.LocaleDetail = JoinDetails(report.LocaleDetail, "verification=" + verification);
            }
            catch (Exception ex)
            {
                report.LocaleDetail = JoinDetails(report.LocaleDetail, "verification-error=" + ex.Message);
                AppLog.Write("locale.verify.failed " + ex);
            }
            AppLog.Write("locale.detail " + report.LocaleDetail);
            ReportProgress(onProgress, report.LocaleApplied
                ? "已确认 Codex 中文界面生效。"
                : "界面语言尚未确认，详细信息已写入日志。");

            if (menuTask != null)
            {
                try
                {
                    report.MenuDetail = await menuTask;
                    report.MenuApplied = HasStatus(report.MenuDetail, "ok");
                }
                catch (Exception ex)
                {
                    report.MenuDetail = "menu-error=" + ex.Message;
                    AppLog.Write("menu.failed " + ex);
                }
            }
            else
            {
                report.MenuApplied = true;
                report.MenuDetail = "英文模式未安装中文菜单补丁。";
            }
            AppLog.Write("menu.detail " + report.MenuDetail);
            ReportProgress(onProgress, report.MenuApplied
                ? "原生菜单已覆盖当前全部标签。"
                : "菜单未通过完整验证，请查看日志中的具体原因。");

            if (report.Complete)
                report.Message = locale == "zh-CN"
                    ? "汉化完成：中文界面和当前全部原生菜单已生效。"
                    : "英文版已启动，语言设置已恢复。";
            else if (report.LocaleApplied)
                report.Message = "中文界面已验证，菜单尚未通过完整验证；请查看菜单详情。";
            else if (report.MenuApplied && locale == "zh-CN")
                report.Message = "原生菜单已汉化，但中文界面状态未能确认；详情请查看运行日志。";
            else
                report.Message = "中文界面和菜单尚未通过完整验证；请查看运行日志。";

            AppLog.Write("launch.complete message=" + report.Message +
                " locale=" + report.LocaleApplied + " menu=" + report.MenuApplied);
            return report;
        }

        private static async Task<string> ApplyLocaleAsync(int port, string locale, bool allowReload)
        {
            var target = await WaitForRendererTargetAsync(port, TimeSpan.FromSeconds(25));
            AppLog.Write("renderer.target type=" + target.Type + " title=" + target.Title);
            var modern = await DevToolsClient.EvaluateAsync(target.WebSocketDebuggerUrl,
                LocalizationScripts.BuildAppServicesProbe(), true);
            if (HasStatus(modern, "ok") || !allowReload)
                return await DevToolsClient.EvaluateAsync(target.WebSocketDebuggerUrl,
                    LocalizationScripts.BuildLocaleScript(locale, allowReload), true);
            if (locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                var bootstrap = LocalizationScripts.BuildI18nBootstrap();
                var identifier = await DevToolsClient.InstallNewDocumentScriptAsync(
                    target.WebSocketDebuggerUrl,
                    bootstrap);
                var bootstrapResult = await DevToolsClient.EvaluateAsync(
                    target.WebSocketDebuggerUrl,
                    bootstrap + ";\n" + I18nBootstrapWaitScript,
                    true);
                // Modern Codex can localize through settings even without the old
                // Statsig config. Actual UI verification decides the final status.

                var setting = await DevToolsClient.EvaluateAsync(
                    target.WebSocketDebuggerUrl,
                    LocalizationScripts.BuildLocaleScript(locale),
                    true);
                return BuildChineseLocaleDetail(bootstrapResult, identifier, setting);
            }

            return await DevToolsClient.EvaluateAsync(
                target.WebSocketDebuggerUrl,
                LocalizationScripts.BuildLocaleScript(locale),
                true);
        }

        private static string BuildChineseLocaleDetail(string bootstrap, string identifier, string setting)
        {
            var settingObject = ParseObject(setting);
            object status;
            var result = new Dictionary<string, object>
            {
                { "status", settingObject != null && settingObject.TryGetValue("status", out status) ? status : "partial" },
                { "bootstrap", ParseObject(bootstrap) ?? (object)bootstrap },
                { "newDocument", identifier ?? "unknown" },
                { "setting", settingObject ?? (object)setting }
            };
            return Serializer.Serialize(result);
        }

        private const string I18nBootstrapWaitScript = @"(async function () {
  var started = Date.now();
  while (Date.now() - started < 10000) {
    var state = window.__codexZhI18nState || {};
    if ((state.patchedClients || 0) > 0 && (state.patchedConfigs || 0) > 0) break;
    await new Promise(function (resolve) { window.setTimeout(resolve, 100); });
  }
  var finalState = window.__codexZhI18nState || {};
  return JSON.stringify({
    status: (finalState.patchedClients || 0) > 0 && (finalState.patchedConfigs || 0) > 0 ? 'ok' : 'partial',
    configId: '72216192',
    enable_i18n: true,
    locale_source: 'SYSTEM',
    patchedClients: finalState.patchedClients || 0,
    patchedConfigs: finalState.patchedConfigs || 0
  });
})()";

        private static async Task<string> VerifyLocaleAsync(int port, string locale)
        {
            var target = await WaitForRendererTargetAsync(port, TimeSpan.FromSeconds(12));
            return await DevToolsClient.EvaluateAsync(target.WebSocketDebuggerUrl,
                LocalizationScripts.BuildLocaleVerification(locale), true);
        }

        public static async Task<string> ApplyThemeAsync(LaunchReport report, string theme, int fontSize)
        {
            if (report == null || !report.Started || report.RendererPort <= 0)
                throw new InvalidOperationException("请先通过本工具启动 Codex，再应用运行时配色。");
            if (report.SessionProcess == null || report.SessionInstall == null || !CodexProcessManager.Scan(report.SessionInstall).Verified.Exists(p => p.Id == report.SessionProcess.Id && p.StartTimeUtcTicks == report.SessionProcess.StartTimeUtcTicks))
                throw new InvalidOperationException("原启动会话已结束或无法验证，请通过本工具重新启动 Codex。");
            var target = await WaitForRendererTargetAsync(report.RendererPort, TimeSpan.FromSeconds(8));
            return await DevToolsClient.EvaluateAsync(target.WebSocketDebuggerUrl, ThemePreset.BuildScript(theme, fontSize), false);
        }

        private static async Task<DevToolsTarget> WaitForRendererTargetAsync(int port, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            DevToolsTarget best = null;
            var bestScore = Int32.MinValue;
            Exception lastError = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var targets = await DevToolsClient.ListTargetsAsync(port);
                    foreach (var target in targets)
                    {
                        if (String.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) ||
                            (target.Type != "page" && target.Type != "iframe" && target.Type != "webview"))
                            continue;
                        string probe;
                        try
                        {
                            probe = await DevToolsClient.EvaluateAsync(
                                target.WebSocketDebuggerUrl,
                                RendererProbeScript,
                                false);
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            continue;
                        }
                        var values = ParseObject(probe);
                        if (values == null || !ReadBoolean(values, "codexZhProbe")) continue;
                        var hasBridge = ReadBoolean(values, "hasBridge");
                        var hasRoot = ReadBoolean(values, "hasAppRoot");
                        var textLength = ReadInt(values, "textLength");
                        var score = (hasBridge ? 10000 : 0) + (hasRoot ? 1000 : 0) + textLength;
                        if (score > bestScore)
                        {
                            best = target;
                            bestScore = score;
                        }
                        if (hasBridge && (hasRoot || textLength >= 40))
                        {
                            AppLog.Write("renderer.ready type=" + target.Type + " url=" + target.Url +
                                " bridge=" + hasBridge + " root=" + hasRoot + " text_length=" + textLength);
                            return target;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                await Task.Delay(300);
            }
            if (best != null && bestScore >= 10000)
            {
                AppLog.Write("renderer.fallback type=" + best.Type + " url=" + best.Url);
                return best;
            }
            var suffix = lastError == null ? String.Empty : " 最后错误：" + lastError.Message;
            throw new TimeoutException("未找到 Codex 实际渲染页面。" + suffix);
        }

        private const string RendererProbeScript = @"(function () {
  var text = document.body ? document.body.innerText || '' : '';
  return JSON.stringify({
    codexZhProbe: true,
    hasBridge: !!(window.electronBridge && typeof window.electronBridge.sendMessageFromView === 'function'),
    hasAppRoot: !!document.querySelector('#root'),
    textLength: text.length
  });
})()";

        private static Dictionary<string, object> ParseObject(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try { return Serializer.Deserialize<Dictionary<string, object>>(json); }
            catch { return null; }
        }

        private static bool ReadBoolean(Dictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static int ReadInt(Dictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) ? Convert.ToInt32(value) : 0;
        }

        private static async Task<string> ApplyMenuAsync(int rendererPort, int inspectorPort)
        {
            string modern;
            try
            {
                var renderer = await WaitForRendererTargetAsync(rendererPort, TimeSpan.FromSeconds(12));
                modern = await DevToolsClient.EvaluateAsync(renderer.WebSocketDebuggerUrl,
                    LocalizationScripts.BuildMenuScript(), true);
                if (!HasStatus(modern, "unavailable")) return modern;
            }
            catch (Exception ex) { modern = "application-menu-service-error=" + ex.Message; }
            DevToolsTarget target;
            if (inspectorPort <= 0)
                return Serializer.Serialize(new Dictionary<string, object> { {"status","unavailable"}, {"reason","menu-verification-unavailable"}, {"renderer",modern} });
            try { target = await DevToolsClient.WaitForTargetAsync(inspectorPort, "node", TimeSpan.FromSeconds(5)); }
            catch (Exception ex)
            {
                return Serializer.Serialize(new Dictionary<string, object> {
                    {"status", "unavailable"}, {"reason", "menu-verification-unavailable"},
                    {"renderer", modern}, {"inspector", ex.Message}
                });
            }
            AppLog.Write("main.target type=" + target.Type + " title=" + target.Title);
            return await DevToolsClient.EvaluateAsync(
                target.WebSocketDebuggerUrl,
                LocalizationScripts.BuildMenuScript(),
                true);
        }

        internal static bool HasStatus(string json, string expected)
        {
            if (String.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var values = Serializer.Deserialize<Dictionary<string, object>>(json);
                object status;
                if (values != null && values.TryGetValue("status", out status) &&
                    String.Equals(Convert.ToString(status), expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }
            return false;
        }

        private static string JoinDetails(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first)) return second;
            if (String.IsNullOrWhiteSpace(second)) return first;
            return first + "; " + second;
        }

        private static void ReportProgress(Action<string> callback, string message)
        {
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
