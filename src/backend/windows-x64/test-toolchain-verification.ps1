[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
$work = Join-Path $repo "artifacts/verify/toolchain/$([guid]::NewGuid().ToString('N'))"
$copy = Join-Path $work 'installation'
New-Item -ItemType Directory -Path (Join-Path $copy 'windows_x64') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'installation.json') -Destination $copy
foreach ($name in @('kernel32.lib', 'kernel32.def', 'kernel32.json', 'kimi_backend_windows_x64_v1.lib')) {
    Copy-Item -LiteralPath (Join-Path $ToolchainRoot "windows_x64/$name") -Destination (Join-Path $copy "windows_x64/$name")
}
function Verify([string] $Name, [string] $ErrorText = '') {
    $report = Join-Path $work "$Name.json"
    & dotnet $compiler toolchain verify --ToolchainRoot $copy --LlvmBin $ToolchainRoot --Report $report *> (Join-Path $work "$Name.log")
    $expectedExit = if ($ErrorText) { 1 } else { 0 }
    if ($LASTEXITCODE -ne $expectedExit) { throw "Unexpected verify exit: $Name. See $work" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($ErrorText) {
        if ($result.toolchainVerification -cne 'failed' -or -not $result.unverifiedToolchain -or -not $result.error.Contains($ErrorText)) { throw "Missing failure evidence: $Name" }
    }
    elseif ($result.status -cne 'passed' -or -not $result.reportedVersionsMatched -or $result.unverifiedToolchain) { throw "Missing success evidence: $Name" }
}
Verify 'valid'
$installationPath = Join-Path $copy 'installation.json'
$installation = [IO.File]::ReadAllText($installationPath)
$changed = $installation | ConvertFrom-Json
$changed.tools.opt = '0' * 64
$changed | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $installationPath -Encoding utf8
Verify 'tool-hash' 'Tool executable SHA-256 mismatch'
[IO.File]::WriteAllText($installationPath, $installation)
$kernelPath = Join-Path $copy 'windows_x64/kernel32.json'
$metadata = [IO.File]::ReadAllText($kernelPath)
foreach ($field in @('target', 'definitionSha256', 'generatorSha256', 'sha256')) {
    $changed = $metadata | ConvertFrom-Json
    $changed.$field = 'changed'
    $changed | ConvertTo-Json | Set-Content -LiteralPath $kernelPath -Encoding utf8
    Verify "kernel-$field" 'kernel32 generation conditions'
}
[IO.File]::WriteAllText($kernelPath, $metadata)
Add-Content -LiteralPath (Join-Path $copy 'windows_x64/kernel32.def') -Value 'Changed'
Verify 'definition' 'kernel32 definition SHA-256 mismatch'
Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'windows_x64/kernel32.def') -Destination (Join-Path $copy 'windows_x64/kernel32.def') -Force
[IO.File]::WriteAllText((Join-Path $copy 'windows_x64/kimi_backend_windows_x64_v1.lib'), 'changed')
Verify 'backend' 'Adopted backend SHA-256 mismatch'
Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib') -Destination (Join-Path $copy 'windows_x64/kimi_backend_windows_x64_v1.lib') -Force
Remove-Item -LiteralPath (Join-Path $copy 'windows_x64/kernel32.lib')
Verify 'missing-kernel' 'setup.ps1'
Write-Output "Toolchain verification tests passed: $work"
