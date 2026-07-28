using System;

namespace CodexZhLauncher
{
    internal sealed class CodexInstall
    {
        public string Kind;
        public string DisplayName;
        public string Version;
        public string InstallLocation;
        public string ExecutablePath;
        public string AppUserModelId;

        public bool IsStorePackage
        {
            get { return !String.IsNullOrWhiteSpace(AppUserModelId); }
        }

        public bool IsValid
        {
            get
            {
                return IsStorePackage ||
                    (!String.IsNullOrWhiteSpace(ExecutablePath) && System.IO.File.Exists(ExecutablePath));
            }
        }
    }

    internal sealed class LaunchReport
    {
        public bool Started;
        public bool LocaleApplied;
        public bool MenuApplied;
        public uint ProcessId;
        public int RendererPort;
        public int InspectorPort;
        public string Message;
        public string LocaleDetail;
        public string MenuDetail;

        public bool Complete
        {
            get { return Started && LocaleApplied && MenuApplied; }
        }
    }

    internal sealed class ProcessResult
    {
        public int ExitCode;
        public string Output;
        public string Error;

        public bool Success
        {
            get { return ExitCode == 0; }
        }
    }
}

