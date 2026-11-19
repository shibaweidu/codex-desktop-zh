using System;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexZhLauncher
{
    internal sealed class WorkspaceSettings
    {
        public string ToolTheme = "violet";
        public string CodexTheme = "default";
        public int FontSize = 14;
        public bool AutoCheck = true;
        public static string FilePath { get { return Path.Combine(AppLog.DirectoryPath, "preferences.json"); } }

        public static WorkspaceSettings Load(string path = null)
        {
            try { return Normalize(new JavaScriptSerializer().Deserialize<WorkspaceSettings>(File.ReadAllText(path ?? FilePath))); }
            catch (Exception ex) { if(path==null)AppLog.Write("preferences.load " + ex.Message); return new WorkspaceSettings(); }
        }
        public static WorkspaceSettings Normalize(WorkspaceSettings value)
        {
            value = value ?? new WorkspaceSettings();
            if (ThemePreset.Find(value.ToolTheme) == null) value.ToolTheme = "violet";
            if (value.CodexTheme != "default" && ThemePreset.Find(value.CodexTheme) == null) value.CodexTheme = "default";
            value.FontSize = Math.Max(12, Math.Min(18, value.FontSize));
            return value;
        }
        public void Save(string path = null)
        {
            path = path ?? FilePath;
            Normalize(this);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), new System.Text.UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    internal sealed class ThemePreset
    {
        public string Id, Name, Background, Sidebar, Foreground, Accent;
        public static readonly ThemePreset[] All = {
            Create("violet", "雾紫", "#1c1d23", "#27272f", "#f5f4f8", "#c4b5fd"),
            Create("midnight", "午夜蓝", "#121c29", "#192538", "#e1eafd", "#9bbdff"),
            Create("graphite", "石墨", "#202020", "#292929", "#eeeeee", "#c9c9c9"),
            Create("forest", "松绿", "#17211e", "#1e2d28", "#e3eee7", "#a2c9ae"),
            Create("paper", "纸白", "#f0efed", "#e4e3df", "#35323a", "#6b557f"),
            Create("contrast", "高对比", "#080809", "#17171b", "#ffffff", "#dfcfff")
        };
        private static ThemePreset Create(string id, string name, string bg, string side, string text, string accent)
        { return new ThemePreset { Id=id, Name=name, Background=bg, Sidebar=side, Foreground=text, Accent=accent }; }
        public static ThemePreset Find(string id) { foreach (var item in All) if (item.Id == id) return item; return null; }
        public static string BuildScript(string id, int size)
        {
            var theme = Find(id);
            if (id != "default" && theme == null) throw new ArgumentException("未知主题。");
            if (size < 12 || size > 18) throw new ArgumentOutOfRangeException("size");
            using (var stream = typeof(ThemePreset).Assembly.GetManifestResourceStream("CodexZhLauncher.Shared.theme-script.js"))
            using (var reader = new StreamReader(stream ?? throwMissingResource()))
            {
                var payload = new { id=id, fontSize=size, background=theme == null ? "" : theme.Background,
                    sidebar=theme == null ? "" : theme.Sidebar, foreground=theme == null ? "" : theme.Foreground, accent=theme == null ? "" : theme.Accent };
                return reader.ReadToEnd().Replace("__THEME_JSON__", new JavaScriptSerializer().Serialize(payload));
            }
        }
        private static Stream throwMissingResource() { throw new InvalidOperationException("缺少 theme-script.js。"); }
    }
}
