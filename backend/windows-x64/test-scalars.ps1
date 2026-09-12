[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmBin
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtures = Join-Path $repo 'bin/scalar-fixtures'
$out = Join-Path $repo 'bin/scalar-native'
New-Item -ItemType Directory -Force $out | Out-Null
$tools = @{}
foreach ($name in @('opt', 'llc', 'lld-link', 'llvm-dlltool', 'llvm-readobj')) { $tools[$name] = Join-Path $LlvmBin "$name.exe" }
$profile = Read-KimiWindowsProfile
foreach ($name in @('opt', 'llc', 'lld-link', 'llvm-readobj')) { $null = Get-KimiLlvmToolIdentity $tools[$name] $profile.llvmVersion }
$null = Get-KimiDlltoolIdentity $tools['llvm-dlltool']
$kernel = New-KimiKernel32Library $tools (Join-Path $out 'kernel32.lib')
$archive = Join-Path $PSScriptRoot 'bin/kimi_backend_windows_x64_v1.lib'
function Invoke-Tool([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE)" }
}
$runs = 0
foreach ($fixture in Get-ChildItem -LiteralPath $fixtures -Filter '*.ll') {
    $stem = [IO.Path]::Combine($fixtures, $fixture.BaseName)
    $expected = [IO.File]::ReadAllText("$stem.stdout")
    $exit = [int][IO.File]::ReadAllText("$stem.exit")
    $expectedError = [IO.File]::ReadAllText("$stem.stderr")
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
        Invoke-Tool $tools['lld-link'] @("$target.obj", $archive, $kernel.path, '/entry:__kimi_start', '/subsystem:console', '/nodefaultlib', '/Brepro', "/out:$target.exe")
        $start = [Diagnostics.ProcessStartInfo]::new("$target.exe")
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw "Timed out: $target" }
        $actual = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne $exit -or $actual -cne $expected -or $errorText -cne $expectedError) {
            throw "Failed: $target, exit=$($process.ExitCode), stdout=$actual, stderr=$errorText"
        }
        $process.Dispose()
        $runs++
    }
}
if ($runs -eq 0) { throw 'No scalar fixtures; run ScalarEmissionTest first.' }
Write-Output "Passed $runs native scalar executions (O0/O2)."
