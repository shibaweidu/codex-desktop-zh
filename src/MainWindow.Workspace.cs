using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace CodexZhLauncher
{
    internal sealed partial class MainWindow
    {
        private readonly WorkspaceSettings preferences = WorkspaceSettings.Load();
        private readonly Dictionary<string, UIElement> pages = new Dictionary<string, UIElement>();
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly List<Tuple<string,string,Brush>> workspaceLogs = new List<Tuple<string,string,Brush>>();
        private ContentControl pageHost;
        private TextBlock breadcrumb, footerStatus, installInfo, recentLog, downloadStatus, themeStatus, themePreview;
        private TextBox logSearch;
        private ComboBox logFilter, themeTarget, themeChoice;
        private Slider fontSlider;
        private StackPanel workspaceActions, downloadOptions;
        private FrameworkElement launchOptions, themeOptions;
        private Button cancelDownload, openDownload, updateCodexButton;
        private CancellationTokenSource downloadCancellation;
        private string downloadedFile;
        private CodexDownloadPlan downloadedPlan;
        private CodexDownloadPlan latestCodexPlan;
        private bool codexInstallRequired;
        private bool closeAfterDownload;
        private bool mirrorCheckStarted;
        private string activePage = "概览";

        private UIElement BuildWorkspace()
        {
            Resources["WorkspaceAccent"] = Brush(ThemePreset.Find(preferences.ToolTheme).Accent);
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(222) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var side = new Grid { Margin = new Thickness(17,24,17,17) };
            side.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            side.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            side.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
            side.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            var brand = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(7,0,0,28) };
            logoImage = new Image { Width=46, Height=46, Margin=new Thickness(0,0,12,0) };
            brand.Children.Add(logoImage);
            var brandText = new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            brandText.Children.Add(Label("Codex",22,true)); brandText.Children.Add(Label("汉化增强工具",11,false,true)); brand.Children.Add(brandText);
            side.Children.Add(brand);
            var nav = new StackPanel();
            var names = new[] { "概览", "下载 Codex", "界面汉化", "主题外观", "日志诊断" };
            for (var i=0;i<names.Length;i++)
            {
                var name = names[i];
                var button = ActionButton(name, delegate { ShowPage(name); });
                button.HorizontalContentAlignment=HorizontalAlignment.Left;
                button.Margin=new Thickness(0,0,0,8); button.Height=44;
                AutomationProperties.SetAutomationId(button, "nav-"+i);
                navigation[name]=button; nav.Children.Add(button);
            }
            Grid.SetRow(nav,1); side.Children.Add(nav);
            var bottom = new StackPanel();
            var sponsor = ActionButton("KAO LA API  ↗\n一站式顶级大模型调用平台\nappkaola.com", delegate { OpenExternal(AppInfo.SponsorUrl); });
            sponsor.Height=86; sponsor.Margin=new Thickness(0,0,0,18);
            sponsor.ToolTip=AppInfo.SponsorUrl; AutomationProperties.SetName(sponsor,"KAO LA API，一站式顶级大模型调用平台"); bottom.Children.Add(sponsor);
            workspaceActions = new StackPanel();
            workspaceActions.Children.Add(ActionButton("设置与关于", delegate { new AboutWindow(this, preferences).ShowDialog(); }));
            bottom.Children.Add(workspaceActions);
            bottom.Children.Add(Label("本地运行     v"+AppInfo.Version,10,false,true));
            Grid.SetRow(bottom,3); side.Children.Add(bottom);
            var sideBorder = new Border { Child=side, Background=Material("#303039","#25252D"), BorderBrush=Brush("#393941"), BorderThickness=new Thickness(0,0,1,0) };
            root.Children.Add(sideBorder);
            var main = new Grid(); Grid.SetColumn(main,1); root.Children.Add(main);
            main.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            breadcrumb=Label("工作台 / 概览",11,false,true); breadcrumb.Margin=new Thickness(32,20,32,5); main.Children.Add(breadcrumb);
            pageHost=new ContentControl { HorizontalContentAlignment=HorizontalAlignment.Stretch };
            var scroll=new ScrollViewer { Content=pageHost, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Padding=new Thickness(32,12,32,24) };
            Grid.SetRow(scroll,1); main.Children.Add(scroll);
            var foot = new StackPanel();
            progress=new ProgressBar { Height=3, IsIndeterminate=true, Foreground=Restart, Opacity=0, BorderThickness=new Thickness(0), Background=LogColor }; foot.Children.Add(progress);
            footerStatus=Label("正在检测本机安装",11,false,true); footerStatus.Margin=new Thickness(25,10,20,10); foot.Children.Add(footerStatus);
            Grid.SetRow(foot,2); main.Children.Add(foot);
            pages["概览"]=BuildOverviewPage(); pages["下载 Codex"]=BuildDownloadsPage(); pages["界面汉化"]=BuildLocalizationPage();
            pages["主题外观"]=BuildThemesPage(); pages["日志诊断"]=BuildDiagnosticsPage();
            ShowPage("概览"); return root;
        }

        private UIElement BuildOverviewPage()
        {
            var page=Page("一切就绪，开始创造。","中文启动与个性化，一处管理。");
            var hero=new StackPanel();
            var heading=Label("Codex Desktop",24,true); hero.Children.Add(heading);
            installInfo=Label("正在读取安装信息",12,false,true); installInfo.Margin=new Thickness(0,6,0,13); hero.Children.Add(installInfo);
            hero.Children.Add(BuildStatus()); hero.Children.Add(BuildAction());
            var options=new WrapPanel { Margin=new Thickness(0,12,0,0) };
            launchOptions=options;
            options.Children.Add(ActionButton("以英文启动", async delegate { await LaunchEnglishWorkspaceAsync(); }));
            options.Children.Add(ActionButton("选择应用", async delegate { await SelectPortableAsync(); }));
            options.Children.Add(ActionButton("重新检测", async delegate { await DetectInstallAsync(); })); hero.Children.Add(options);
            page.Children.Add(Card(hero));
            var quick = new UniformGridCompat();
            quick.Children.Add(ActionButton("下载与安装  →",delegate {ShowPage("下载 Codex");}));
            quick.Children.Add(ActionButton("换个外观  →",delegate {ShowPage("主题外观");})); page.Children.Add(quick);
            page.Children.Add(Label("最近操作",14,true)); recentLog=Label("等待操作记录",12,false,true); recentLog.LineHeight=25; page.Children.Add(Card(recentLog));
            return page;
        }
        private UIElement BuildLocalizationPage()
        {
            var page=Page("界面汉化","保留现有语言、原生菜单和安全关闭流程。");
            var stack=new StackPanel();
            foreach(var text in new[]{"1   确认安装与进程路径", "2   设置简体中文并加载官方资源", "3   验证页面实际语言", "4   检查原生菜单与未覆盖标签"})
            { var item=Label(text,14); item.Margin=new Thickness(0,10,0,14); stack.Children.Add(item); }
            stack.Children.Add(Label("验证结果写入日志。页面与菜单分别确认，不把请求成功当成全部汉化完成。",12,false,true));
            stack.Children.Add(ActionButton("转到启动控制",delegate {ShowPage("概览");}));
            stack.Children.Add(ActionButton("查看运行日志",delegate {ShowPage("日志诊断");})); page.Children.Add(Card(stack)); return page;
        }
        private UIElement BuildDownloadsPage()
        {
            var page=Page("Codex 更新","仅通过 Wangnov 社区镜像检查并安装更新。");
            downloadOptions=new StackPanel();
            downloadStatus=Label("进入此页后自动检查本机版本与社区镜像。",15,true);downloadOptions.Children.Add(downloadStatus);
            var detail=Label("自动识别 Windows 架构与已安装版本。安装前校验安装包大小、SHA-256 和 Microsoft 签名。",12,false,true);detail.Margin=new Thickness(0,6,0,14);downloadOptions.Children.Add(detail);
            updateCodexButton=ActionButton("检查并更新 Codex",async delegate {await CheckAndUpdateCodexAsync(false);});updateCodexButton.MinWidth=230;downloadOptions.Children.Add(updateCodexButton);
            page.Children.Add(Card(downloadOptions));
            var result=new StackPanel();cancelDownload=ActionButton("取消下载",delegate {if(downloadCancellation!=null) downloadCancellation.Cancel();});cancelDownload.IsEnabled=false;result.Children.Add(cancelDownload);
            openDownload=ActionButton("重试系统安装",async delegate {await InstallVerifiedCodexAsync();});openDownload.IsEnabled=false;result.Children.Add(openDownload);
            result.Children.Add(ActionButton("打开社区镜像项目",delegate {OpenExternal("https://github.com/Wangnov/codex-app-mirror");})); page.Children.Add(Card(result)); return page;
        }
        private UIElement BuildThemesPage()
        {
            var page=Page("让工作台，有你的样子。","项目自有纯色预设，不下载第三方素材和脚本。");
            var stack=new StackPanel();
            themeOptions=stack;
            themeTarget=Select(new[]{"Codex 运行时配色", "工具强调色"},"应用对象"); stack.Children.Add(themeTarget);
            themeChoice=Select(ThemePreset.All.Select(t=>t.Name).ToArray(),"配色预设");
            themeChoice.SelectedIndex=Math.Max(0,Array.FindIndex(ThemePreset.All,t=>t.Id==preferences.CodexTheme));
            themeTarget.SelectionChanged+=delegate { themeChoice.SelectedIndex=Math.Max(0,Array.FindIndex(ThemePreset.All,t=>t.Id==(themeTarget.SelectedIndex==1?preferences.ToolTheme:preferences.CodexTheme))); UpdateThemePreview(); };
            themeChoice.SelectionChanged+=delegate { UpdateThemePreview(); }; stack.Children.Add(themeChoice);
            themePreview=Label("今天，想构建什么？",21,true); themePreview.Padding=new Thickness(22); themePreview.MinHeight=100; stack.Children.Add(themePreview);
            stack.Children.Add(Label("预览字号（12–18）",12,false,true));
            fontSlider=new Slider { Minimum=12, Maximum=18, TickFrequency=1, IsSnapToTickEnabled=true, Value=preferences.FontSize, Margin=new Thickness(0,12,0,15) };
            fontSlider.ValueChanged+=delegate { UpdateThemePreview(); }; stack.Children.Add(fontSlider);
            themeStatus=Label("Codex 配色仅在本工具启动的会话中生效；不修改原生设置或代码语法颜色。",12,false,true); stack.Children.Add(themeStatus);
            stack.Children.Add(ActionButton("应用所选配色",async delegate {await ApplyWorkspaceThemeAsync(false);}));
            stack.Children.Add(ActionButton("恢复默认",async delegate {await ApplyWorkspaceThemeAsync(true);}));
            page.Children.Add(Card(stack)); UpdateThemePreview(); return page;
        }
        private UIElement BuildDiagnosticsPage()
        {
            var page=Page("每一步，都看得见。","本机日志、汉化结果和失败原因。");
            var tools=new WrapPanel();
            tools.Children.Add(ActionButton("打开日志目录",delegate {OpenExternal(AppLog.DirectoryPath);}));
            tools.Children.Add(ActionButton("复制日志",delegate { try {Clipboard.SetText(String.Join(Environment.NewLine,workspaceLogs.Select(x=>x.Item1+" "+x.Item2)));} catch(Exception ex){AppendLog(ex.Message,Danger);} }));
            tools.Children.Add(ActionButton("导出日志",delegate {ExportWorkspaceLog();})); page.Children.Add(tools);
            logSearch=new TextBox { Height=35, Padding=new Thickness(8), Margin=new Thickness(0,14,0,8), Background=LogColor, Foreground=TextPrimary, BorderBrush=BorderColor };
            AutomationProperties.SetName(logSearch,"搜索日志"); logSearch.TextChanged+=delegate {RenderWorkspaceLogs();}; page.Children.Add(logSearch);
            logFilter=Select(new[]{"全部日志", "提示与错误"},"日志级别"); logFilter.SelectionChanged+=delegate {RenderWorkspaceLogs();}; page.Children.Add(logFilter);
            var log=BuildLogPanel(); ((FrameworkElement)log).Height=330; page.Children.Add(log); return page;
        }
        private async Task CheckAndUpdateCodexAsync(bool automatic)
        {
            if(busy) return;
            downloadedFile=null;downloadedPlan=null;latestCodexPlan=null;codexInstallRequired=false;openDownload.IsEnabled=false;cancelDownload.IsEnabled=false;
            downloadCancellation=new CancellationTokenSource(); var token=downloadCancellation.Token;
            var installWhenReady=false;
            CodexInstall detectedStore=null;
            SetBusy(true,"正在检查 Codex 版本","仅使用社区镜像清单");updateCodexButton.IsEnabled=false;
            try
            {
                var localTask=CodexDiscovery.DetectStorePackageAsync();
                var arch=CodexDownloads.NativeArchitecture;
                var plan=await CodexDownloads.ResolveAsync("windows",arch,token);
                var local=await localTask;detectedStore=local;
                if(currentInstall!=null&&!currentInstall.IsStorePackage)
                    throw new InvalidOperationException("当前选择的是便携版。社区镜像仅提供 Microsoft Store MSIX，不能覆盖便携版；已停止以免更新错目标。");
                if(local==null && currentInstall!=null && !currentInstall.IsStorePackage)
                    throw new InvalidOperationException("检测到便携版。社区镜像提供 Microsoft Store 签名包，不能安全覆盖便携版；请先安装 Store 版或继续使用原有便携版更新方式。");
                if(local==null)
                {
                    latestCodexPlan=plan;codexInstallRequired=true;
                    downloadStatus.Text="已发现可用版本 "+plan.PackageVersion+"（"+arch+"）\n尚未检测到 Store 安装，可下载并安装。";
                }
                else if(!CodexDownloads.NeedsUpdate(local.Version,plan.PackageVersion))
                {
                    latestCodexPlan=null;codexInstallRequired=false;
                    downloadStatus.Text="已是最新版本\n本机 "+local.Version+" · 镜像 "+plan.PackageVersion+"（"+arch+"）";
                    AppendLog("Codex 已是最新版本 "+local.Version+"；未下载、未修改。",Accent);return;
                }
                else
                {
                    latestCodexPlan=plan;codexInstallRequired=true;
                    downloadStatus.Text="发现新版本 "+plan.PackageVersion+"\n本机 "+local.Version+" → 社区镜像；正在下载…";
                }
                if(automatic){AppendLog(downloadStatus.Text,TextSecondary);}
                AppendLog("社区镜像下载计划："+plan.PackageVersion+" "+arch+" SHA-256="+plan.Sha256,TextSecondary);
                var downloadProgress=new Progress<int>(n=>{if(IsLoaded) downloadStatus.Text="正在下载 "+plan.Version+"（"+n+"%）";});
                downloadedFile=await CodexDownloads.DownloadAsync(plan,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","Codex"),downloadProgress,token);
                downloadedPlan=plan;
                using(var verify=Process.Start(new ProcessStartInfo{FileName="powershell.exe",Arguments="-NoProfile -NonInteractive -Command "+ProcessRunner.QuoteArgument("$s=Get-AuthenticodeSignature -LiteralPath "+ProcessRunner.PowerShellLiteral(downloadedFile)+"; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'Microsoft') { exit 2 }") ,UseShellExecute=false,CreateNoWindow=true}))
                {await Task.Run(()=>verify.WaitForExit());if(verify.ExitCode!=0)throw new InvalidDataException("Microsoft Authenticode 签名无效，已拒绝安装。");}
                downloadStatus.Text="下载与 SHA-256 校验通过。Windows 将验证 Microsoft 包签名。\n"+(CodexDiscovery.CountRunningCodexProcesses(local??currentInstall)>0?"Codex 正在运行；请先退出，再点“重试系统安装”。":"准备请求系统安装授权…");
                AppendLog("Codex 安装包 SHA-256 已验证；MSIX 身份锁定 OpenAI.Codex，签名由 Windows AppX 部署服务验证。",Accent);
                if(CodexDiscovery.CountRunningCodexProcesses(local??currentInstall)>0){openDownload.IsEnabled=true;return;}
                installWhenReady=true;
            }
            catch(OperationCanceledException) {downloadStatus.Text="下载已取消，未验证文件已清理。"; AppendLog(downloadStatus.Text,TextSecondary);}
            catch(Exception ex) {downloadStatus.Text="更新失败："+ex.Message; AppendLog(downloadStatus.Text,Danger);}
            finally {cancelDownload.IsEnabled=false;downloadCancellation.Dispose();downloadCancellation=null;SetBusy(false,null,null);if(updateCodexButton!=null)updateCodexButton.IsEnabled=true;UpdatePresentation();if(closeAfterDownload)Close();}
            if(installWhenReady)await InstallVerifiedCodexAsync(detectedStore);
        }
        private async Task InstallVerifiedCodexAsync(CodexInstall detectedStore=null)
        {
            if(busy||String.IsNullOrWhiteSpace(downloadedFile)||downloadedPlan==null||!File.Exists(downloadedFile))return;
            var installed=await CodexDiscovery.DetectStorePackageAsync();if(detectedStore!=null)installed=detectedStore;
            if(CodexDiscovery.CountRunningCodexProcesses(installed??currentInstall)>0){downloadStatus.Text="Codex 仍在运行，系统无法安全覆盖。退出 Codex 后再点击重试。";AppendLog(downloadStatus.Text,Warning);return;}
            if(!codexInstallRequired || !CodexDownloads.NeedsUpdate(installed==null?null:installed.Version,downloadedPlan.PackageVersion))
            {downloadStatus.Text="本机版本已不低于该安装包版本；为避免降级，已取消安装。";openDownload.IsEnabled=false;AppendLog(downloadStatus.Text,Warning);return;}
            if(downloadedPlan.Architecture!=CodexDownloads.NativeArchitecture){downloadStatus.Text="安装包架构与本机不匹配，已拒绝安装。";return;}
            var confirmation=MessageBox.Show(this,"将从社区镜像安装 Codex "+downloadedPlan.PackageVersion+"，并更新本机 Store 应用。\n\nWindows 可能显示 UAC 管理员授权提示；只有批准后安装才会继续。请先保存工作并确认 Codex 已关闭。是否继续？","确认安装 Codex 更新",MessageBoxButton.YesNo,MessageBoxImage.Warning);
            if(confirmation!=MessageBoxResult.Yes){downloadStatus.Text="已保留下载包；你可以稍后安装或重新检查版本。";AppendLog("用户取消 Codex 安装。",TextSecondary);return;}
            if(CodexDiscovery.CountRunningCodexProcesses(installed??currentInstall)>0){downloadStatus.Text="检测到 Codex 又启动了，请退出后重试安装。";AppendLog(downloadStatus.Text,Warning);return;}
            try
            {
                var script=CodexDownloads.BuildWindowsInstallScript(downloadedPlan,downloadedFile);
                var args="-NoProfile -NonInteractive -Command "+ProcessRunner.QuoteArgument(script);
                SetBusy(true,"正在通过 Windows 部署更新 Codex","保持 Codex 关闭；请查看系统错误");
                var result=await RunElevatedInstallAsync(script);
                if(!result.Item1)throw new InvalidOperationException(result.Item2);
                currentInstall=await CodexDiscovery.DetectStorePackageAsync();
                if(currentInstall==null||CodexDownloads.NeedsUpdate(currentInstall.Version,downloadedPlan.PackageVersion))throw new InvalidOperationException("部署命令完成，但已安装包版本仍未达到目标版本。");
                downloadStatus.Text="Codex 已更新到 "+currentInstall.Version+"。";AppendLog(downloadStatus.Text,Accent);openDownload.IsEnabled=false;try{File.Delete(downloadedFile);}catch(Exception cleanup){AppendLog("更新已成功，但下载包清理失败："+cleanup.Message,Warning);}downloadedFile=null;downloadedPlan=null;codexInstallRequired=false;
                RefreshWorkspaceStatus("Codex 已更新");
            }
            catch(Exception ex){downloadStatus.Text="系统安装失败："+ex.Message;AppendLog(downloadStatus.Text,Danger);}
            finally{SetBusy(false,null,null);UpdatePresentation();}
        }
        private async Task<Tuple<bool,string>> RunElevatedInstallAsync(string installScript)
        {
            var id=Guid.NewGuid().ToString("N");var scriptPath=Path.Combine(Path.GetTempPath(),"CodexZhInstall-"+id+".ps1");var resultPath=scriptPath+".result";
            var sid=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            var wrapped="$ErrorActionPreference='Stop'; if([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne "+ProcessRunner.PowerShellLiteral(sid)+"){throw '管理员账户与当前登录用户不同；为避免安装到错误账户，已取消。'}; try {"+installScript+"; [IO.File]::WriteAllText("+ProcessRunner.PowerShellLiteral(resultPath)+",'OK',[Text.Encoding]::UTF8); exit 0} catch { [IO.File]::WriteAllText("+ProcessRunner.PowerShellLiteral(resultPath)+",$_.ToString(),[Text.Encoding]::UTF8); exit 1 }";
            try
            {
                File.WriteAllText(scriptPath,wrapped,new System.Text.UTF8Encoding(false));
                using(var process=Process.Start(new ProcessStartInfo{FileName="powershell.exe",Arguments="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+ProcessRunner.QuoteArgument(scriptPath),UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden}))
                    await Task.Run(()=>process.WaitForExit());
                var detail=File.Exists(resultPath)?File.ReadAllText(resultPath,System.Text.Encoding.UTF8).Trim():"Windows 安装程序未返回详细信息。";
                return Tuple.Create(detail=="OK",detail=="OK"?"":detail);
            }
            catch(System.ComponentModel.Win32Exception ex)
            {return Tuple.Create(false,ex.NativeErrorCode==1223?"已取消 UAC 授权，Codex 未安装。":"无法启动管理员安装："+ex.Message);}
            catch(Exception ex){return Tuple.Create(false,"管理员安装失败："+ex.Message);}
            finally{try{if(File.Exists(scriptPath))File.Delete(scriptPath);}catch{}try{if(File.Exists(resultPath))File.Delete(resultPath);}catch{}}
        }
        private async Task ApplyWorkspaceThemeAsync(bool reset)
        {
            if(busy) return;
            var preset=ThemePreset.All[Math.Max(0,themeChoice.SelectedIndex)]; var font=(int)fontSlider.Value;
            if(themeTarget.SelectedIndex==1)
            {
                var old=preferences.ToolTheme; preferences.ToolTheme=reset?"violet":preset.Id;
                if(!SaveWorkspacePreferences()) {preferences.ToolTheme=old;return;}
                ApplyToolAccent();
                themeStatus.Text="工具强调色已保存。工具继续使用深色材质。"; return;
            }
            var selected=reset?"default":preset.Id;
            if(lastLaunchReport==null || currentInstall==null || CodexDiscovery.CountRunningCodexProcesses(currentInstall)==0)
            {
                var oldTheme=preferences.CodexTheme;var oldFont=preferences.FontSize;
                preferences.CodexTheme=selected; preferences.FontSize=font;
                if(SaveWorkspacePreferences()) themeStatus.Text="偏好已保存，尚未应用到 Codex；请通过本工具汉化启动后再应用。";
                else {preferences.CodexTheme=oldTheme;preferences.FontSize=oldFont;}
                return;
            }
            SetBusy(true,"正在应用运行时配色","不会修改安装文件");
            try
            {
                var result=await LocalizationRuntime.ApplyThemeAsync(lastLaunchReport,selected,font);
                var values=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(result);
                object status;
                var ok=values!=null && values.TryGetValue("status",out status) && Convert.ToString(status)=="ok";
                themeStatus.Text=ok ? (reset?"已移除工具注入的样式，恢复应用原有外观。":"根界面配色已验证；原生菜单和代码语法颜色未修改。") : "当前版本配色未通过验证，未保留新样式。";
                if(ok) {var oldTheme=preferences.CodexTheme;var oldFont=preferences.FontSize;preferences.CodexTheme=selected;preferences.FontSize=font;if(!SaveWorkspacePreferences()){preferences.CodexTheme=oldTheme;preferences.FontSize=oldFont;}}
                AppendLog("配色结果："+result,ok?Accent:Warning);
            }
            catch(Exception ex){themeStatus.Text="配色未完成："+ex.Message;AppendLog(themeStatus.Text,Warning);}
            finally{SetBusy(false,null,null);UpdatePresentation();}
        }
        private bool SaveWorkspacePreferences()
        {try{preferences.Save();return true;}catch(Exception ex){themeStatus.Text="设置未保存："+ex.Message;AppendLog(themeStatus.Text,Danger);return false;}}
        private void UpdateThemePreview()
        {
            if(themePreview==null || themeChoice==null) return;
            var preset=ThemePreset.All[Math.Max(0,themeChoice.SelectedIndex)];themePreview.Background=Brush(preset.Background);themePreview.Foreground=Brush(preset.Foreground);
            themePreview.FontSize=fontSlider==null?preferences.FontSize:fontSlider.Value;
        }
        private async Task LaunchEnglishWorkspaceAsync()
        {
            if(busy || currentInstall==null) return;
            if(CodexDiscovery.CountRunningCodexProcesses(currentInstall)>0){AppendLog("请先完全退出 Codex 再使用英文启动。",Warning);return;}
            lastLaunchLocale="en-US";lastOperationError=null;
            SetBusy(true,"正在英文启动","请查看运行日志");
            try{lastLaunchReport=await LocalizationRuntime.LaunchAsync(currentInstall,"en-US",m=>AppendLog(m,TextSecondary));AppendLog(lastLaunchReport.Message,Accent);}
            catch(Exception ex){lastOperationError=ex.Message;AppendLog("英文启动失败："+ex.Message,Danger);}
            finally{SetBusy(false,null,null);UpdatePresentation();}
        }
        private void ShowPage(string name)
        {activePage=name;pageHost.Content=pages[name];breadcrumb.Text="工作台 / "+name;foreach(var item in navigation){item.Value.Background=item.Key==name?Brush("#3C354E"):Brush("#292A32");item.Value.Foreground=item.Key==name?(Brush)Resources["WorkspaceAccent"]:TextSecondary;}if(name=="下载 Codex"&&!mirrorCheckStarted&&IsLoaded){mirrorCheckStarted=true;StartCodexMirrorCheck();}}
        private async void StartCodexMirrorCheck(){await CheckAndUpdateCodexAsync(true);}
        private void ApplyToolAccent()
        {
            // Keep readable contrast on dark chrome even when the selected Codex preset is light.
            var selected=Brush(ThemePreset.Find(preferences.ToolTheme).Accent);
            var c=selected.Color;
            if(Math.Max(c.R,Math.Max(c.G,c.B))<170) selected=Brush("#C4B5FD");
            Resources["WorkspaceAccent"]=selected;
            UpdatePrimaryAccent();
            if(progress!=null)progress.Foreground=selected;
            ShowPage(activePage);
        }
        private void UpdatePrimaryAccent()
        {
            if(primaryButton==null)return;
            var selected=(SolidColorBrush)Resources["WorkspaceAccent"];var c=selected.Color;
            primaryButton.Background=selected;
            primaryButton.Template=CreatePrimaryButtonTemplate(new SolidColorBrush(Color.FromRgb((byte)(c.R+(255-c.R)*.2),(byte)(c.G+(255-c.G)*.2),(byte)(c.B+(255-c.B)*.2))),new SolidColorBrush(Color.FromRgb((byte)(c.R*.85),(byte)(c.G*.85),(byte)(c.B*.85))));
        }
        private void RefreshWorkspaceStatus(string title)
        {if(footerStatus!=null)footerStatus.Text=title+" · "+(busy?"处理中":"本地运行");if(installInfo!=null)installInfo.Text=currentInstall==null?"尚未发现客户端，可选择应用或前往下载。":currentInstall.DisplayName+" / "+currentInstall.Kind+" / "+currentInstall.Version;}
        private void RecordWorkspaceLog(string message,Brush color)
        {
            AppLog.Write("ui "+message);
            workspaceLogs.Add(Tuple.Create(DateTime.Now.ToString("HH:mm:ss"),message.Length>6000?message.Substring(0,6000)+"…":message,color));
            if(workspaceLogs.Count>300)workspaceLogs.RemoveAt(0);
            if(recentLog!=null)recentLog.Text=String.Join(Environment.NewLine,workspaceLogs.Skip(Math.Max(0,workspaceLogs.Count-3)).Select(x=>x.Item1+"  "+(x.Item2.Length>110?x.Item2.Substring(0,110)+"…":x.Item2)));
            RenderWorkspaceLogs();
        }
        private void RenderWorkspaceLogs()
        {
            if(logBox==null)return;logBox.Document.Blocks.Clear();var query=logSearch==null?"":logSearch.Text;
            foreach(var entry in workspaceLogs)
            {
                if(query.Length>0 && entry.Item2.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                if(logFilter!=null&&logFilter.SelectedIndex==1&&entry.Item3!=Danger&&entry.Item3!=Warning)continue;
                var p=new Paragraph{Margin=new Thickness(0,0,0,7)};p.Inlines.Add(new Run(entry.Item1+"  "){Foreground=TextSecondary});p.Inlines.Add(new Run(entry.Item2){Foreground=entry.Item3});logBox.Document.Blocks.Add(p);
            }
            if(logBox.Document.Blocks.Count==0)logBox.Document.Blocks.Add(new Paragraph(new Run("没有匹配的日志") {Foreground=TextSecondary}));
            logBox.ScrollToEnd();
        }
        private void ExportWorkspaceLog()
        {
            var dialog=new SaveFileDialog{Filter="文本日志|*.txt",FileName="codex-launcher-log.txt"};if(dialog.ShowDialog(this)!=true)return;
            try{File.WriteAllLines(dialog.FileName,workspaceLogs.Select(x=>x.Item1+" "+x.Item2),System.Text.Encoding.UTF8);}catch(Exception ex){AppendLog("导出失败："+ex.Message,Danger);}
        }
        private void OpenExternal(string path)
        {try{if(path==AppLog.DirectoryPath)Directory.CreateDirectory(path);AppInfo.OpenUrl(path);}catch(Exception ex){AppendLog("打开失败："+ex.Message,Danger);}}
        private void OnWorkspaceClosing(object sender,CancelEventArgs e)
        {
            if(allowsUpdateExit)return;
            if(busy && downloadCancellation==null){e.Cancel=true;MessageBox.Show(this,"当前操作正在执行，请完成后关闭工具。","正在处理",MessageBoxButton.OK,MessageBoxImage.Information);return;}
            if(downloadCancellation!=null){e.Cancel=true;closeAfterDownload=true;downloadCancellation.Cancel();}
        }
        private StackPanel Page(string title,string detail)
        {var p=new StackPanel();p.Children.Add(Label(title,25,true));var sub=Label(detail,12,false,true);sub.Margin=new Thickness(0,7,0,24);p.Children.Add(sub);return p;}
        private TextBlock Label(string text,double size=13,bool bold=false,bool muted=false)
        {return new TextBlock{Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=muted?TextSecondary:TextPrimary,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)};}
        private Border Card(UIElement child)
        {return new Border{Child=child,Padding=new Thickness(24),Margin=new Thickness(0,0,0,22),Background=Material("#34353F","#292A32"),BorderBrush=BorderColor,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Effect=new DropShadowEffect{BlurRadius=15,ShadowDepth=4,Opacity=.18,Color=Colors.Black}};}
        private static Brush Material(string top,string bottom)
        {return new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),(Color)ColorConverter.ConvertFromString(bottom),90);}
        private Button ActionButton(string label,Action action)
        {
            var b=new Button{Content=label,Padding=new Thickness(14,9,14,9),MinHeight=38,Margin=new Thickness(0,7,10,3),Foreground=TextPrimary,Background=Brush("#393A44"),BorderBrush=BorderColor,Cursor=Cursors.Hand,Template=CreateButtonTemplate(10)};
            AutomationProperties.SetName(b,label);b.Click+=delegate{action();};return b;
        }
        private ComboBox Select(string[] choices,string label)
        {
            var box=new ComboBox{ItemsSource=choices,SelectedIndex=0,MinHeight=38,Margin=new Thickness(0,10,0,18),MaxWidth=520,HorizontalAlignment=HorizontalAlignment.Left,MinWidth=280,Foreground=TextPrimary,Background=LogColor};
            AutomationProperties.SetName(box,label);
            box.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBox}'>
              <Grid>
                <ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                  <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='#25262E' BorderBrush='#51515E' BorderThickness='1' CornerRadius='8'/></ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Margin='12,8,30,8' VerticalAlignment='Center' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}'/>
                <TextBlock Text='⌄' Margin='0,0,12,0' HorizontalAlignment='Right' VerticalAlignment='Center' IsHitTestVisible='False' Foreground='#C4B5FD'/>
                <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='None'>
                  <Border MinWidth='{TemplateBinding ActualWidth}' Background='#30313B' BorderBrush='#595967' BorderThickness='1' CornerRadius='8' Padding='5'>
                    <ScrollViewer MaxHeight='270' CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
                  </Border>
                </Popup>
              </Grid>
              <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate>");
            var itemStyle=new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,TextPrimary));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBoxItem}'>
              <Border x:Name='Item' Background='Transparent' CornerRadius='5' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
              <ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='#4A425E'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter Property='Foreground' Value='#DFD5FF'/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate>")));
            box.ItemContainerStyle=itemStyle;return box;
        }
        private sealed class UniformGridCompat : System.Windows.Controls.Primitives.UniformGrid
        {public UniformGridCompat(){Columns=2;Margin=new Thickness(0,0,0,18);}}
    }
}
