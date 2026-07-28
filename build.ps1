[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sourceDir = Join-Path $projectRoot 'src'
$sharedDir = Join-Path $projectRoot 'shared'
$manifestFile = Join-Path $sourceDir 'app.manifest'
$outputDir = Join-Path $projectRoot 'dist'
$intermediateDir = Join-Path $projectRoot 'obj'
$outputFile = Join-Path $outputDir 'Codex-Zh-Launcher-Windows-x64.exe'
$iconFile = Join-Path $intermediateDir 'AppIcon.ico'
$iconGenerator = Join-Path $projectRoot 'scripts\generate-windows-icon.ps1'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "System C# compiler not found: $compiler"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path $intermediateDir | Out-Null

if (-not (Test-Path -LiteralPath $iconGenerator)) {
    throw "Missing Windows icon generator: $iconGenerator"
}
& $iconGenerator -OutputPath $iconFile

$references = @(
    (Join-Path $frameworkRoot 'WPF\PresentationFramework.dll'),
    (Join-Path $frameworkRoot 'WPF\PresentationCore.dll'),
    (Join-Path $frameworkRoot 'WPF\WindowsBase.dll'),
    (Join-Path $frameworkRoot 'System.dll'),
    (Join-Path $frameworkRoot 'System.Core.dll'),
    (Join-Path $frameworkRoot 'System.Net.Http.dll'),
    (Join-Path $frameworkRoot 'System.Web.Extensions.dll'),
    (Join-Path $frameworkRoot 'System.Xml.dll'),
    (Join-Path $frameworkRoot 'System.Xml.Linq.dll'),
    (Join-Path $frameworkRoot 'System.Xaml.dll'),
    (Join-Path $frameworkRoot 'System.Windows.Forms.dll')
)

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "Missing compiler reference: $reference"
    }
}

$sharedResources = @(
    @{ Path = (Join-Path $sharedDir 'locale-script.js'); Name = 'CodexZhLauncher.Shared.locale-script.js' },
    @{ Path = (Join-Path $sharedDir 'menu-script.js'); Name = 'CodexZhLauncher.Shared.menu-script.js' },
    @{ Path = (Join-Path $sharedDir 'menu-translations.json'); Name = 'CodexZhLauncher.Shared.menu-translations.json' }
)
foreach ($resource in $sharedResources) {
    if (-not (Test-Path -LiteralPath $resource.Path)) {
        throw "Missing shared resource: $($resource.Path)"
    }
}

$sourceFiles = Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName
if (-not $sourceFiles) {
    throw 'No C# source files found.'
}

$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/utf8output',
    '/codepage:65001',
    "/win32manifest:$manifestFile",
    "/win32icon:$iconFile",
    "/out:$outputFile"
)

if ($Configuration -eq 'Release') {
    $compilerArgs += '/optimize+'
    $compilerArgs += '/debug-'
} else {
    $compilerArgs += '/optimize-'
    $compilerArgs += '/debug:full'
}

foreach ($reference in $references) {
    $compilerArgs += "/reference:$reference"
}
foreach ($resource in $sharedResources) {
    $compilerArgs += "/resource:$($resource.Path),$($resource.Name)"
}
$compilerArgs += "/resource:$iconFile,CodexZhLauncher.AppIcon.ico"
$compilerArgs += $sourceFiles

& $compiler $compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed. csc exit code: $LASTEXITCODE"
}

$hash = Get-FileHash -LiteralPath $outputFile -Algorithm SHA256
$checksumFile = Join-Path $outputDir 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText(
    $checksumFile,
    "$($hash.Hash)  $([System.IO.Path]::GetFileName($outputFile))`r`n",
    [System.Text.Encoding]::ASCII
)
Write-Host "Build completed: $outputFile"
Write-Host "SHA-256: $($hash.Hash)"
