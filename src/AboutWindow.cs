using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexZhLauncher
{
    internal sealed class AboutWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        private readonly TextBlock updateStatus;
        private readonly Button checkButton;
        private readonly Button downloadButton;
        private UpdateCheckResult latestResult;

        private static readonly Brush CanvasColor = Brush("#111315");
        private static readonly Brush SurfaceColor = Brush("#202328");
        private static readonly Brush BorderColor = Brush("#30343A");
        private static readonly Brush TextPrimary = Brush("#F3F5F7");
        private static readonly Brush TextSecondary = Brush("#929AA3");
        private static readonly Brush Accent = Brush("#C4B5FD");

        public AboutWindow(Window owner)
        {
            Owner = owner;
            Title = "关于 Codex 汉化增强工具";
            Width = 500;
            Height = 390;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = CanvasColor;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            Icon = AppIcon.Load();
            SourceInitialized += delegate { EnableDarkTitleBar(); };

            var root = new Grid { Margin = new Thickness(28, 25, 28, 24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Image
            {
                Source = AppIcon.Load(),
                Width = 54,
                Height = 54,
                Margin = new Thickness(0, 0, 16, 0)
            });
            var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            heading.Children.Add(new TextBlock
            {
                Text = "Codex 汉化增强工具",
                Foreground = TextPrimary,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold
            });
            heading.Children.Add(new TextBlock
            {
                Text = "版本 " + AppInfo.Version,
                Foreground = TextSecondary,
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0)
            });
            header.Children.Add(heading);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var description = new TextBlock
            {
                Text = "为 Windows 和 macOS 提供 Codex Desktop 中文界面、原生菜单翻译和安全重启支持。",
                Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Margin = new Thickness(0, 22, 0, 20)
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            var updateGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 20),
                Background = SurfaceColor
            };
            updateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            updateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            updateStatus = new TextBlock
            {
                Text = "尚未检查更新",
                Foreground = TextPrimary,
                Margin = new Thickness(14, 13, 14, 13),
                VerticalAlignment = VerticalAlignment.Center
            };
            updateGrid.Children.Add(updateStatus);
            checkButton = BuildButton("检查更新", false);
            checkButton.Margin = new Thickness(0, 8, 8, 8);
            checkButton.Click += async delegate { await CheckNowAsync(); };
            Grid.SetColumn(checkButton, 1);
            updateGrid.Children.Add(checkButton);
            Grid.SetRow(updateGrid, 2);
            root.Children.Add(updateGrid);

            downloadButton = BuildButton("下载新版本", true);
            downloadButton.Visibility = Visibility.Collapsed;
            downloadButton.HorizontalAlignment = HorizontalAlignment.Left;
            downloadButton.Click += delegate
            {
                if (latestResult != null) AppInfo.OpenUrl(latestResult.ReleaseUrl);
            };
            Grid.SetRow(downloadButton, 3);
            root.Children.Add(downloadButton);

            var links = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var feedbackButton = BuildButton("反馈支持", false);
            feedbackButton.Margin = new Thickness(0, 0, 10, 0);
            feedbackButton.Click += delegate { AppInfo.OpenUrl(AppInfo.FeedbackUrl); };
            links.Children.Add(feedbackButton);
            var repositoryButton = BuildButton("GitHub 仓库", false);
            repositoryButton.Click += delegate { AppInfo.OpenUrl(AppInfo.RepositoryUrl); };
            links.Children.Add(repositoryButton);
            Grid.SetRow(links, 4);
            root.Children.Add(links);

            Content = root;
        }

        private void EnableDarkTitleBar()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }

        private async System.Threading.Tasks.Task CheckNowAsync()
        {
            checkButton.IsEnabled = false;
            checkButton.Content = "正在检查";
            updateStatus.Text = "正在连接 GitHub Releases...";
            downloadButton.Visibility = Visibility.Collapsed;
            try
            {
                latestResult = await UpdateChecker.CheckAsync(AppInfo.Version);
                updateStatus.Text = latestResult.Message;
                updateStatus.Foreground = latestResult.UpdateAvailable ? Accent : TextPrimary;
                downloadButton.Visibility = latestResult.UpdateAvailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                latestResult = null;
                updateStatus.Text = "检查失败：" + ex.Message;
                updateStatus.Foreground = TextSecondary;
                AppLog.Write("update.check.failed " + ex.Message);
            }
            finally
            {
                checkButton.Content = "重新检查";
                checkButton.IsEnabled = true;
            }
        }

        private static Button BuildButton(string text, bool accent)
        {
            var background = accent ? Accent : SurfaceColor;
            var foreground = accent ? Brush("#17131E") : TextPrimary;
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, BorderColor);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;

            var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Button.OpacityProperty, 0.55));
            template.Triggers.Add(disabled);

            return new Button
            {
                Content = text,
                MinWidth = 94,
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Background = background,
                Foreground = foreground,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = template
            };
        }

        private static SolidColorBrush Brush(string value)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
    }
}
