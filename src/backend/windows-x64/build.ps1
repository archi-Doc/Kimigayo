[CmdletBinding()]
param(
    [string] $ToolchainRoot = '',
    [string] $LlvmBin = '',
    [switch] $AllowUnpinnedToolchain
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'artifact-paths.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$profile = Read-KimiWindowsProfile
$expectedVersion = $profile.llvmVersion
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
$outDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../artifacts/backend/windows-x64'))
$workDir = Join-Path $PSScriptRoot ('../../../temp/backend-build/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$reportPath = Join-Path $outDir 'verification.json'
# Invalidate the previous run before invoking any compiler or executable.
@{ status = 'incomplete'; adopted = $false } | ConvertTo-Json | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $reportPath -Encoding utf8
$inputFiles = @($PSCommandPath, (Join-Path $PSScriptRoot 'toolchain.ps1'), (Join-Path $PSScriptRoot 'artifact-paths.ps1'), (Join-Path $PSScriptRoot 'kernel32.ps1'), (Join-Path $PSScriptRoot 'kernel32.def'), (Join-Path $PSScriptRoot 'profile.json'), (Join-Path $PSScriptRoot '../../../Directory.Build.props')) + @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'), (Join-Path $PSScriptRoot 'tests') -File | ForEach-Object { $_.FullName })
$sourceIdentities = [ordered]@{}
foreach ($file in ($inputFiles | Sort-Object -CaseSensitive)) {
    $relative = [IO.Path]::GetRelativePath($PSScriptRoot, $file)
    $sourceIdentities[$relative] = Get-KimiSha256 $file
}
function Tool([string] $name) {
    return [IO.Path]::GetFullPath((Join-Path $LlvmBin "$name.exe"), (Get-Location).ProviderPath)
}
function Run([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}
$toolNames = @('clang', 'llvm-lib', 'llvm-readobj', 'llvm-objdump', 'llvm-nm', 'opt', 'llc', 'lld-link')
$tools = @{}
$versions = @{}
$toolIdentities = @{}
$matched = $true
foreach ($name in $toolNames) {
    $tools[$name] = Tool $name
    # llvm-lib uses lib.exe's /? interface; its binary identity is recorded below.
    if ($name -ne 'llvm-lib') {
        $identity = Get-KimiLlvmToolIdentity $tools[$name] $expectedVersion -AllowUnpinnedToolchain:$AllowUnpinnedToolchain
        $toolIdentities[$name] = $identity
        $versions[$name] = $identity.version
        $matched = $matched -and $identity.versionMatched
    }
    else {
        $toolIdentities[$name] = @{ path = $tools[$name]; sha256 = Get-KimiSha256 $tools[$name] }
    }
}
@{ status = 'incomplete'; adopted = $false; llvmVersion = $expectedVersion; reportedVersionsMatched = $matched; unverifiedToolchain = -not $matched; tools = $toolIdentities } | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $reportPath -Encoding utf8
$tools['llvm-dlltool'] = Tool 'llvm-dlltool'
$toolIdentities['llvm-dlltool'] = Get-KimiDlltoolIdentity $tools['llvm-dlltool'] -AllowUnpinnedToolchain:$AllowUnpinnedToolchain
$unverified = -not $matched -or -not $toolIdentities['llvm-dlltool'].hashMatched
@{ status = 'incomplete'; adopted = $false; llvmVersion = $expectedVersion; reportedVersionsMatched = $matched; unverifiedToolchain = $unverified; tools = $toolIdentities } | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $reportPath -Encoding utf8
# The kernel32 candidate is kept beside the backend's (with its definition), for the report and an LLVM update's adoption.
$kernel = New-KimiKernel32Library $tools (Join-Path $outDir ([IO.Path]::GetFileName((Get-KimiKernel32))))
$kernelPath = $kernel.path
$objects = @()
foreach ($name in @('memcmp', 'memcpy', 'memmove', 'memset', 'chkstk')) {
    $obj = Join-Path $workDir "$name.obj"
    Run $tools.clang @('--target=x86_64-pc-windows-msvc', '-c', (Join-Path $PSScriptRoot "src/$name.S"), '-o', $obj)
    $objects += $obj
    $inspection = & $tools['llvm-readobj'] --file-headers --symbols --unwind --coff-directives $obj | Out-String
    if ($LASTEXITCODE -ne 0 -or $inspection -notmatch 'COFF-x86-64' -or $inspection -notmatch 'RuntimeFunction') { throw "Invalid COFF/unwind: $name" }
    if ($inspection -match '(?i)DEFAULTLIB|\.CRT\$|\.tls') { throw "Unexpected startup dependency: $name" }
    $inspection | ConvertTo-KimiArtifactText | Set-Content -LiteralPath (Join-Path $outDir "$name.inspection.txt") -Encoding utf8
    $undefined = & $tools['llvm-nm'] --undefined-only $obj | Out-String
    if ($LASTEXITCODE -ne 0 -or $undefined.Trim()) { throw "Unexpected undefined symbols: $name $undefined" }
    $disassembly = & $tools['llvm-objdump'] -d $obj | Out-String
    if ($LASTEXITCODE -ne 0 -or $disassembly -match '\bcall[q]?\s') { throw "Unexpected helper call: $name" }
    $disassembly | ConvertTo-KimiArtifactText | Set-Content -LiteralPath (Join-Path $outDir "$name.disassembly.txt") -Encoding utf8
}
$archive = Join-Path $outDir ([IO.Path]::GetFileName((Get-KimiBackendArchive)))
$librarian = (Resolve-Path -LiteralPath $tools['llvm-lib']).Path
Push-Location -LiteralPath $workDir
try {
    # COFF archive member names must not include the builder's checkout path.
    Run $librarian (@('/nologo', "/out:$archive") + @($objects | ForEach-Object { [IO.Path]::GetFileName($_) }))
}
finally { Pop-Location }
$members = @(& $librarian /list $archive)
if ($LASTEXITCODE -ne 0 -or (($members | Sort-Object) -join ',') -cne 'chkstk.obj,memcmp.obj,memcpy.obj,memmove.obj,memset.obj') {
    throw 'Archive members must use filenames without build directory paths'
}
$defined = & $tools['llvm-nm'] --defined-only --extern-only --format=posix $archive | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect archive exports' }
$symbols = @([regex]::Matches($defined, '(?m)^(\S+) T ') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -CaseSensitive)
if (($symbols -join ',') -cne '__chkstk,memcmp,memcpy,memmove,memset' -or $defined -match '_fltused') { throw "Wrong archive exports: $defined" }

$probe = Join-Path $workDir 'probe.obj'
Run $tools.clang @('--target=x86_64-pc-windows-msvc', '-c', (Join-Path $PSScriptRoot 'tests/probe.S'), '-o', $probe)
# The helper suites and their own inputs: native.c the archive's (__chkstk, with probe.S), memory.c the memory functions'.
foreach ($suite in @(@{ name = 'native'; inputs = @($probe) }, @{ name = 'memory'; inputs = @() })) {
    $name = $suite.name
    $inputIr = Join-Path $workDir "$name.ll"
    Run $tools.clang @('--target=x86_64-pc-windows-msvc', '-S', '-emit-llvm', '-O0', '-Xclang', '-disable-O0-optnone', '-ffreestanding', '-fno-builtin', '-fno-stack-protector', '-fno-lto', '-fasynchronous-unwind-tables', '-march=x86-64', '-mno-avx', (Join-Path $PSScriptRoot "tests/$name.c"), '-o', $inputIr)
    Protect-KimiLlvmPaths $inputIr
    foreach ($level in @('O0', 'O2')) {
        $ir = $inputIr
        Run $tools.opt @('-passes=verify', '-disable-output', $ir)
        if ($level -eq 'O2') {
            $ir = Join-Path $workDir "$name.O2.ll"
            Run $tools.opt @('-S', '-passes=default<O2>', $inputIr, '-o', $ir)
            Protect-KimiLlvmPaths $ir
            Run $tools.opt @('-passes=verify', '-disable-output', $ir)
        }
        $obj = Join-Path $workDir "$name.$level.obj"
        Invoke-KimiLlvmOutput $tools.llc @("-$level", '-filetype=obj', '-mtriple=x86_64-pc-windows-msvc', '-mcpu=x86-64', '-mattr=+sse2', '-relocation-model=pic', '-code-model=small', $ir) $obj
        $exe = Join-Path $workDir "$name.$level.exe"
        Run $tools['lld-link'] (@($obj) + $suite.inputs + @($archive, $kernelPath, '/entry:test_entry', '/subsystem:console', '/nodefaultlib', '/Brepro', "/out:$exe"))
        # Bound a broken helper test; never leave a hung executable behind.
        $process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); throw "$level $name test timed out" }
        if ($process.ExitCode -ne 0) { throw "$level $name tests failed: $($process.ExitCode)" }
    }
}
foreach ($file in $inputFiles) {
    $relative = [IO.Path]::GetRelativePath($PSScriptRoot, $file)
    $actual = Get-KimiSha256 $file
    if ($actual -cne $sourceIdentities[$relative]) { throw "Source changed during verification: $relative" }
}
$archiveHash = Get-KimiSha256 $archive
@{
    status = 'tested-candidate'; adopted = $false; profile = 'windows-x64-v1'; llvmVersion = $expectedVersion; reportedVersionsMatched = $matched; unverifiedToolchain = $unverified; unversionedTools = @('llvm-lib', 'llvm-dlltool')
    packageId = 'kimi-backend-windows-x64'; abiVersion = $profile.abiVersion; packageVersion = $profile.packageVersion
    library = 'kimi_backend'; artifactSha256 = $archiveHash
    providedSymbols = $symbols; tests = @('O0', 'O2'); tools = $toolIdentities; versions = $versions; sources = $sourceIdentities
    kernel32 = $kernel
} | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "Native tests passed. Candidate report: $reportPath (not an adopted compiler catalog)."
# The candidates are checked, never installed: the compiler carries the adopted libraries (Tatara B6a), and setup requires the
# candidates to reproduce them.
if ($unverified -or $archiveHash -cne $profile.artifactSha256 -or $kernel.sha256 -cne $profile.kernel32.sha256) {
    Write-Warning 'The candidates in artifacts/backend/windows-x64 are not the adopted backend and kernel32 (profile.json), or unpinned tools built them.'
}
else { Write-Output 'The candidates reproduce the adopted backend and kernel32 (profile.json).' }
