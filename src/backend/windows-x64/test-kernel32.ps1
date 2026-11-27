[CmdletBinding()]
param([string] $ToolchainRoot = '', [string] $LlvmBin = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
. (Join-Path $PSScriptRoot 'kernel32.ps1')
. (Join-Path $PSScriptRoot 'expected-output.ps1')
function Expect-Failure([scriptblock] $Action, [string] $Message) {
    try { & $Action | Out-Null } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        return
    }
    throw "Expected failure: $Message"
}
$tools = @{}
foreach ($name in @('llvm-dlltool','llvm-readobj')) { $tools[$name] = (Resolve-Path -LiteralPath (Join-Path $LlvmBin "$name.exe")).Path }
$identity = Get-KimiDlltoolIdentity $tools['llvm-dlltool']
if (-not $identity.hashMatched) { throw 'Pinned dlltool required' }
$definition = Get-KimiKernel32Definition
$root = Join-Path $PSScriptRoot ('../../../temp/kernel32-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$hashes = @()
foreach ($name in @('first checkout', 'different checkout')) {
    $directory = Join-Path $root $name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $generated = New-KimiKernel32Library $tools (Join-Path $directory 'kernel32.lib')
    $hashes += $generated.sha256
    $bytes = [IO.File]::ReadAllBytes($generated.path)
    foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
        if ($encoding.GetString($bytes).Contains($root, [StringComparison]::OrdinalIgnoreCase) -or
            $encoding.GetString($bytes).Contains([Environment]::GetFolderPath('UserProfile'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Private path in import library' }
    }
}
if ($hashes[0] -cne $hashes[1]) { throw 'Different build directories changed the import library' }
$output = Join-Path $root 'previous.lib'
[IO.File]::WriteAllText($output, 'previous output')
$broken = @{ 'llvm-dlltool' = $tools['llvm-readobj']; 'llvm-readobj' = $tools['llvm-readobj'] }
Expect-Failure { New-KimiKernel32Library $broken $output 2>$null } 'generation failed'
if (-not (Test-KimiExpectedOutput ([Text.Encoding]::UTF8.GetBytes('previous output')) ([IO.File]::ReadAllBytes($output))) -or @(Get-ChildItem -LiteralPath $root -Directory -Filter 'previous.lib-*').Count) { throw 'Failed generation damaged output or left staging files' }
Expect-Failure { Get-KimiDlltoolIdentity $tools['llvm-readobj'] } 'SHA-256 mismatch'
$warnings = @()
$unverified = Get-KimiDlltoolIdentity $tools['llvm-readobj'] -AllowUnpinnedToolchain -WarningVariable warnings -WarningAction SilentlyContinue
if ($unverified.hashMatched -or $warnings.Count -ne 1) { throw 'Missing exploratory warning/identity' }
Expect-Failure { Get-KimiDlltoolIdentity (Join-Path $root 'missing.exe') } 'not found'

# The inspection's rejections (Test-KimiKernel32Library, its one owner; C-444 N5): the adopted library passes, and each library crafted
# from the shared definition differing in one property fails for it. No library has members that llvm-readobj prints without a Format
# line, so the row for that rule reads the adopted library through llvm-readobj with its Format lines dropped.
$crafted = Join-Path $root 'crafted'
New-Item -ItemType Directory -Path $crafted | Out-Null
function New-CraftedLibrary([string] $Name, [string] $Machine, [string[]] $Lines) {
    $path = Join-Path $crafted "$Name.def"
    [IO.File]::WriteAllText($path, ($Lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    & $tools['llvm-dlltool'] -m $Machine -d $path -l (Join-Path $crafted "$Name.lib")
    if ($LASTEXITCODE -ne 0) { throw "Crafted library $Name was not generated" }
    return Join-Path $crafted "$Name.lib"
}
Test-KimiKernel32Library $tools['llvm-dlltool'] $tools['llvm-readobj'] (Get-KimiKernel32)
$lines = $definition.TrimEnd("`n").Split("`n")
$x64 = 'i386:x86-64'
$rows = [ordered]@{
    'DLL name' = New-CraftedLibrary 'other-dll' $x64 (@('LIBRARY OTHER.dll') + $lines[1..($lines.Count - 1)])
    'unexpected architecture' = New-CraftedLibrary 'arm64' 'arm64' $lines
    'unexpected import symbol set' = @(
        (New-CraftedLibrary 'missing' $x64 $lines[0..($lines.Count - 2)]),
        (New-CraftedLibrary 'extra' $x64 ($lines + '    KimiExtraImport')),
        (New-CraftedLibrary 'duplicate' $x64 ($lines + $lines[-1])))
}
foreach ($row in $rows.GetEnumerator()) {
    foreach ($library in $row.Value) { Expect-Failure { Test-KimiKernel32Library $tools['llvm-dlltool'] $tools['llvm-readobj'] $library } $row.Key }
}
$noFormat = Join-Path $crafted 'readobj-without-format.ps1'
"& '$($tools['llvm-readobj'])' @args | Where-Object { `$_ -notmatch '^Format:' }" | Set-Content -LiteralPath $noFormat -Encoding utf8
Expect-Failure { Test-KimiKernel32Library $tools['llvm-dlltool'] $noFormat (Get-KimiKernel32) } 'unexpected architecture'
Write-Output 'Kernel32 tests passed: generation, inspection and its rejections, path independence, privacy, failed generation and tool policy.'
