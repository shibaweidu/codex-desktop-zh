using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexZhLauncher
{
    internal sealed class CodexDownloadPlan
    {
        public string Platform, Architecture, Version, PackageVersion, PackageFamilyName, Sha256, FileName;
        public Uri Url;
        public long Length;
    }

    internal static class CodexDownloads
    {
        internal const string WindowsPublisher = "CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B";
        public const string ManifestUrl = "https://codexapp.agentsmirror.com/latest/manifest";
        public static async Task<CodexDownloadPlan> ResolveAsync(string platform, string architecture, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            using (var response = await client.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                var manifestUri=response.RequestMessage.RequestUri;
                if(manifestUri==null||manifestUri.Scheme!="https"||(manifestUri.Host!="codexapp.agentsmirror.com"&&manifestUri.Host!="codexapp-r2.agentsmirror.com"&&manifestUri.Host!="codexapp-s3.agentsmirror.com"))
                    throw new InvalidDataException("版本清单重定向到了未许可的来源。");
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192]; int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("下载清单过大。");
                        output.Write(buffer, 0, count);
                    }
                    token.ThrowIfCancellationRequested();
                    return Parse(System.Text.Encoding.UTF8.GetString(output.ToArray()), platform, architecture);
                }
            }
        }
        public static CodexDownloadPlan Parse(string json, string platform, string architecture)
        {
            if ((platform != "windows" && platform != "macos") || (architecture != "x64" && architecture != "arm64"))
                throw new ArgumentException("不支持的平台或架构。");
            var root = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
            var sources = Object(root, "sources");
            var os = Object(sources, platform);
            var entry = Object(platform == "windows" ? Object(os, "architectures") : os, architecture);
            object available;
            if (!entry.TryGetValue("downloadable", out available) || !(available is bool) || !(bool)available)
                throw new InvalidOperationException("该平台/架构当前没有可下载的安装包。");
            var plan = new CodexDownloadPlan { Platform=platform, Architecture=architecture };
            plan.Version = Text(entry, platform == "windows" ? "appVersion" : "bundleShortVersion");
            plan.PackageVersion = Text(entry, platform == "windows" ? "version" : "bundleVersion");
            plan.Length = Convert.ToInt64(entry["contentLength"]);
            if (platform == "windows")
            {
                var catalog = Object(entry, "catalog");
                plan.PackageFamilyName = Text(catalog,"packageFamilyName");
                if(!String.Equals(plan.PackageFamilyName,"OpenAI.Codex_2p2nqsd0c76g0",StringComparison.Ordinal))
                    throw new InvalidDataException("社区镜像 MSIX 包身份不属于 Codex Store 应用。");
                Version packageVersion;
                if(!TryNumericVersion(plan.PackageVersion,out packageVersion))throw new InvalidDataException("社区镜像 Windows 包版本无效。");
                if (!String.Equals(Text(catalog, "hashAlgorithm"), "SHA256", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包缺少 SHA-256 校验信息。");
                var hash = Convert.FromBase64String(Text(catalog, "hash"));
                if (hash.Length != 32) throw new InvalidDataException("SHA-256 长度不正确。");
                plan.Sha256 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                plan.Url = new Uri("https://codexapp.agentsmirror.com/latest/win-" + architecture);
            }
            else
            {
                plan.Sha256 = Text(entry, "sha256").ToLowerInvariant();
                var official = new Uri(Text(entry, "url"));
                if (official.Scheme != "https" || official.Host != "persistent.oaistatic.com" || !official.AbsolutePath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("macOS 下载地址不是预期的官方 DMG。");
                plan.Url = new Uri("https://codexapp.agentsmirror.com/latest/" + (architecture == "arm64" ? "mac-arm64" : "mac-intel"));
            }
            if (plan.Length <= 0 || plan.Length > 4L * 1024 * 1024 * 1024 || !IsHash(plan.Sha256) ||
                String.IsNullOrWhiteSpace(plan.Version) || plan.Version.Length > 80 || !System.Text.RegularExpressions.Regex.IsMatch(plan.Version, @"^[0-9A-Za-z.\-]+$"))
                throw new InvalidDataException("下载清单的版本、大小或校验信息无效。");
            plan.FileName = "Codex-" + (platform == "windows" ? plan.PackageVersion : plan.Version) + "-" + platform + "-" + architecture + (platform == "windows" ? ".msix" : ".dmg");
            return plan;
        }
        public static string NativeArchitecture
        {
            get { return String.Equals(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"), "ARM64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64"; }
        }
        // Compare package versions, never Windows appVersion against an MSIX version.
        public static bool NeedsUpdate(string installed, string latest)
        {
            Version target;
            if (!TryNumericVersion(latest, out target)) throw new InvalidDataException("镜像安装版本无效，已停止自动更新。");
            if (installed == null) return true;
            Version current;
            if (!TryNumericVersion(installed, out current)) throw new InvalidDataException("无法确认本机版本，已停止自动更新。请重新检测安装。");
            return current.CompareTo(target) < 0;
        }
        private static bool TryNumericVersion(string text, out Version result)
        {
            result = null;
            if (String.IsNullOrWhiteSpace(text) || !System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+(\.\d+){0,3}$")) return false;
            var parts = text.Split('.');
            return Version.TryParse(text + String.Concat(System.Linq.Enumerable.Repeat(".0", 4-parts.Length)), out result);
        }
        internal static async Task VerifyFileAsync(string file, CodexDownloadPlan plan, CancellationToken token)
        {
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                await CopyVerifiedAsync(input, Stream.Null, plan.Length, plan.Sha256, null, token);
        }
        internal static string BuildWindowsVerificationScript(CodexDownloadPlan plan, string file)
        {
            Version version;
            if(plan==null||plan.Platform!="windows"||plan.PackageFamilyName!="OpenAI.Codex_2p2nqsd0c76g0"||
                (plan.Architecture!="arm64"&&plan.Architecture!="x64")||!TryNumericVersion(plan.PackageVersion,out version)||
                plan.Length<=0||!IsHash(plan.Sha256)||String.IsNullOrWhiteSpace(file))
                throw new InvalidDataException("缺少有效的 Windows 包身份或校验信息。");
            // Store certificates identify the app publisher, not Microsoft, in Subject.
            // Require Windows trust validation AND the exact Codex publisher identity.
            return "$ErrorActionPreference='Stop'; $file="+PowerShellLiteral(file)+"; "+
                "if((Get-Item -LiteralPath $file).Length -ne "+plan.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)+"){throw 'MSIX size mismatch'}; "+
                "if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne "+PowerShellLiteral(plan.Sha256)+"){throw 'MSIX SHA-256 mismatch'}; "+
                "$s=Get-AuthenticodeSignature -LiteralPath $file; "+
                "if($s.Status -ne 'Valid'){throw ('MSIX signature invalid: status='+$s.Status+'; '+$s.StatusMessage)}; "+
                "if($null -eq $s.SignerCertificate -or $s.SignerCertificate.Subject -cne "+PowerShellLiteral(WindowsPublisher)+"){throw 'MSIX signer publisher mismatch'}; "+
                "Add-Type -AssemblyName System.IO.Compression.FileSystem; $z=[IO.Compression.ZipFile]::OpenRead($file); "+
                "try {$e=$z.GetEntry('AppxManifest.xml'); if($null -eq $e){throw 'Missing MSIX manifest'}; "+
                "$r=New-Object IO.StreamReader($e.Open()); try{[xml]$x=$r.ReadToEnd()}finally{$r.Dispose()}; "+
                "$i=$x.Package.Identity; if($i.Name -cne 'OpenAI.Codex' -or $i.Publisher -cne "+PowerShellLiteral(WindowsPublisher)+
                " -or $i.Version -ne "+PowerShellLiteral(plan.PackageVersion)+" -or $i.ProcessorArchitecture -ne "+PowerShellLiteral(plan.Architecture)+
                "){throw 'MSIX identity mismatch'} } finally {$z.Dispose()}; "+
                "Write-Output ('MSIX verified; signature='+$s.Status+'; publisher='+$s.SignerCertificate.Subject+'; issuer='+$s.SignerCertificate.Issuer); ";
        }
        internal static string BuildWindowsInstallScript(CodexDownloadPlan plan, string file)
        {
            return BuildWindowsVerificationScript(plan,file)+
                "$cur=Get-AppxPackage -Name 'OpenAI.Codex' | Select-Object -First 1; if($cur -and [version]$cur.Version -ge [version]"+PowerShellLiteral(plan.PackageVersion)+"){throw 'Codex is already current or newer'}; Add-AppxPackage -Path "+PowerShellLiteral(file)+" -ForceUpdateFromAnyVersion; $p=Get-AppxPackage -Name 'OpenAI.Codex' | Where-Object {$_.Version.ToString() -eq "+PowerShellLiteral(plan.PackageVersion)+" -and $_.PackageFamilyName -eq "+PowerShellLiteral(plan.PackageFamilyName)+"}; if($null -eq $p){throw 'Installed package identity/version mismatch'}";
        }
        private static string PowerShellLiteral(string value){return "'"+(value??String.Empty).Replace("'","''")+"'";}
        public static async Task<string> DownloadAsync(CodexDownloadPlan plan, string directory, IProgress<int> progress, CancellationToken token)
        {
            if (plan == null || !IsHash(plan.Sha256) || plan.Url.Scheme != "https") throw new InvalidDataException("无效的下载任务。");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, Guid.NewGuid().ToString("N") + "-" + plan.FileName);
            var temp = target + ".partial";
            try
            {
                using (var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
                using (var response = await client.GetAsync(plan.Url, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage.RequestUri.Scheme != "https" ||
                        (response.RequestMessage.RequestUri.Host!="codexapp.agentsmirror.com" && response.RequestMessage.RequestUri.Host!="codexapp-r2.agentsmirror.com" && response.RequestMessage.RequestUri.Host!="codexapp-s3.agentsmirror.com"))
                        throw new InvalidDataException("拒绝社区镜像域名之外的下载重定向。");
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != plan.Length)
                        throw new InvalidDataException("安装包已变化，请重新读取下载清单。");
                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    { await CopyVerifiedAsync(input, output, plan.Length, plan.Sha256, progress, token); }
                }
                token.ThrowIfCancellationRequested();
                File.Move(temp, target);
                return target;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static async Task CopyVerifiedAsync(Stream input, Stream output, long expectedLength, string expectedHash, IProgress<int> progress, CancellationToken token)
        {
            if(expectedLength <= 0 || expectedLength > 4L*1024*1024*1024 || !IsHash(expectedHash)) throw new InvalidDataException("无效的下载校验信息。");
            using(var hash=SHA256.Create())
            {
                var buffer=new byte[81920]; long total=0; int count; int last=-1;
                while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0)
                {
                    total+=count;
                    if(total>expectedLength)throw new InvalidDataException("下载超过清单规定大小。");
                    await output.WriteAsync(buffer,0,count,token);
                    hash.TransformBlock(buffer,0,count,null,0);
                    var percent=(int)(total*100/expectedLength);
                    if(percent!=last && progress!=null){progress.Report(percent);last=percent;}
                }
                token.ThrowIfCancellationRequested();
                hash.TransformFinalBlock(new byte[0],0,0);
                var actual=BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                if(total!=expectedLength || !String.Equals(actual,expectedHash,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包大小或 SHA-256 校验失败，未保留下载文件。");
            }
        }
        private static bool IsHash(string hash) { return hash != null && System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$"); }
        private static Dictionary<string,object> Object(Dictionary<string,object> parent,string key)
        { object value; if(parent==null || !parent.TryGetValue(key,out value) || !(value is Dictionary<string,object>)) throw new InvalidDataException("下载清单缺少 " + key); return (Dictionary<string,object>)value; }
        private static string Text(Dictionary<string,object> obj,string key) { object value; return obj.TryGetValue(key,out value) ? Convert.ToString(value) : ""; }
    }
}
