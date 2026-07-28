using System.Windows.Media.Imaging;

namespace CodexZhLauncher
{
    internal static class AppIcon
    {
        public static BitmapFrame Load()
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("CodexZhLauncher.AppIcon.ico"))
            {
                if (stream == null) return null;
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                BitmapFrame largest = null;
                foreach (var frame in decoder.Frames)
                {
                    if (largest == null || frame.PixelWidth > largest.PixelWidth) largest = frame;
                }
                if (largest != null) largest.Freeze();
                return largest;
            }
        }
    }
}
