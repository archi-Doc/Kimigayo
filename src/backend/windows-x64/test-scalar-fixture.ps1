# One fixture owns its outputs and log; all workers read an immutable toolchain context.
[CmdletBinding()]
param([IO.FileInfo] $Fixture, [hashtable] $Context)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-scalar-process.ps1')
$tools = $Context.tools
$fixtures = $Context.fixtures
$out = $Context.output
$kernel = $Context.kernel
$archive = $Context.archive
$allowedSymbols = [Collections.Generic.HashSet[string]]::new([string[]]$Context.allowedSymbols, [StringComparer]::Ordinal)
$log = Join-Path $Context.logs ($Fixture.BaseName + '.log')
$completed = [Collections.Generic.List[string]]::new()
$timer = [Diagnostics.Stopwatch]::StartNew()
$ok = $false
function Invoke-Tool([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE)" }
}
try {
    & {
        $stem = [IO.Path]::Combine($fixtures, $fixture.BaseName)
        $expected = [IO.File]::ReadAllText("$stem.stdout")
        $exit = [int][IO.File]::ReadAllText("$stem.exit")
        $expectedError = [IO.File]::ReadAllText("$stem.stderr")
        $timeout = if (Test-Path -LiteralPath "$stem.timeout") { [int][IO.File]::ReadAllText("$stem.timeout") } else { 0 }
        Invoke-Tool $tools.opt @('-passes=verify', '-disable-output', $fixture.FullName)
        foreach ($level in @('O0', 'O2')) {
            $target = Join-Path $out ($fixture.BaseName + '.' + $level)
            $ir = $fixture.FullName
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
            Invoke-KimiFixtureProcess $target @{ stdout = $expected; stderr = $expectedError; exit = $exit; timeout = $timeout }
            $completed.Add($level)
        }
    } *> $log
    $ok = $true
}
catch { $_ | Out-String | Add-Content -LiteralPath $log }
[pscustomobject]@{ fixture = $Fixture.Name; ok = $ok; levels = @($completed); seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3); log = $log }
