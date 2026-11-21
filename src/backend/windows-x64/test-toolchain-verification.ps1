# The development toolchain's check (check-toolchain.ps1; Tatara B6b3): a valid root passes, and each malformed installation record,
# changed hash, changed kernel32 generation record or definition, and missing file fails naming its cause. Each row checks a root of
# its own: the toolchain's tools and DLLs hard-linked (read only, never written), installation.json and windows_x64 copied, so a row
# changes only its copies.
[CmdletBinding()]
param([string] $ToolchainRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$check = Join-Path $PSScriptRoot 'check-toolchain.ps1'
$work = Join-Path $repo "artifacts/verify/toolchain/$([guid]::NewGuid().ToString('N'))"
$copy = Join-Path $work 'toolchain'
New-Item -ItemType Directory -Path (Join-Path $copy 'windows_x64') -Force | Out-Null
try {
    foreach ($file in Get-ChildItem -LiteralPath $ToolchainRoot -File | Where-Object Extension -In '.exe', '.dll') {
        $null = New-Item -ItemType HardLink -Path (Join-Path $copy $file.Name) -Target $file.FullName
    }
    Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'installation.json') -Destination $copy
    foreach ($name in @('kernel32.lib', 'kernel32.def', 'kernel32.json', 'kimi_backend_windows_x64_v1.lib')) {
        Copy-Item -LiteralPath (Join-Path $ToolchainRoot "windows_x64/$name") -Destination (Join-Path $copy "windows_x64/$name")
    }
    function Test-Row([string] $Name, [string] $ErrorText = '') {
        $failure = $null
        try { & $check -ToolchainRoot $copy *> (Join-Path $work "$Name.log") }
        catch { $failure = $_.Exception.Message; $failure | Add-Content -LiteralPath (Join-Path $work "$Name.log") }
        if ($ErrorText -and ($null -eq $failure -or -not $failure.Contains($ErrorText))) { throw "Row $Name expected '$ErrorText', got: $failure" }
        if (-not $ErrorText -and $null -ne $failure) { throw "Row $Name expected success, got: $failure" }
    }
    Test-Row 'valid'
    $installationPath = Join-Path $copy 'installation.json'
    $installation = [IO.File]::ReadAllText($installationPath)
    # PLAN G983 (form J): a record that is not the JSON the check expects (a field missing or of another kind, tatara's sha256 among
    # them, a number that is not an integer, not an object) is an invalid installation before any tool; a tool's entry missing or of
    # another kind is that tool's mismatch, after its version banner.
    $rows = @(
        @('{"profile":"windows-x64-v1","tools":{},"supportingDlls":{},"tatara":{"sha256":""}}', 'Invalid toolchain installation record'),
        @('{"schemaVersion":"1","profile":"windows-x64-v1","tools":{},"supportingDlls":{},"tatara":{"sha256":""}}', 'Invalid toolchain installation record'),
        @('{"schemaVersion":3.5,"profile":"windows-x64-v1","tools":{},"supportingDlls":{},"tatara":{"sha256":""}}', 'Invalid toolchain installation record'),
        @('{"schemaVersion":1,"profile":1,"tools":{},"supportingDlls":{},"tatara":{"sha256":""}}', 'Invalid toolchain installation record'),
        @('{"schemaVersion":1,"profile":"windows-x64-v1","tools":[],"supportingDlls":{},"tatara":{"sha256":""}}', 'Invalid toolchain installation record'),
        @('{"schemaVersion":1,"profile":"windows-x64-v1","tools":{},"tatara":{"sha256":""}}', 'Invalid toolchain installation record'),
        @('{"schemaVersion":1,"profile":"windows-x64-v1","tools":{},"supportingDlls":{}}', 'Invalid toolchain installation record'),
        @('{"schemaVersion":1,"profile":"windows-x64-v1","tools":{},"supportingDlls":{},"tatara":{"sha256":7}}', 'Invalid toolchain installation record'),
        @('[]', 'Invalid toolchain installation record'),
        @('{"schemaVersion":1,"profile":"windows-x64-v1","tools":{},"supportingDlls":{},"tatara":{"sha256":""}}', 'Tool executable SHA-256 mismatch'),
        @('{"schemaVersion":1,"profile":"windows-x64-v1","tools":{"clang":7},"supportingDlls":{},"tatara":{"sha256":""}}', 'Tool executable SHA-256 mismatch'))
    for ($i = 0; $i -lt $rows.Count; $i++) {
        [IO.File]::WriteAllText($installationPath, $rows[$i][0])
        Test-Row "record-$i" $rows[$i][1]
    }
    $changed = $installation | ConvertFrom-Json
    $changed.tools.opt = '0' * 64
    $changed | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $installationPath -Encoding utf8
    Test-Row 'tool-hash' 'Tool executable SHA-256 mismatch'
    $changed = $installation | ConvertFrom-Json
    $changed.tatara.sha256 = '0' * 64
    $changed | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $installationPath -Encoding utf8
    Test-Row 'tatara-hash' 'tatara.dll SHA-256 mismatch'
    [IO.File]::WriteAllText($installationPath, $installation)
    $kernelPath = Join-Path $copy 'windows_x64/kernel32.json'
    $metadata = [IO.File]::ReadAllText($kernelPath)
    foreach ($field in @('target', 'definitionSha256', 'generatorSha256', 'sha256')) {
        $changed = $metadata | ConvertFrom-Json
        $changed.$field = 'changed'
        $changed | ConvertTo-Json | Set-Content -LiteralPath $kernelPath -Encoding utf8
        Test-Row "kernel-$field" 'kernel32 generation conditions'
    }
    [IO.File]::WriteAllText($kernelPath, $metadata)
    Add-Content -LiteralPath (Join-Path $copy 'windows_x64/kernel32.def') -Value 'Changed'
    Test-Row 'definition' 'kernel32 definition SHA-256 mismatch'
    Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'windows_x64/kernel32.def') -Destination (Join-Path $copy 'windows_x64/kernel32.def') -Force
    [IO.File]::WriteAllText((Join-Path $copy 'windows_x64/kimi_backend_windows_x64_v1.lib'), 'changed')
    Test-Row 'backend' 'Adopted backend SHA-256 mismatch'
    Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib') -Destination (Join-Path $copy 'windows_x64/kimi_backend_windows_x64_v1.lib') -Force
    # A kernel32.lib its kernel32.json agrees with, but not the catalog's kernel32.sha256 (the adopted bytes).
    $kernelLibrary = Join-Path $copy 'windows_x64/kernel32.lib'
    [IO.File]::WriteAllText($kernelLibrary, 'changed')
    $changed = $metadata | ConvertFrom-Json
    $changed.sha256 = (Get-FileHash -LiteralPath $kernelLibrary -Algorithm SHA256).Hash.ToLowerInvariant()
    $changed | ConvertTo-Json | Set-Content -LiteralPath $kernelPath -Encoding utf8
    Test-Row 'kernel-catalog' 'Adopted kernel32 SHA-256 mismatch'
    [IO.File]::WriteAllText($kernelPath, $metadata)
    Remove-Item -LiteralPath $kernelLibrary
    Test-Row 'missing-kernel' 'setup.ps1'
}
finally { Remove-Item -LiteralPath $copy -Recurse -Force }
Write-Output "Toolchain verification tests passed: $work"
