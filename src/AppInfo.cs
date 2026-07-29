using System.Diagnostics;

namespace CodexZhLauncher
{
    internal static class AppInfo
    {
        public const string Version = "0.7.3";
        public const string RepositoryUrl = "https://github.com/shibaweidu/codex-desktop-zh";
        public const string FeedbackUrl = RepositoryUrl + "/issues/new/choose";
        public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";
        public const string SponsorUrl = "https://www.appkaola.com";

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
