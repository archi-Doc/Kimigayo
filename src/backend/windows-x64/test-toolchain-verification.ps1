# The development toolchain's check (check-toolchain.ps1; Tatara B6b3): a valid root passes, and each malformed installation record,
# changed hash and missing file fails naming its cause. Each row checks a root of its own: the toolchain's tools and DLLs hard-linked
# (read only, never written) and installation.json copied, so a row changes only its copies. A root's windows_x64 is not read (B6b6).
[CmdletBinding()]
param([string] $ToolchainRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$check = Join-Path $PSScriptRoot 'check-toolchain.ps1'
$work = Join-Path $repo "artifacts/verify/toolchain/$([guid]::NewGuid().ToString('N'))"
$copy = Join-Path $work 'toolchain'
New-Item -ItemType Directory -Path $copy -Force | Out-Null
try {
    foreach ($file in Get-ChildItem -LiteralPath $ToolchainRoot -File | Where-Object Extension -In '.exe', '.dll') {
        $null = New-Item -ItemType HardLink -Path (Join-Path $copy $file.Name) -Target $file.FullName
    }
    Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'installation.json') -Destination $copy
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
    # A missing tool is a missing file, whose remedy is setup.
    Remove-Item -LiteralPath (Join-Path $copy 'llvm-objdump.exe')
    Test-Row 'missing-tool' 'setup.ps1'
}
finally { Remove-Item -LiteralPath $copy -Recurse -Force }
Write-Output "Toolchain verification tests passed: $work"
