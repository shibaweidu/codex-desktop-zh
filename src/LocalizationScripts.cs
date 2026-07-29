using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexZhLauncher
{
    internal static class LocalizationScripts
    {
        private const string I18nBootstrapResource = "CodexZhLauncher.Shared.i18n-bootstrap.js";
        private const string LocaleResource = "CodexZhLauncher.Shared.locale-script.js";
        private const string MenuResource = "CodexZhLauncher.Shared.menu-script.js";
        private const string TranslationsResource = "CodexZhLauncher.Shared.menu-translations.json";
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static readonly Dictionary<string, Dictionary<string, string>> TranslationGroups =
            LoadTranslationGroups();

        public static string BuildLocaleScript(string locale)
        {
            var template = ReadResource(LocaleResource);
            var script = template.Replace("__LOCALE_JSON__", Serializer.Serialize(locale));
            EnsureResolved(script, "__LOCALE_JSON__");
            return script;
        }

        public static string BuildMenuScript()
        {
            return BuildMenuScriptForPlatform("windows");
        }

        internal static string BuildMenuScriptForPlatform(string platform)
        {
            var normalized = String.Equals(platform, "macos", StringComparison.OrdinalIgnoreCase)
                ? "macos"
                : "windows";
            var translations = MergeTranslations(normalized);
            var script = ReadResource(MenuResource)
                .Replace("__TRANSLATIONS_JSON__", Serializer.Serialize(translations))
                .Replace("__PLATFORM_JSON__", Serializer.Serialize(normalized));
            EnsureResolved(script, "__TRANSLATIONS_JSON__");
            EnsureResolved(script, "__PLATFORM_JSON__");
            return script;
        }

        public static string SelfTest()
        {
            var bootstrap = ReadResource(I18nBootstrapResource);
            var locale = BuildLocaleScript("zh-CN");
            var windowsMenu = BuildMenuScriptForPlatform("windows");
            var macMenu = BuildMenuScriptForPlatform("macos");
            if (bootstrap.IndexOf("72216192", StringComparison.Ordinal) < 0 ||
                bootstrap.IndexOf("enable_i18n", StringComparison.Ordinal) < 0 ||
                bootstrap.IndexOf("locale_source", StringComparison.Ordinal) < 0 ||
                bootstrap.IndexOf("getDynamicConfig", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("i18n 引导脚本缺少必要的动态配置补丁。 ");
            if (locale.IndexOf("localeOverride", StringComparison.Ordinal) < 0 ||
                locale.IndexOf("vscode://codex/set-setting", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("语言脚本缺少官方设置接口。 ");
            if (locale.IndexOf("JSON.stringify({ key: 'localeOverride', value: locale })", StringComparison.Ordinal) < 0 ||
                locale.IndexOf("params: { key: 'localeOverride'", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("语言设置请求格式与当前 Codex 接口不一致。 ");
            if (windowsMenu.IndexOf("Menu.setApplicationMenu", StringComparison.Ordinal) < 0 ||
                windowsMenu.IndexOf("切换侧边栏", StringComparison.Ordinal) < 0 ||
                windowsMenu.IndexOf("开始性能跟踪", StringComparison.Ordinal) < 0 ||
                windowsMenu.IndexOf("强制重新加载", StringComparison.Ordinal) < 0 ||
                windowsMenu.IndexOf("切换开发者工具", StringComparison.Ordinal) < 0 ||
                windowsMenu.IndexOf("最小化", StringComparison.Ordinal) < 0 ||
                windowsMenu.IndexOf("Go to Chat", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Windows 菜单翻译资源不完整。 ");
            if (macMenu.IndexOf("隐藏其他应用", StringComparison.Ordinal) < 0 ||
                macMenu.IndexOf("前置全部窗口", StringComparison.Ordinal) < 0 ||
                macMenu.IndexOf("platform = \"macos\"", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("macOS 菜单翻译资源不完整。 ");
            if (windowsMenu.IndexOf("app.asar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                macMenu.IndexOf("app.asar", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("菜单脚本不应修改 app.asar。 ");
            return "i18n-bootstrap=ok; locale-script=ok; menu-script=ok; translations=" + MergeTranslations("windows").Count +
                "; macos-translations=" + MergeTranslations("macos").Count;
        }

        private static Dictionary<string, Dictionary<string, string>> LoadTranslationGroups()
        {
            var groups = Serializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                ReadResource(TranslationsResource));
            if (groups == null || !groups.ContainsKey("common") ||
                !groups.ContainsKey("windows") || !groups.ContainsKey("macos"))
                throw new InvalidOperationException("共享菜单翻译资源格式无效。 ");
            return groups;
        }

        private static Dictionary<string, string> MergeTranslations(string platform)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in TranslationGroups["common"]) merged[pair.Key] = pair.Value;
            foreach (var pair in TranslationGroups[platform]) merged[pair.Key] = pair.Value;
            return merged;
        }

        private static string ReadResource(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("缺少嵌入资源：" + name);
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                    return reader.ReadToEnd();
            }
        }

        private static void EnsureResolved(string script, string placeholder)
        {
            if (script.IndexOf(placeholder, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("共享脚本占位符未替换：" + placeholder);
        }
    }
}
