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
            var target = await DevToolsClient.WaitForTargetAsync(port, "page", TimeSpan.FromSeconds(25));
            AppLog.Write("renderer.target type=" + target.Type + " title=" + target.Title);
            return await DevToolsClient.EvaluateAsync(
                target.WebSocketDebuggerUrl,
                LocalizationScripts.BuildLocaleScript(locale),
                true);
        }

        private static async Task<string> VerifyLocaleAsync(int port, string locale)
        {
            await Task.Delay(900);
            var target = await DevToolsClient.WaitForTargetAsync(port, "page", TimeSpan.FromSeconds(12));
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
                return values != null && values.TryGetValue("status", out status) &&
                    String.Equals(Convert.ToString(status), expected, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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
