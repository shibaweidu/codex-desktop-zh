using System.Diagnostics;

namespace CodexZhLauncher
{
    internal static class AppInfo
    {
        public const string Version = "0.6.0";
        public const string RepositoryUrl = "https://github.com/shibaweidu/codex-desktop-zh";
        public const string FeedbackUrl = RepositoryUrl + "/issues/new/choose";
        public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";

        public static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
}
