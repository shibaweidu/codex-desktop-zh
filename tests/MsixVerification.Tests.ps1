param([Parameter(Mandatory=$true)][string]$TestExecutable)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$testDirectory=Join-Path ([IO.Path]::GetTempPath()) ('CodexMsixVerification-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
$package=Join-Path $testDirectory "Codex publisher's package.msix"
$verifier=Join-Path $testDirectory 'verify.ps1'
$fixtureTrust=[PSCustomObject]@{Status='Valid';Subject='CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B';NullCertificate=$false}
# Only the OS trust result is injected. The generated production verifier checks
# real fixture bytes, hashes, ZIP manifests and the exact signer identity.
function Get-AuthenticodeSignature {
    param($LiteralPath)
    $certificate=if($fixtureTrust.NullCertificate){$null}else{
        [PSCustomObject]@{Subject=$fixtureTrust.Subject;Issuer='CN=Microsoft Marketplace CA G 026, O=Microsoft Corporation'}
    }
    [PSCustomObject]@{Status=$fixtureTrust.Status;StatusMessage='Fixture trust result';SignerCertificate=$certificate}
}
function New-Package {
    param([string]$Name='OpenAI.Codex',[string]$Publisher='CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B',[string]$Version='26.1.9.0',[string]$Architecture='x64')
    if(Test-Path -LiteralPath $package){Remove-Item -LiteralPath $package}
    $archive=[IO.Compression.ZipFile]::Open($package,[IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry=$archive.CreateEntry('AppxManifest.xml')
        $writer=New-Object IO.StreamWriter($entry.Open())
        try {$writer.Write("<Package><Identity Name='$Name' Publisher='$Publisher' Version='$Version' ProcessorArchitecture='$Architecture'/></Package>")}finally{$writer.Dispose()}
    }finally{$archive.Dispose()}
    $null=& $TestExecutable '--write-verifier' $verifier $package '26.1.9.0' 'x64'
    if($LASTEXITCODE -ne 0){throw 'Cannot generate MSIX verifier'}
}
function Expect-Rejection {
    param([string]$Expected,[string]$Name)
    $rejected=$false
    try { & $verifier | Out-Null } catch {
        if($_.Exception.Message -notlike "*$Expected*"){throw}
        $rejected=$true
    }
    if(-not $rejected){throw "Expected rejection: $Name"}
    Write-Host "PASS $Name"
}
try {
    New-Package
    $output=& $verifier
    if($output -notmatch 'MSIX verified'){throw 'Valid Codex Store publisher was rejected'}
    Write-Host 'PASS valid_store_signature_without_microsoft_in_subject'
    foreach($status in @('NotSigned','HashMismatch','NotTrusted','UnknownError')){
        $fixtureTrust.Status=$status
        Expect-Rejection "status=$status" "reject_signature_$status"
    }
    $fixtureTrust.Status='Valid'
    $fixtureTrust.Subject='CN=Microsoft Corporation'
    Expect-Rejection 'signer publisher mismatch' 'reject_other_microsoft_signer'
    $fixtureTrust.Subject='CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B'
    $fixtureTrust.NullCertificate=$true
    Expect-Rejection 'signer publisher mismatch' 'reject_missing_signer'
    $fixtureTrust.NullCertificate=$false
    New-Package -Name 'Other.App'; Expect-Rejection 'identity mismatch' 'reject_other_package'
    New-Package -Publisher 'CN=Other'; Expect-Rejection 'identity mismatch' 'reject_manifest_publisher'
    New-Package -Version '26.1.8.0'; Expect-Rejection 'identity mismatch' 'reject_wrong_version'
    New-Package -Architecture 'arm64'; Expect-Rejection 'identity mismatch' 'reject_wrong_architecture'
    New-Package
    $bytes=[IO.File]::ReadAllBytes($package);$bytes[0]=$bytes[0] -bxor 1;[IO.File]::WriteAllBytes($package,$bytes)
    Expect-Rejection 'SHA-256 mismatch' 'reject_changed_cached_bytes'
    [IO.File]::WriteAllBytes($package,[byte[]]@(1,2,3))
    Expect-Rejection 'size mismatch' 'reject_changed_cached_size'
}finally{
    # Explicitly scoped disposable fixtures only; no real app is installed or closed.
    Remove-Item -LiteralPath $testDirectory -Recurse -Force
}
