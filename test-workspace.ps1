[CmdletBinding()]
param([string]$LiveManifest)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $root 'obj\workspace-tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$compilerArgs = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/nowarn:0649',"/out:$out\WorkspaceTests.exe",'/main:CodexZhLauncher.WorkspaceTests')
foreach($name in @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll')) {
    $compilerArgs += "/reference:$(Join-Path $framework $name)"
}
$compilerArgs += "/resource:$root\shared\theme-script.js,CodexZhLauncher.Shared.theme-script.js"
foreach($file in @('src\CodexDownloads.cs','src\WorkspaceSettings.cs','src\AppLog.cs','tests\WorkspaceTests.cs')) { $compilerArgs += (Join-Path $root $file) }
& (Join-Path $framework 'csc.exe') $compilerArgs
if($LASTEXITCODE -ne 0) { throw 'Workspace test compilation failed.' }
$scriptPath=Join-Path $out 'CodexInstallScript.ps1'
$env:CODEX_ZH_TEST_SCRIPT=$scriptPath
if($LiveManifest) { & "$out\WorkspaceTests.exe" $LiveManifest }
else { & "$out\WorkspaceTests.exe" }
if($LASTEXITCODE -ne 0) { throw 'Workspace tests failed.' }
$tokens=$null;$parseErrors=$null
[System.Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$parseErrors) | Out-Null
if($parseErrors.Count -gt 0) { throw ('Generated AppX install script has syntax errors: '+(($parseErrors | ForEach-Object { $_.Message }) -join '; ')) }
Write-Host 'PASS generated_windows_install_script_powershell_syntax'
Remove-Item Env:\CODEX_ZH_TEST_SCRIPT -ErrorAction SilentlyContinue
