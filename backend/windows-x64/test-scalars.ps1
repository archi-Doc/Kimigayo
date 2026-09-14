[CmdletBinding()]
param(
    [string] $ToolchainRoot = '', [string] $LlvmBin = '',
    [string] $FixturePattern = '*.ll'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtures = Join-Path $repo 'bin/scalar-fixtures'
$out = Join-Path $repo 'bin/scalar-native'
New-Item -ItemType Directory -Force $out | Out-Null
$tools = @{}
foreach ($name in @('opt', 'llc', 'lld-link', 'llvm-dlltool', 'llvm-readobj', 'llvm-nm')) { $tools[$name] = Join-Path $LlvmBin "$name.exe" }
$profile = Read-KimiWindowsProfile
foreach ($name in @('opt', 'llc', 'lld-link', 'llvm-readobj', 'llvm-nm')) { $null = Get-KimiLlvmToolIdentity $tools[$name] $profile.llvmVersion }
$null = Get-KimiDlltoolIdentity $tools['llvm-dlltool']
$kernel = New-KimiKernel32Library $tools (Join-Path $out 'kernel32.lib')
$archive = Join-Path $ToolchainRoot 'windows_x64/kimi_backend_windows_x64_v1.lib'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $profile.artifactSha256) { throw 'Installed backend SHA-256 mismatch' }
$allowedSymbols = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($symbol in $profile.providedSymbols) { $null = $allowedSymbols.Add($symbol) }
foreach ($line in ((Get-KimiKernel32Definition) -split "`n" | Select-Object -Skip 2)) {
    $symbol = $line.Trim()
    if ($symbol) { $null = $allowedSymbols.Add($symbol); $null = $allowedSymbols.Add("__imp_$symbol") }
}
function Invoke-Tool([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE)" }
}
$runs = 0
foreach ($fixture in Get-ChildItem -LiteralPath $fixtures -Filter $FixturePattern) {
    $stem = [IO.Path]::Combine($fixtures, $fixture.BaseName)
    $expected = [IO.File]::ReadAllText("$stem.stdout")
    $exit = [int][IO.File]::ReadAllText("$stem.exit")
    $expectedError = [IO.File]::ReadAllText("$stem.stderr")
    $timeout = if (Test-Path -LiteralPath "$stem.timeout") { [int][IO.File]::ReadAllText("$stem.timeout") } else { 0 }
    foreach ($level in @('O0', 'O2')) {
        $target = Join-Path $out ($fixture.BaseName + '.' + $level)
        $ir = $fixture.FullName
        Invoke-Tool $tools.opt @('-passes=verify', '-disable-output', $ir)
        if ($level -eq 'O2') {
            Invoke-Tool $tools.opt @('-S', '-passes=default<O2>', $ir, '-o', "$target.ll")
            $ir = "$target.ll"
            Invoke-Tool $tools.opt @('-passes=verify', '-disable-output', $ir)
        }
        Invoke-Tool $tools.llc @("-$level", '-filetype=obj', '-mtriple=x86_64-pc-windows-msvc', '-mcpu=x86-64', '-mattr=+sse2', '-relocation-model=pic', '-code-model=small', $ir, '-o', "$target.obj")
        $undefined = @(& $tools['llvm-nm'] --undefined-only --format=posix "$target.obj")
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect scalar object dependencies' }
        $undefined | Set-Content -LiteralPath "$target.undefined.txt" -Encoding utf8
        foreach ($line in $undefined) {
            $symbol = ($line -split '\s+', 2)[0]
            if (-not $allowedSymbols.Contains($symbol)) { throw "Unexpected dependency in ${target}: $symbol" }
        }
        Invoke-Tool $tools['lld-link'] @("$target.obj", $archive, $kernel.path, '/entry:__kimi_start', '/subsystem:console', '/nodefaultlib', '/Brepro', "/out:$target.exe")
        $start = [Diagnostics.ProcessStartInfo]::new("$target.exe")
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if ($timeout -gt 0) {
                if ($process.WaitForExit($timeout)) { throw "Divergent fixture returned: $target, exit=$($process.ExitCode)" }
                $process.Kill($true)
                $process.WaitForExit()
            }
            elseif (-not $process.WaitForExit(30000)) { throw "Timed out: $target" }
            $actual = $stdout.GetAwaiter().GetResult()
            $errorText = $stderr.GetAwaiter().GetResult()
            if (($timeout -eq 0 -and $process.ExitCode -ne $exit) -or $actual -cne $expected -or $errorText -cne $expectedError) {
                throw "Failed: $target, exit=$($process.ExitCode), stdout=$actual, stderr=$errorText"
            }
        }
        finally {
            if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }
        $runs++
    }
}
if ($runs -eq 0) { throw 'No scalar fixtures; run ScalarEmissionTest first.' }
Write-Output "Passed $runs native scalar executions (O0/O2)."
