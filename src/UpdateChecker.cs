using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexZhLauncher
{
    internal sealed class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
        public string Message { get; set; }
    }

    internal static class UpdateChecker
    {
        private const string LatestReleaseApi =
            "https://api.github.com/repos/shibaweidu/codex-desktop-zh/releases/latest";

        private sealed class GitHubRelease
        {
            public string tag_name { get; set; }
            public string html_url { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
        }

        public static async Task<UpdateCheckResult> CheckAsync(string currentVersion)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexZhLauncher/" + currentVersion);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                var response = await client.GetAsync(LatestReleaseApi).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false), currentVersion);
            }
        }

        internal static UpdateCheckResult Parse(string json, string currentVersion)
        {
            var release = new JavaScriptSerializer().Deserialize<GitHubRelease>(json);
            if (release == null || release.draft || release.prerelease)
                throw new InvalidOperationException("GitHub 返回的最新版本无效。");

            var latest = ParseVersion(release.tag_name);
            var current = ParseVersion(currentVersion);
            var releaseUri = ValidateReleaseUrl(release.html_url);
            var available = latest > current;
            return new UpdateCheckResult
            {
                UpdateAvailable = available,
                LatestVersion = latest.ToString(3),
                ReleaseUrl = releaseUri.AbsoluteUri,
                Message = available
                    ? "发现新版本 v" + latest.ToString(3)
                    : "当前已是最新版本 v" + current.ToString(3)
            };
        }

        internal static string SelfTest()
        {
            const string newer =
                "{\"tag_name\":\"v0.6.1\",\"html_url\":\"https://github.com/shibaweidu/codex-desktop-zh/releases/tag/v0.6.1\",\"draft\":false,\"prerelease\":false}";
            const string current =
                "{\"tag_name\":\"v0.6.0\",\"html_url\":\"https://github.com/shibaweidu/codex-desktop-zh/releases/tag/v0.6.0\",\"draft\":false,\"prerelease\":false}";
            var newerResult = Parse(newer, "0.6.0");
            var currentResult = Parse(current, "0.6.0");
            if (!newerResult.UpdateAvailable || currentResult.UpdateAvailable)
                throw new InvalidOperationException("更新版本比较自检失败。");
            try
            {
                Parse(
                    "{\"tag_name\":\"v9.0.0\",\"html_url\":\"https://example.com/releases/tag/v9.0.0\",\"draft\":false,\"prerelease\":false}",
                    "0.6.0");
                throw new InvalidOperationException("外部更新地址未被拒绝。");
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message == "外部更新地址未被拒绝。") throw;
            }
            return "update-check=ok";
        }

        private static Version ParseVersion(string value)
        {
            var normalized = (value ?? String.Empty).Trim().TrimStart('v', 'V');
            var suffix = normalized.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) normalized = normalized.Substring(0, suffix);
            Version version;
            if (!Version.TryParse(normalized, out version))
                throw new InvalidOperationException("无法识别版本号：" + value);
            return new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);
        }

        private static Uri ValidateReleaseUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith(
                    "/shibaweidu/codex-desktop-zh/releases/",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新地址不是本项目的 GitHub Release。");
            }
            return uri;
        }
    }
}
