using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Collections.Generic;
namespace CodexZhLauncher {
internal static class WorkspaceTests {
    private static int count;
    private static string generatedInstallScript;
    private static void Check(bool condition, string name) { if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name); }
    private static void Reject(Action action,string name) {try{action();}catch(Exception){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    public static int Main(string[] args) {try{Run();
        if(args.Length==5&&args[0]=="--write-verifier") {
            var package=args[2];string sha;
            using(var input=File.OpenRead(package))using(var algorithm=SHA256.Create())sha=BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-","");
            var verificationPlan=new CodexDownloadPlan{Platform="windows",Architecture=args[4],PackageVersion=args[3],PackageFamilyName="OpenAI.Codex_2p2nqsd0c76g0",Length=new FileInfo(package).Length,Sha256=sha};
            File.WriteAllText(args[1],CodexDownloads.BuildWindowsVerificationScript(verificationPlan,package),new UTF8Encoding(true));return 0;
        }
        if(args.Length>0) {
            var live=File.ReadAllText(args[0]);
            foreach(var platform in new[]{"windows","macos"})foreach(var arch in new[]{"x64","arm64"}){
                var p=CodexDownloads.Parse(live,platform,arch);
                Check(p.Length>0,"live_manifest_"+platform+"_"+arch);Console.WriteLine(p.Version+" / "+p.PackageVersion+" / "+p.Sha256);
            }
        }var scriptPath=Environment.GetEnvironmentVariable("CODEX_ZH_TEST_SCRIPT");if(!String.IsNullOrWhiteSpace(scriptPath)&&!String.IsNullOrWhiteSpace(generatedInstallScript))File.WriteAllText(scriptPath,generatedInstallScript,new UTF8Encoding(false));Console.WriteLine("workspace_tests="+count+" passed");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    private static void Run() {
        var serializer=new JavaScriptSerializer();
        var hash=Enumerable.Range(0,32).Select(x=>(byte)x).ToArray();
        var win=new Dictionary<string,object>{{"downloadable",true},{"appVersion","26.1.2"},{"version","26.1.9.0"},{"contentLength",3},{"catalog",new Dictionary<string,object>{{"hashAlgorithm","SHA256"},{"hash",Convert.ToBase64String(hash)},{"packageFamilyName","OpenAI.Codex_2p2nqsd0c76g0"}}}};
        var archive="Codex-darwin-arm64-26.3.4.zip";
        var mac=new Dictionary<string,object>{{"downloadable",true},{"currentForCodexVersion",true},{"bundleShortVersion","26.3.4"},{"bundleVersion","123"},{"contentLength",3},{"sha256",new string('a',64)},{"url","https://persistent.oaistatic.com/Codex.dmg"},{"sparkleArchiveFileName",archive},{"sparkleArchiveBundleShortVersion","26.3.4"},{"bundleIdentifier","com.openai.codex"},{"teamIdentifier","2DC432GLL2"},{"appcast",new Dictionary<string,object>{{"enclosureLength",3L}}}};
        var root=new {codexVersion="WRONG",derived=new {latestChecksums=new Dictionary<string,string>{{archive,new string('b',64)}}},sources=new {windows=new {architectures=new {x64=win,arm64=win}},macos=new {x64=mac,arm64=mac}}};
        Func<string> json=()=>serializer.Serialize(root);
        var plan=CodexDownloads.Parse(json(),"windows","x64");
        Check(plan.Version=="26.1.2" && plan.PackageVersion=="26.1.9.0","platform_version_not_top_level");
        Check(plan.Sha256==BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant(),"base64_sha256");
        Check(CodexDownloads.Parse(json(),"windows","arm64").Url.AbsoluteUri.EndsWith("win-arm64"),"windows_arm64");
        Check(!CodexDownloads.NeedsUpdate("26.1111.2228.0","26.1111.2228.0"),"windows_same_package_is_current");
        Check(!CodexDownloads.NeedsUpdate("26.1111.3000.0","26.1111.2228.0"),"windows_newer_package_never_downgraded");
        Check(CodexDownloads.NeedsUpdate("26.1111.1000.0","26.1111.2228.0"),"windows_older_package_needs_update");
        Check(CodexDownloads.NeedsUpdate(null,"26.1111.2228.0"),"windows_missing_package_needs_install");
        Reject(()=>CodexDownloads.NeedsUpdate("-","26.1111.2228.0"),"windows_unknown_local_version_rejected");
        Check(CodexDownloads.Parse(json(),"windows","x64").FileName.Contains("26.1.9.0"),"windows_filename_uses_package_version");
        var installScript=CodexDownloads.BuildWindowsInstallScript(plan,"C:\\Downloads\\Codex.msix");
        generatedInstallScript=installScript;
        Check(installScript.Contains("OpenAI.Codex")&&installScript.Contains("ProcessorArchitecture")&&installScript.Contains("Add-AppxPackage"),"windows_install_identity_validation");
        Check(installScript.Contains("ForceUpdateFromAnyVersion")&&installScript.Contains("already current or newer"),"windows_update_never_downgrades");
        Check(installScript.Contains("Get-AuthenticodeSignature")&&installScript.IndexOf("Get-AuthenticodeSignature")<installScript.IndexOf("Add-AppxPackage"),"windows_deployment_rechecks_signature");
        Reject(()=>CodexDownloads.BuildWindowsVerificationScript(new CodexDownloadPlan{Platform="windows",Architecture="x86"},"test.msix"),"invalid_verification_plan");
        var macPlan=CodexDownloads.Parse(json(),"macos","arm64");
        Check(macPlan.Version=="26.3.4","mac_platform_version");
        Check(macPlan.Url.Host=="codexapp.agentsmirror.com"&&macPlan.Url.AbsolutePath.Contains("mac-arm64"),"mac_community_mirror_only");
        Check(macPlan.Sha256==new string('a',64),"mac_dmg_hash_parsed");
        Reject(()=>CodexDownloads.Parse(json(),"linux","x64"),"unsupported_platform");
        Reject(()=>CodexDownloads.Parse(json(),"windows","x86"),"unsupported_architecture");
        win["downloadable"]=false;Reject(()=>CodexDownloads.Parse(json(),"windows","x64"),"unavailable");win["downloadable"]=true;
        win["appVersion"]="../bad";Reject(()=>CodexDownloads.Parse(json(),"windows","x64"),"invalid_filename_version");win["appVersion"]="26.1.2";
        win["contentLength"]=-1;Reject(()=>CodexDownloads.Parse(json(),"windows","x64"),"invalid_length");win["contentLength"]=3;
        var catalog=(Dictionary<string,object>)win["catalog"];catalog["hash"]="YQ==";Reject(()=>CodexDownloads.Parse(json(),"windows","x64"),"short_hash");catalog["hash"]=Convert.ToBase64String(hash);
        mac["url"]="https://example.org/Codex.dmg";Reject(()=>CodexDownloads.Parse(json(),"macos","arm64"),"wrong_official_host");
        mac["url"]="http://persistent.oaistatic.com/Codex.dmg";Reject(()=>CodexDownloads.Parse(json(),"macos","arm64"),"http_official_url");
        Reject(()=>CodexDownloads.Parse("{}","windows","x64"),"missing_manifest_fields");
        var bytes=Encoding.UTF8.GetBytes("test package");string expected;
        using(var sha=SHA256.Create())expected=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        using(var input=new MemoryStream(bytes))using(var output=new MemoryStream()) {
            CodexDownloads.CopyVerifiedAsync(input,output,bytes.Length,expected,null,CancellationToken.None).GetAwaiter().GetResult();
            Check(output.ToArray().SequenceEqual(bytes),"stream_copy_and_checksum");
        }
        Reject(()=>CodexDownloads.CopyVerifiedAsync(new MemoryStream(bytes),new MemoryStream(),bytes.Length,new string('0',64),null,CancellationToken.None).GetAwaiter().GetResult(),"hash_mismatch");
        Reject(()=>CodexDownloads.CopyVerifiedAsync(new MemoryStream(bytes),new MemoryStream(),bytes.Length-1,expected,null,CancellationToken.None).GetAwaiter().GetResult(),"oversize_stream");
        Reject(()=>CodexDownloads.CopyVerifiedAsync(new MemoryStream(bytes),new MemoryStream(),bytes.Length+1,expected,null,CancellationToken.None).GetAwaiter().GetResult(),"truncated_stream");
        var cancel=new CancellationTokenSource();cancel.Cancel();
        Reject(()=>CodexDownloads.CopyVerifiedAsync(new MemoryStream(bytes),new MemoryStream(),bytes.Length,expected,null,cancel.Token).GetAwaiter().GetResult(),"cancel_stream");
        var normalized=WorkspaceSettings.Normalize(new WorkspaceSettings{ToolTheme="bad",CodexTheme="bad",FontSize=80});
        Check(normalized.ToolTheme=="violet"&&normalized.CodexTheme=="default"&&normalized.FontSize==18,"normalize_settings");
        Check(WorkspaceSettings.Normalize(new WorkspaceSettings{FontSize=0}).FontSize==12,"font_minimum");
        var folder=Path.Combine(Path.GetTempPath(),"CodexWorkspaceTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try {
            var file=Path.Combine(folder,"settings.json");normalized.Save(file);
            normalized.ToolTheme="forest";normalized.AutoCheck=false;normalized.Save(file);
            var reloaded=WorkspaceSettings.Load(file);
            Check(reloaded.ToolTheme=="forest"&&!reloaded.AutoCheck,"atomic_settings_replace");
            File.WriteAllText(file,"{bad");Check(WorkspaceSettings.Load(file).ToolTheme=="violet","corrupt_settings_fallback");
            Check(Directory.GetFiles(folder,"*.tmp").Length==0,"settings_no_partial_files");
            Reject(()=>normalized.Save(folder),"settings_write_failure");
        }finally{Directory.Delete(folder,true);}
        foreach(var preset in ThemePreset.All)Check(!ThemePreset.BuildScript(preset.Id,14).Contains("__THEME_JSON__"),"theme_resource_"+preset.Id);
        Reject(()=>ThemePreset.BuildScript("bad",14),"invalid_theme_id");
        Reject(()=>ThemePreset.BuildScript("violet",30),"invalid_theme_font");
    }
}}
