using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (var response = await GetMirrorResponseAsync(client, new Uri(ManifestUrl), 0, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192]; int count;
                        while ((count = await ReadWithDeadlineAsync(input, buffer, timeout.Token).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("下载清单过大。");
                            output.Write(buffer, 0, count);
                        }
                        timeout.Token.ThrowIfCancellationRequested();
                        return Parse(System.Text.Encoding.UTF8.GetString(output.ToArray()), platform, architecture);
                    }
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
                await CopyVerifiedAsync(input, Stream.Null, plan.Length, plan.Sha256, null, token).ConfigureAwait(false);
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
                "$family="+PowerShellLiteral(plan.PackageFamilyName)+"; $target=[version]"+PowerShellLiteral(plan.PackageVersion)+"; "+
                "function Get-CurrentCodex { Get-AppxPackage -Name 'OpenAI.Codex' | Where-Object {$_.PackageFamilyName -eq $family} | Sort-Object Version -Descending | Select-Object -First 1 }; "+
                "$cur=Get-CurrentCodex; if($cur -and [version]$cur.Version -ge $target){Write-Output 'Codex is already current or newer'; exit 0}; "+
                "$cmd=Get-Command Add-AppxPackage; $deploy=@{}; "+
                "if($cmd.Parameters.ContainsKey('LiteralPath')){$deploy['LiteralPath']=$file}else{$deploy['Path']=$file}; "+
                "Write-Output 'MSIX_DEPLOY_START'; Add-AppxPackage @deploy; "+
                "$p=Get-CurrentCodex; if($null -eq $p -or [version]$p.Version -lt $target -or $p.Status -ne 'Ok'){throw 'Windows has not registered a healthy target package'}; "+
                "Write-Output ('MSIX_DEPLOY_REGISTERED '+$p.PackageFullName)";
        }
        private static string PowerShellLiteral(string value){return "'"+(value??String.Empty).Replace("'","''")+"'";}
        internal static string CacheFileName(CodexDownloadPlan plan)
        {
            if (plan == null || !IsHash(plan.Sha256) || String.IsNullOrEmpty(plan.FileName) ||
                !System.Text.RegularExpressions.Regex.IsMatch(plan.FileName, @"^Codex-[0-9A-Za-z.\-]+\.msix$"))
                throw new InvalidDataException("无效的下载任务。");
            return plan.Sha256.ToLowerInvariant() + "-" + plan.FileName;
        }

        internal static bool IsMirrorAddress(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort &&
                String.IsNullOrEmpty(uri.UserInfo) && (uri.Host == "codexapp.agentsmirror.com" ||
                uri.Host == "codexapp-r2.agentsmirror.com" || uri.Host == "codexapp-s3.agentsmirror.com");
        }

        private static async Task<HttpResponseMessage> GetMirrorResponseAsync(HttpClient client, Uri uri, long offset, CancellationToken token)
        {
            for (var redirects = 0; redirects <= 4; redirects++)
            {
                if (!IsMirrorAddress(uri)) throw new InvalidDataException("拒绝社区镜像域名之外的下载重定向。");
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                    if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    var status = (int)response.StatusCode;
                    if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308)
                        return response;
                    var location = response.Headers.Location;
                    response.Dispose();
                    if (location == null) throw new InvalidDataException("镜像未提供重定向地址。");
                    uri = new Uri(uri, location);
                }
            }
            throw new InvalidDataException("镜像重定向次数过多。");
        }

        private static async Task<int> ReadWithDeadlineAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using (timeout.Token.Register(() => { try { stream.Dispose(); } catch (IOException) {} }))
                {
                    try
                    {
                        var count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                        return count;
                    }
                    catch (Exception)
                    {
                        if (!timeout.IsCancellationRequested) throw;
                        token.ThrowIfCancellationRequested();
                        throw new IOException("45 秒未收到下载数据。");
                    }
                }
            }
        }

        public static async Task<string> DownloadAsync(CodexDownloadPlan plan, string directory, IProgress<string> progress, CancellationToken token)
        {
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
                return await DownloadAsync(plan, directory, progress, token, client, AppLog.Write).ConfigureAwait(false);
        }

        internal static async Task<string> DownloadAsync(CodexDownloadPlan plan, string directory, IProgress<string> progress, CancellationToken token, HttpClient client, Action<string> log = null)
        {
            var target = Path.Combine(directory, CacheFileName(plan));
            if (!IsMirrorAddress(plan.Url) || plan.Length <= 0 || plan.Length > 4L * 1024 * 1024 * 1024)
                throw new InvalidDataException("无效的下载任务。");
            Directory.CreateDirectory(directory);
            var temp = target + ".partial";
            using (new FileStream(Path.Combine(directory, ".codex-download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (File.Exists(target))
                {
                    try
                    {
                        if (progress != null) progress.Report("正在复核已缓存的安装包…");
                        await VerifyFileAsync(target, plan, token).ConfigureAwait(false);
                        return target;
                    }
                    catch (InvalidDataException) { File.Delete(target); }
                }

                // v0.8.1 and earlier stored completed packages with a random prefix.
                foreach (var existing in Directory.EnumerateFiles(directory, "*-" + plan.FileName))
                {
                    var name = Path.GetFileName(existing);
                    Guid oldDownloadId;
                    if (name.Length != 33 + plan.FileName.Length || !Guid.TryParseExact(name.Substring(0, 32), "N", out oldDownloadId) ||
                        new FileInfo(existing).Length != plan.Length) continue;
                    if (progress != null) progress.Report("正在复核之前下载的同版本安装包…");
                    try { await VerifyFileAsync(existing, plan, token).ConfigureAwait(false); }
                    catch (InvalidDataException) { continue; }
                    catch (IOException) { continue; }
                    token.ThrowIfCancellationRequested();
                    File.Move(existing, target);
                    return target;
                }

                // Prefer the direct R2 mirror. Try the public mirror router on failure.
                var direct = new UriBuilder(plan.Url) { Host = "codexapp-r2.agentsmirror.com" }.Uri;
                var sources = new[] { direct, plan.Url, direct };
                var failures = new List<string>();
                foreach (var source in sources)
                {
                    token.ThrowIfCancellationRequested();
                    var length = File.Exists(temp) ? new FileInfo(temp).Length : 0;
                    if (length > plan.Length) { File.Delete(temp); length = 0; }
                    try
                    {
                        if (length != plan.Length)
                            await FetchPartialAsync(client, source, temp, length, plan.Length, progress, token).ConfigureAwait(false);
                        if (progress != null) progress.Report("正在校验下载大小和 SHA-256…");
                        try { await VerifyFileAsync(temp, plan, token).ConfigureAwait(false); }
                        catch (InvalidDataException) { File.Delete(temp); throw; }
                        token.ThrowIfCancellationRequested();
                        File.Move(temp, target);
                        return target;
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is OperationCanceledException)) throw;
                        token.ThrowIfCancellationRequested();
                        var detail = ex is OperationCanceledException ? "连接超时" :
                            ex is HttpRequestException && !(ex is MirrorHttpException) ? "网络连接失败" : ex.Message;
                        failures.Add(source.Host + "：" + detail);
                        if (log != null) log("codex.download.retry " + failures[failures.Count - 1]);
                    }
                }
                throw new IOException("镜像下载未完成，重新检查可继续下载。 " + String.Join(" / ", failures));
            }
        }

        private sealed class MirrorHttpException : HttpRequestException
        {
            internal MirrorHttpException(int status) : base("HTTP " + status) {}
        }

        internal static long GetDownloadOffset(HttpResponseMessage response, long offset, long total)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != total)
                    throw new InvalidDataException("镜像包大小与选定版本不同。");
                return 0;
            }
            if (response.StatusCode != HttpStatusCode.PartialContent) throw new MirrorHttpException((int)response.StatusCode);
            var range = response.Content.Headers.ContentRange;
            if (offset <= 0 || range == null || range.Unit != "bytes" || range.From != offset ||
                range.To != total - 1 || range.Length != total ||
                (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != total - offset))
                throw new InvalidDataException("镜像返回的续传位置或文件大小不匹配。");
            return offset;
        }

        private static async Task FetchPartialAsync(HttpClient client, Uri url, string file, long offset, long total, IProgress<string> progress, CancellationToken token)
        {
            HttpResponseMessage response;
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(25));
                if (progress != null) progress.Report(offset > 0 ? "正在连接镜像并准备续传…" : "正在连接镜像…");
                response = await GetMirrorResponseAsync(client, url, offset, connect.Token).ConfigureAwait(false);
            }
            using (response)
            {
                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                    throw new MirrorHttpException((int)response.StatusCode);
                if (response.Content == null) throw new InvalidDataException("镜像未返回安装包内容。");
                if (response.Content.Headers.ContentEncoding.Count > 0 &&
                    !System.Linq.Enumerable.All(response.Content.Headers.ContentEncoding, value => value == "identity"))
                    throw new InvalidDataException("镜像返回了未经请求的内容编码。");
                var start = GetDownloadOffset(response, offset, total);
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(file, start == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 262144, true))
                {
                    var buffer = new byte[262144];
                    var position = start;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    long lastUpdate = -1000;
                    int count;
                    while ((count = await ReadWithDeadlineAsync(input, buffer, token).ConfigureAwait(false)) > 0)
                    {
                        if (count > total - position) throw new InvalidDataException("镜像返回的数据超过安装包大小。");
                        await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                        position += count;
                        if (watch.ElapsedMilliseconds - lastUpdate < 1000) continue;
                        lastUpdate = watch.ElapsedMilliseconds;
                        var rate = (position - start) / Math.Max(0.1, watch.Elapsed.TotalSeconds) / 1048576;
                        if (progress != null)
                            progress.Report(String.Format("正在下载 {0}%（{1:F0}/{2:F0} MiB，{3:F2} MiB/s）",
                                position * 100 / total, position / 1048576.0, total / 1048576.0, rate));
                    }
                    if (position != total) throw new IOException("镜像提前断开连接，已保留下载进度。");
                }
            }
        }
        internal static async Task CopyVerifiedAsync(Stream input, Stream output, long expectedLength, string expectedHash, IProgress<int> progress, CancellationToken token)
        {
            if(expectedLength <= 0 || expectedLength > 4L*1024*1024*1024 || !IsHash(expectedHash)) throw new InvalidDataException("无效的下载校验信息。");
            using(var hash=SHA256.Create())
            {
                var buffer=new byte[81920]; long total=0; int count; int last=-1;
                while((count=await input.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false))>0)
                {
                    total+=count;
                    if(total>expectedLength)throw new InvalidDataException("下载超过清单规定大小。");
                    await output.WriteAsync(buffer,0,count,token).ConfigureAwait(false);
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
