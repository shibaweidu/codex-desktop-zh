using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexZhLauncher
{
    internal static class LocalizationRuntime
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

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
            AppLog.Write("launch.started pid=" + report.ProcessId);
            ReportProgress(onProgress, "Codex 已启动，正在连接本地汉化接口。");

            var localeTask = ApplyLocaleAsync(report.RendererPort, locale);
            Task<string> menuTask = null;
            if (locale.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                menuTask = ApplyMenuAsync(report.InspectorPort);

            try
            {
                report.LocaleDetail = await localeTask;
                report.LocaleApplied = HasStatus(report.LocaleDetail, "ok");
            }
            catch (Exception ex)
            {
                report.LocaleDetail = "setting-error=" + ex.Message;
                AppLog.Write("locale.failed " + ex);
            }

            try
            {
                var verification = await VerifyLocaleAsync(report.RendererPort, locale);
                if (HasStatus(verification, "ok")) report.LocaleApplied = true;
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
                : "仍有原生菜单未翻译，遗漏项已显示在日志中。");

            if (report.Complete)
                report.Message = locale == "zh-CN"
                    ? "汉化完成：中文界面和当前全部原生菜单已生效。"
                    : "英文版已启动，语言设置已恢复。";
            else if (report.LocaleApplied)
                report.Message = "中文界面已生效，但原生菜单仍有未翻译项；详情请查看运行日志。";
            else if (report.MenuApplied && locale == "zh-CN")
                report.Message = "原生菜单已汉化，但中文界面状态未能确认；详情请查看运行日志。";
            else
                report.Message = "中文界面设置未生效，原生菜单仍有未翻译项；详情请查看运行日志。";

            AppLog.Write("launch.complete message=" + report.Message +
                " locale=" + report.LocaleApplied + " menu=" + report.MenuApplied);
            return report;
        }

        private static async Task<string> ApplyLocaleAsync(int port, string locale)
        {
            var target = await WaitForRendererTargetAsync(port, TimeSpan.FromSeconds(25));
            AppLog.Write("renderer.target type=" + target.Type + " title=" + target.Title);
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
                if (!HasStatus(bootstrapResult, "ok"))
                    throw new InvalidOperationException("Codex 官方中文资源引导未完成：" + bootstrapResult);

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
            await Task.Delay(900);
            var target = await WaitForRendererTargetAsync(port, TimeSpan.FromSeconds(12));
            var encodedLocale = Serializer.Serialize(locale);
            var script = @"(function () {
  var requested = " + encodedLocale + @";
  var text = document.body ? document.body.innerText || '' : '';
  var zhMarkers = ['新建任务', '拉取请求', '已安排', '插件'];
  var enMarkers = ['New task', 'Pull requests', 'Scheduled', 'Plugins'];
  var count = function (markers) {
    return markers.reduce(function (total, marker) {
      return total + (text.indexOf(marker) >= 0 ? 1 : 0);
    }, 0);
  };
  var zhMatches = count(zhMarkers);
  var enMatches = count(enMarkers);
  var expectsChinese = requested.toLowerCase().indexOf('zh') === 0;
  var ok = expectsChinese ? zhMatches >= 2 : enMatches >= 2;
  return JSON.stringify({
    status: ok ? 'ok' : 'partial',
    requested: requested,
    navigatorLanguage: navigator.language,
    documentLanguage: document.documentElement.lang || '',
    zhMarkers: zhMatches,
    enMarkers: enMatches
  });
})()";
            return await DevToolsClient.EvaluateAsync(target.WebSocketDebuggerUrl, script, false);
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

        private static async Task<string> ApplyMenuAsync(int port)
        {
            var target = await DevToolsClient.WaitForTargetAsync(port, "node", TimeSpan.FromSeconds(20));
            AppLog.Write("main.target type=" + target.Type + " title=" + target.Title);
            return await DevToolsClient.EvaluateAsync(
                target.WebSocketDebuggerUrl,
                LocalizationScripts.BuildMenuScript(),
                false);
        }

        private static bool HasStatus(string json, string expected)
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
            return json.IndexOf("\"status\":\"" + expected + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
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
