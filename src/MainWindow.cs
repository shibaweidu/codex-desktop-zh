using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexZhLauncher
{
    internal sealed class MainWindow : Window
    {
        private readonly SolidColorBrush Accent = Brush("#31B77A");
        private readonly SolidColorBrush AccentHover = Brush("#3BC98A");
        private readonly SolidColorBrush AccentPressed = Brush("#249A64");
        private readonly SolidColorBrush Restart = Brush("#C4B5FD");
        private readonly SolidColorBrush RestartHover = Brush("#D8CCFF");
        private readonly SolidColorBrush RestartPressed = Brush("#A78BFA");
        private readonly SolidColorBrush TextPrimary = Brush("#F3F5F7");
        private readonly SolidColorBrush TextSecondary = Brush("#929AA3");
        private readonly SolidColorBrush BorderColor = Brush("#2B3035");
        private readonly SolidColorBrush CanvasColor = Brush("#111315");
        private readonly SolidColorBrush LogColor = Brush("#0B0D0F");
        private readonly SolidColorBrush Warning = Brush("#C4B5FD");
        private readonly SolidColorBrush Danger = Brush("#F26D6D");

        private readonly DispatcherTimer stateTimer;
        private Image logoImage;
        private Ellipse statusDot;
        private TextBlock statusText;
        private TextBlock detailText;
        private TextBlock primaryLabel;
        private Button primaryButton;
        private ProgressBar progress;
        private RichTextBox logBox;
        private CodexInstall currentInstall;
        private LaunchReport lastLaunchReport;
        private bool busy;
        private bool polling;
        private bool restartButtonMode;
        private int missingPollCount;
        private int lastRunningCount = -1;
        private string shutdownFailureDetail;
        private bool updatePromptShown;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        public MainWindow()
        {
            Title = "Codex 汉化增强工具";
            Width = 820;
            Height = 620;
            MinWidth = 680;
            MinHeight = 500;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = CanvasColor;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Content = BuildLayout();
            ApplyToolIcon();

            stateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            stateTimer.Tick += async delegate { await PollStateAsync(); };
            SourceInitialized += delegate { EnableDarkTitleBar(); };
            Loaded += async delegate
            {
                AppendLog("工具已启动，正在检测 Codex Desktop。", TextSecondary);
                await DetectInstallAsync();
                stateTimer.Start();
                await CheckForUpdatesOnStartupAsync();
            };
            Closed += delegate { stateTimer.Stop(); };
        }

        private UIElement BuildLayout()
        {
            var root = new Grid
            {
                Margin = new Thickness(30, 24, 30, 28),
                Background = CanvasColor
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            AddAt(root, BuildHeader(), 0);
            AddAt(root, BuildStatus(), 1);
            AddAt(root, BuildAction(), 2);

            progress = new ProgressBar
            {
                Height = 2,
                Margin = new Thickness(0, 0, 0, 19),
                BorderThickness = new Thickness(0),
                Background = BorderColor,
                Foreground = Accent,
                IsIndeterminate = true,
                Opacity = 0
            };
            AddAt(root, progress, 3);
            AddAt(root, BuildLogHeader(), 4);
            AddAt(root, BuildLogPanel(), 5);
            return root;
        }

        private UIElement BuildHeader()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            logoImage = new Image
            {
                Width = 40,
                Height = 40,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 13, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(logoImage, 0);
            grid.Children.Add(logoImage);

            var title = new TextBlock
            {
                Text = "Codex 汉化增强工具",
                Foreground = TextPrimary,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            var tools = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var sponsorText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 0, 15, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var sponsorLink = new Hyperlink(new Run("Kao La API 赞助支持"))
            {
                Foreground = TextSecondary,
                TextDecorations = null,
                Cursor = Cursors.Hand,
                ToolTip = AppInfo.SponsorUrl
            };
            sponsorLink.Click += delegate { AppInfo.OpenUrl(AppInfo.SponsorUrl); };
            sponsorLink.MouseEnter += delegate { sponsorLink.Foreground = Restart; };
            sponsorLink.MouseLeave += delegate { sponsorLink.Foreground = TextSecondary; };
            sponsorText.Inlines.Add(sponsorLink);
            tools.Children.Add(sponsorText);
            tools.Children.Add(new TextBlock
            {
                Text = "v" + AppInfo.Version,
                Foreground = TextSecondary,
                FontSize = 12,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var aboutButton = BuildIconButton("\uE946", "关于与更新");
            aboutButton.Click += delegate
            {
                new AboutWindow(this).ShowDialog();
            };
            tools.Children.Add(aboutButton);
            Grid.SetColumn(tools, 2);
            grid.Children.Add(tools);
            return grid;
        }

        private UIElement BuildStatus()
        {
            var border = new Border
            {
                BorderBrush = BorderColor,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0, 17, 0, 18),
                Margin = new Thickness(0, 0, 0, 20)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            statusDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = TextSecondary,
                Margin = new Thickness(1, 6, 13, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(statusDot, 0);
            grid.Children.Add(statusDot);

            var stack = new StackPanel();
            statusText = new TextBlock
            {
                Text = "正在检测 Codex",
                Foreground = TextPrimary,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            detailText = new TextBlock
            {
                Text = "",
                Foreground = TextSecondary,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(statusText);
            stack.Children.Add(detailText);
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);
            border.Child = grid;
            return border;
        }

        private UIElement BuildAction()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            primaryButton = BuildPrimaryButton();
            Grid.SetColumn(primaryButton, 0);
            grid.Children.Add(primaryButton);
            return grid;
        }

        private UIElement BuildLogHeader()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock
            {
                Text = "运行日志",
                Foreground = TextPrimary,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var openLogButton = BuildIconButton("\uE838", "打开日志目录");
            openLogButton.Click += delegate
            {
                Directory.CreateDirectory(AppLog.DirectoryPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppLog.DirectoryPath,
                    UseShellExecute = true
                });
            };
            Grid.SetColumn(openLogButton, 1);
            grid.Children.Add(openLogButton);
            return grid;
        }

        private UIElement BuildLogPanel()
        {
            var border = new Border
            {
                Background = LogColor,
                BorderBrush = BorderColor,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };
            logBox = new RichTextBox
            {
                IsReadOnly = true,
                IsDocumentEnabled = false,
                Background = Brushes.Transparent,
                Foreground = TextPrimary,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(13, 11, 13, 11),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
            logBox.Document.Blocks.Clear();
            border.Child = logBox;
            return border;
        }

        private Button BuildPrimaryButton()
        {
            var icon = new TextBlock
            {
                Text = "\uE768",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 15,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            primaryLabel = new TextBlock
            {
                Text = "汉化并启动",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var foregroundBinding = new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
            };
            icon.SetBinding(TextBlock.ForegroundProperty, foregroundBinding);
            primaryLabel.SetBinding(TextBlock.ForegroundProperty, foregroundBinding);

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(icon);
            content.Children.Add(primaryLabel);

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.ForegroundProperty, Brush("#191421")));
            var button = new Button
            {
                Content = content,
                Width = 218,
                Height = 44,
                Background = Accent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Style = style,
                Template = CreatePrimaryButtonTemplate()
            };
            button.Click += async delegate
            {
                if (currentInstall == null)
                    await SelectPortableAsync();
                else if (CodexProcessManager.Scan(currentInstall).TotalPotentialCount > 0)
                    await ConfirmCloseAndRestartAsync();
                else
                    await LaunchChineseAsync();
            };
            return button;
        }

        private Button BuildIconButton(string glyph, string tooltip)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 15,
                    Foreground = TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Width = 30,
                Height = 30,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Template = CreateIconButtonTemplate()
            };
            return button;
        }

        private ControlTemplate CreatePrimaryButtonTemplate()
        {
            return CreatePrimaryButtonTemplate(AccentHover, AccentPressed);
        }

        private ControlTemplate CreatePrimaryButtonTemplate(Brush hoverColor, Brush pressedColor)
        {
            var template = CreateButtonTemplate(5);
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, hoverColor, "ButtonBorder"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, pressedColor, "ButtonBorder"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#332E43"), "ButtonBorder"));
            disabled.Setters.Add(new Setter(Button.ForegroundProperty, Brush("#C8C1DA")));
            template.Triggers.Add(disabled);
            return template;
        }

        private ControlTemplate CreateIconButtonTemplate()
        {
            var template = CreateButtonTemplate(4);
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, BorderColor, "ButtonBorder"));
            template.Triggers.Add(hover);
            return template;
        }

        private static ControlTemplate CreateButtonTemplate(double cornerRadius)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private async Task DetectInstallAsync()
        {
            if (busy) return;
            var failed = false;
            SetBusy(true, "正在检测 Codex", "读取本机安装状态");
            try
            {
                currentInstall = await CodexDiscovery.DetectAsync();
                missingPollCount = 0;
                if (currentInstall != null)
                {
                    AppendLog("已检测到 " + currentInstall.DisplayName + "，包版本 " + currentInstall.Version + "。", Accent);
                }
                else
                {
                    AppendLog("未检测到 Codex Desktop，可手动选择可执行文件。", Warning);
                }
            }
            catch (Exception ex)
            {
                failed = true;
                currentInstall = null;
                SetStatus("检测失败", ex.Message, Danger);
                AppendLog("检测失败：" + ex.Message, Danger);
                AppLog.Write("detect.failed " + ex);
            }
            finally
            {
                SetBusy(false, null, null);
                if (failed)
                {
                    primaryLabel.Text = "选择 Codex";
                    primaryButton.IsEnabled = true;
                }
                else
                {
                    UpdatePresentation();
                }
            }
        }

        private async Task PollStateAsync()
        {
            if (busy || polling) return;
            polling = true;
            try
            {
                if (currentInstall == null)
                {
                    missingPollCount++;
                    if (missingPollCount >= 5)
                        await DetectInstallAsync();
                }
                else
                {
                    UpdatePresentation();
                }
            }
            finally
            {
                polling = false;
            }
        }

        private void UpdatePresentation()
        {
            if (busy) return;
            if (currentInstall == null || !currentInstall.IsValid)
            {
                SetStatus("未找到 Codex", "选择 Codex.exe 或 ChatGPT.exe", Danger);
                SetRestartButtonMode(false);
                primaryLabel.Text = "选择 Codex";
                primaryButton.IsEnabled = true;
                lastRunningCount = 0;
                return;
            }

            var running = CodexDiscovery.CountRunningCodexProcesses(currentInstall);
            if (running != lastRunningCount)
            {
                if (running > 0)
                    AppendLog("检测到 Codex 正在运行（" + running + " 个相关进程）。", TextSecondary);
                else if (lastRunningCount > 0)
                    AppendLog("Codex 已完全退出，可以重新汉化启动。", TextSecondary);
                lastRunningCount = running;
            }

            if (running > 0)
            {
                SetRestartButtonMode(true);
                if (!String.IsNullOrWhiteSpace(shutdownFailureDetail))
                {
                    SetStatus("无法关闭全部 Codex 进程", shutdownFailureDetail, Danger);
                    primaryLabel.Text = "重试关闭并重启";
                }
                else if (lastLaunchReport != null)
                {
                    var statusTitle = lastLaunchReport.Complete
                        ? "汉化已完成，Codex 正在运行"
                        : lastLaunchReport.LocaleApplied
                            ? "界面已汉化，菜单仍有遗漏"
                            : lastLaunchReport.MenuApplied
                                ? "菜单已汉化，界面待确认"
                                : "汉化未完全生效";
                    SetStatus(statusTitle, lastLaunchReport.Message, lastLaunchReport.Complete ? Accent : Warning);
                    primaryLabel.Text = "重新汉化并重启";
                }
                else
                {
                    SetStatus("Codex 正在运行", "可关闭当前进程并以中文模式重新启动", Warning);
                    primaryLabel.Text = "关闭并汉化重启";
                }
                primaryButton.IsEnabled = true;
                return;
            }

            shutdownFailureDetail = null;
            lastLaunchReport = null;
            SetRestartButtonMode(false);
            var source = currentInstall.Kind == "Microsoft Store"
                ? "Microsoft Store · 包版本 " + currentInstall.Version
                : currentInstall.DisplayName;
            SetStatus("已就绪", source, Accent);
            primaryLabel.Text = "汉化并启动";
            primaryButton.IsEnabled = true;
        }

        private async Task LaunchChineseAsync()
        {
            if (busy || currentInstall == null) return;
            shutdownFailureDetail = null;
            lastLaunchReport = null;
            SetBusy(true, "正在汉化并启动", "请稍候，进度会显示在下方日志中");
            AppendLog("开始启动中文版 Codex。", Accent);
            AppLog.Write("ui.launch.begin locale=zh-CN");
            try
            {
                var report = await LocalizationRuntime.LaunchAsync(
                    currentInstall,
                    "zh-CN",
                    delegate(string message) { AppendLog(message, TextSecondary); });
                lastLaunchReport = report;
                AppendLog("界面语言：" + (report.LocaleApplied ? "已确认中文" : "未确认"), report.LocaleApplied ? Accent : Warning);
                AppendLog("原生菜单：" + (report.MenuApplied ? "已全部覆盖" : "仍有未翻译项"), report.MenuApplied ? Accent : Warning);
                if (!String.IsNullOrWhiteSpace(report.LocaleDetail))
                    AppendLog("语言详情：" + report.LocaleDetail, TextSecondary);
                if (!String.IsNullOrWhiteSpace(report.MenuDetail))
                    AppendLog("菜单详情：" + report.MenuDetail, TextSecondary);
                AppendLog(report.Message, report.Complete ? Accent : Warning);
            }
            catch (Exception ex)
            {
                SetStatus("启动失败", ex.Message, Danger);
                AppendLog("启动失败：" + ex.Message, Danger);
                AppLog.Write("launch.ui.failed " + ex);
            }
            finally
            {
                SetBusy(false, null, null);
                UpdatePresentation();
            }
        }

        private async Task ConfirmCloseAndRestartAsync()
        {
            if (busy || currentInstall == null) return;
            var running = CodexProcessManager.Scan(currentInstall);
            if (running.TotalPotentialCount == 0)
            {
                await LaunchChineseAsync();
                return;
            }

            var answer = MessageBox.Show(
                this,
                "Codex 正在运行。\n\n工具会先请求正常退出；等待 5 秒后仍未退出的相关进程将被强制终止，然后自动以中文模式重新启动。\n\n正在执行的任务和未保存输入可能丢失。确定继续吗？",
                "确认关闭并重启",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                AppendLog("用户取消了关闭并重启操作。", TextSecondary);
                AppLog.Write("ui.restart.cancelled");
                return;
            }

            shutdownFailureDetail = null;
            lastLaunchReport = null;
            var restart = false;
            SetBusy(true, "正在关闭 Codex", "先正常退出，超时后再强制终止");
            AppendLog("用户已确认关闭并汉化重启。", Warning);
            AppLog.Write("ui.restart.confirmed");
            try
            {
                var report = await CodexProcessManager.ShutdownAsync(
                    currentInstall,
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(8),
                    delegate(string message) { AppendLog(message, TextSecondary); });
                var shutdownSummary =
                    "关闭统计：初始 " + report.InitialCount +
                    "，请求正常关闭 " + report.GracefulRequested +
                    "，正常退出 " + report.GracefulExited +
                    "，尝试强制终止 " + report.ForceAttempted +
                    "，强制终止 " + report.ForceTerminated +
                    "，剩余 " + report.RemainingCount + "。";
                AppendLog(shutdownSummary, report.Success ? Accent : Warning);
                AppLog.Write("ui.restart." + shutdownSummary);
                foreach (var error in report.Errors)
                    AppendLog(error, Danger);

                if (!report.Success)
                {
                    var ids = report.FailedProcessIds.Count == 0
                        ? "未知"
                        : String.Join(", ", report.FailedProcessIds.ConvertAll(delegate(int id) { return id.ToString(); }).ToArray());
                    shutdownFailureDetail = "仍有 " + report.RemainingCount + " 个候选进程，PID：" + ids;
                    SetStatus("无法关闭全部 Codex 进程", shutdownFailureDetail, Danger);
                    AppendLog("安全起见，已停止自动重启。", Danger);
                    return;
                }

                lastRunningCount = 0;
                AppendLog("等待系统释放单实例锁，然后重新启动。", TextSecondary);
                await Task.Delay(750);
                restart = true;
            }
            catch (Exception ex)
            {
                shutdownFailureDetail = ex.Message;
                SetStatus("关闭失败", ex.Message, Danger);
                AppendLog("关闭失败：" + ex.Message, Danger);
                AppLog.Write("restart.shutdown.failed " + ex);
            }
            finally
            {
                SetBusy(false, null, null);
                if (!restart) UpdatePresentation();
            }

            if (restart) await LaunchChineseAsync();
        }

        private async Task SelectPortableAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 Codex Desktop",
                Filter = "Codex Desktop|Codex.exe;ChatGPT.exe|可执行文件|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                currentInstall = CodexDiscovery.UsePortableExecutable(dialog.FileName);
                AppendLog("已选择便携版：" + dialog.FileName, Accent);
                UpdatePresentation();
            }
            catch (Exception ex)
            {
                SetStatus("无法使用所选文件", ex.Message, Danger);
                AppendLog("选择便携版失败：" + ex.Message, Danger);
                AppLog.Write("portable.select.failed " + ex);
            }
            await Task.FromResult(0);
        }

        private void ApplyToolIcon()
        {
            try
            {
                var icon = AppIcon.Load();
                if (icon == null) return;
                Icon = icon;
                logoImage.Source = icon;
            }
            catch (Exception ex)
            {
                AppLog.Write("icon.load.failed " + ex.Message);
            }
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                var result = await UpdateChecker.CheckAsync(AppInfo.Version);
                AppLog.Write("update.check " + result.Message);
                if (!result.UpdateAvailable || updatePromptShown || !IsVisible) return;
                updatePromptShown = true;
                AppendLog(result.Message + "，可从 GitHub Releases 下载。", Restart);
                var choice = MessageBox.Show(
                    this,
                    result.Message + "。\n\n是否打开下载页面？",
                    "发现新版本",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (choice == MessageBoxResult.Yes) AppInfo.OpenUrl(result.ReleaseUrl);
            }
            catch (Exception ex)
            {
                AppLog.Write("update.check.failed " + ex.Message);
            }
        }

        private void SetBusy(bool value, string title, string detail)
        {
            busy = value;
            progress.Opacity = value ? 1 : 0;
            primaryButton.IsEnabled = !value;
            if (value) primaryLabel.Text = "正在处理";
            if (title != null) SetStatus(title, detail ?? "", TextSecondary);
        }

        private void SetRestartButtonMode(bool value)
        {
            if (primaryButton == null || restartButtonMode == value) return;
            restartButtonMode = value;
            primaryButton.Background = value ? Restart : Accent;
            primaryButton.Template = value
                ? CreatePrimaryButtonTemplate(RestartHover, RestartPressed)
                : CreatePrimaryButtonTemplate();
        }

        private void SetStatus(string title, string detail, Brush color)
        {
            statusText.Text = title;
            detailText.Text = detail ?? "";
            detailText.ToolTip = String.IsNullOrWhiteSpace(detail) ? null : detail;
            statusDot.Fill = color;
        }

        private void AppendLog(string message, Brush color)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string, Brush>(AppendLog), message, color);
                return;
            }
            if (logBox == null || String.IsNullOrWhiteSpace(message)) return;
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 5) };
            paragraph.Inlines.Add(new Run(DateTime.Now.ToString("HH:mm:ss") + "  ") { Foreground = TextSecondary });
            paragraph.Inlines.Add(new Run(message) { Foreground = color });
            logBox.Document.Blocks.Add(paragraph);
            logBox.ScrollToEnd();
        }

        private void EnableDarkTitleBar()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                var enabled = 1;
                if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
            catch
            {
                // Older Windows versions keep the native title bar.
            }
        }

        private static void AddAt(Grid grid, UIElement element, int row)
        {
            Grid.SetRow(element, row);
            grid.Children.Add(element);
        }

        private static SolidColorBrush Brush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}
