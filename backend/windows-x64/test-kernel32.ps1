[CmdletBinding()]
param([Parameter(Mandatory)] [string] $LlvmBin)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
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
$root = Join-Path $PSScriptRoot ('bin/kernel32-tests/' + [guid]::NewGuid().ToString('N'))
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
if ([IO.File]::ReadAllText($output) -cne 'previous output' -or @(Get-ChildItem -LiteralPath $root -Directory -Filter 'previous.lib-*').Count) { throw 'Failed generation damaged output or left staging files' }
Expect-Failure { Get-KimiDlltoolIdentity $tools['llvm-readobj'] } 'SHA-256 mismatch'
$warnings = @()
$unverified = Get-KimiDlltoolIdentity $tools['llvm-readobj'] -AllowUnpinnedToolchain -WarningVariable warnings -WarningAction SilentlyContinue
if ($unverified.hashMatched -or $warnings.Count -ne 1) { throw 'Missing exploratory warning/identity' }
Expect-Failure { Get-KimiDlltoolIdentity (Join-Path $root 'missing.exe') } 'not found'
$entry = [pscustomobject]@{ name='kernel32'; kind='import'; generator='llvm-dlltool'; dll='KERNEL32.dll'; definitionSha256=(Read-KimiWindowsProfile).kernel32.definitionSha256 }
Assert-KimiKernel32Manifest $entry
$entry.definitionSha256 = '0' * 64
Expect-Failure { Assert-KimiKernel32Manifest $entry } 'Invalid generated kernel32 identity'
Write-Output 'Kernel32 tests passed: generation, inspection, path independence, privacy, failed generation, tool policy and manifest integrity.'
