using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

[assembly: System.Reflection.AssemblyTitle("Codex 汉化增强工具")]
[assembly: System.Reflection.AssemblyDescription("Codex Desktop 中文界面与原生菜单运行时增强工具")]
[assembly: System.Reflection.AssemblyCompany("Codex Localization Enhancer Community")]
[assembly: System.Reflection.AssemblyProduct("Codex 汉化增强工具")]
[assembly: System.Reflection.AssemblyCopyright("Copyright 2026")]
[assembly: System.Reflection.AssemblyVersion("0.7.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.7.2.0")]

namespace CodexZhLauncher
{
    internal static class Program
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && String.Equals(args[0], "--apply-update-windows", StringComparison.Ordinal))
            {
                Environment.ExitCode = UpdateInstaller.ApplyWindowsUpdate(args);
                return;
            }

            if (args.Length > 0)
            {
                AttachConsole(AttachParentProcess);
                Console.OutputEncoding = Encoding.UTF8;
                try
                {
                    RunCommand(args);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("error: " + ex.Message);
                    AppLog.Write("command.failed " + ex);
                    Environment.ExitCode = 1;
                }
                return;
            }

            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            UpdateInstaller.ScheduleCleanup();
            app.Run(new MainWindow());
        }

        private static void RunCommand(string[] args)
        {
            var command = args[0].ToLowerInvariant();
            if (command == "--self-test")
            {
                Output(LocalizationScripts.SelfTest());
                Output(UpdateChecker.SelfTest());
                Output(UpdateInstaller.SelfTest());
                return;
            }

            var install = CodexDiscovery.DetectAsync().GetAwaiter().GetResult();
            if (command == "--diagnostics")
            {
                PrintDiagnostics(install);
                return;
            }

            if (command == "--launch-zh" || command == "--launch-en")
            {
                var locale = command == "--launch-zh" ? "zh-CN" : "en-US";
                var report = LocalizationRuntime.LaunchAsync(install, locale).GetAwaiter().GetResult();
                Output(report.Message);
                Output(String.Format("pid={0}; renderer_port={1}; inspector_port={2}",
                    report.ProcessId, report.RendererPort, report.InspectorPort));
                Output(String.Format("locale={0}; menu={1}", report.LocaleApplied, report.MenuApplied));
                Environment.ExitCode = report.Complete ? 0 : 2;
                return;
            }

            throw new ArgumentException("未知参数。可用参数：--diagnostics、--self-test、--launch-zh、--launch-en");
        }

        private static void PrintDiagnostics(CodexInstall install)
        {
            Output("Codex Localization Enhancer " + AppInfo.Version);
            Output("os=" + Environment.OSVersion.VersionString);
            Output("64bit=" + Environment.Is64BitOperatingSystem);
            if (install == null)
            {
                Output("codex=not-found");
                Environment.ExitCode = 2;
                return;
            }
            Output("codex=found");
            Output("kind=" + install.Kind);
            Output("version=" + install.Version);
            Output("location=" + install.InstallLocation);
            Output("aumid=" + (install.AppUserModelId ?? "-"));
            Output("running_processes=" + CodexDiscovery.CountRunningCodexProcesses(install));
            Output("log=" + AppLog.FilePath);
        }

        private static void Output(string value)
        {
            Console.WriteLine(value);
            AppLog.Write("cli " + value);
        }
    }
}
