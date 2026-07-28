using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CodexZhLauncher
{
    [Flags]
    internal enum ActivateOptions
    {
        None = 0x00000000,
        DesignMode = 0x00000001,
        NoErrorUi = 0x00000002,
        NoSplashScreen = 0x00000004
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    internal class ApplicationActivationManager
    {
    }

    internal static class AppActivation
    {
        public static uint Launch(CodexInstall install, string arguments)
        {
            if (install == null || !install.IsValid)
                throw new InvalidOperationException("未检测到可用的 Codex Desktop。 ");

            if (install.IsStorePackage)
            {
                var manager = (IApplicationActivationManager)new ApplicationActivationManager();
                uint processId;
                var result = manager.ActivateApplication(
                    install.AppUserModelId,
                    arguments,
                    ActivateOptions.NoErrorUi,
                    out processId);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                return processId;
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = install.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = install.InstallLocation
            });
            if (process == null) throw new InvalidOperationException("Codex Desktop 启动失败。");
            return unchecked((uint)process.Id);
        }

        public static int ReserveLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        public static string BuildArguments(int rendererPort, int inspectorPort, string locale)
        {
            return String.Join(" ", new[]
            {
                "--remote-debugging-address=127.0.0.1",
                "--remote-debugging-port=" + rendererPort,
                "--inspect=127.0.0.1:" + inspectorPort,
                "--lang=" + locale
            });
        }
    }
}

