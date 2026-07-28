[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$outputDirectory = Join-Path $projectRoot 'obj\shutdown-tests'
$fixture = Join-Path $outputDirectory 'ProcessFixture.exe'
$tests = Join-Path $outputDirectory 'ShutdownTests.exe'

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 `
    "/out:$fixture" `
    "/reference:$(Join-Path $frameworkRoot 'System.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.Drawing.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.Windows.Forms.dll')" `
    (Join-Path $projectRoot 'tests\ProcessFixture.cs')
if ($LASTEXITCODE -ne 0) { throw "Fixture compilation failed: $LASTEXITCODE" }

& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /nowarn:0649 `
    "/out:$tests" `
    "/main:CodexZhLauncher.ShutdownTests" `
    "/reference:$(Join-Path $frameworkRoot 'System.dll')" `
    "/reference:$(Join-Path $frameworkRoot 'System.Core.dll')" `
    (Join-Path $projectRoot 'src\Models.cs') `
    (Join-Path $projectRoot 'src\AppLog.cs') `
    (Join-Path $projectRoot 'src\CodexProcessManager.cs') `
    (Join-Path $projectRoot 'tests\ShutdownTests.cs')
if ($LASTEXITCODE -ne 0) { throw "Shutdown test compilation failed: $LASTEXITCODE" }

& $tests $fixture
if ($LASTEXITCODE -ne 0) { throw "Shutdown tests failed: $LASTEXITCODE" }
