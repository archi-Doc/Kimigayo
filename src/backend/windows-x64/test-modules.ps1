[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [string] $ResultRoot = (Join-Path $PSScriptRoot '../../../artifacts/verify/module-verification')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'expected-output.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
$work = Join-Path ([IO.Path]::GetFullPath($ResultRoot)) ([guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$checks = [Collections.Generic.List[object]]::new()
$utf8 = [Text.UTF8Encoding]::new($false)
# Returns the captured stdout bytes; both streams are kept byte for byte.
function Invoke-Kimi([string] $Name, [string[]] $Arguments, [int] $ExpectedExit = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($compiler)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = [IO.MemoryStream]::new()
        $stderr = [IO.MemoryStream]::new()
        $copies = @($process.StandardOutput.BaseStream.CopyToAsync($stdout), $process.StandardError.BaseStream.CopyToAsync($stderr))
        if (-not $process.WaitForExit(60000)) { throw "Timed out: $Name" }
        foreach ($copy in $copies) { $null = $copy.GetAwaiter().GetResult() }
        $output = $stdout.ToArray()
        $errorOutput = $stderr.ToArray()
        [IO.File]::WriteAllBytes((Join-Path $work "$Name.stdout"), $output)
        [IO.File]::WriteAllBytes((Join-Path $work "$Name.stderr"), $errorOutput)
        $checks.Add(@{ name = $Name; exit = $process.ExitCode; expectedExit = $ExpectedExit; arguments = $Arguments })
        if ($process.ExitCode -ne $ExpectedExit) {
            throw "Unexpected exit for ${Name}: $($process.ExitCode)`n$($utf8.GetString($output))`n$($utf8.GetString($errorOutput))"
        }
        return ,$output
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
$library = @'
public group Api
    public func run() => Child.Api.output(keep("module"))
    public func keep<T>(value: T) -> T => value@move
    #Test
    func dependencyTest() => Missing.mustNotBeIncluded()
'@
$child = @'
public group Api
    public func output(text: string) => Console.writeLine(text)
'@
$source = @'
public func main()
    Lib.Api.run()
    Again.Api.run()
#Test
func usesDependency()
    $expect(Lib.Api.keep(42) == 42)
'@
$status = 'FAIL'
try {
    foreach ($name in @('App', 'Library', 'Child')) { $null = New-Item -ItemType Directory -Path (Join-Path $work $name) }
    $project = Join-Path $work 'App/App.kimiproj'
    $librarySource = Join-Path $work 'Library/main.kimi'
    $library | Set-Content -LiteralPath $librarySource -Encoding utf8
    $child | Set-Content -LiteralPath (Join-Path $work 'Child/main.kimi') -Encoding utf8
    $source | Set-Content -LiteralPath (Join-Path $work 'App/main.kimi') -Encoding utf8
    'OutputKind="Library" PackageId="child" PackageVersion="1" Targets={"x86_64-pc-windows-msvc"}' | Set-Content -LiteralPath (Join-Path $work 'Child/Child.kimiproj') -Encoding utf8
    'OutputKind="Library" PackageId="library" PackageVersion="1" Targets={"x86_64-pc-windows-msvc"} Dependencies={Child={PackageId="child" PackageVersion="1" Project="../Child/Child.kimiproj"}}' | Set-Content -LiteralPath (Join-Path $work 'Library/Library.kimiproj') -Encoding utf8
    foreach ($level in @('O0', 'O2')) {
        @"
OutputKind="Application" Targets={"x86_64-pc-windows-msvc"} Optimization="$level"
Dependencies={Lib={PackageId="library" PackageVersion="1" Project="../Library/Library.kimiproj"} Again={PackageId="library" PackageVersion="1" Project="../Library/Library.kimiproj"}}
"@ | Set-Content -LiteralPath $project -Encoding utf8
        $null = Invoke-Kimi "$level-restore" @('restore', $project)
        $null = Invoke-Kimi "$level-build" @('build', $project)
        $recordPath = Join-Path $work 'App/bin/x86_64-pc-windows-msvc/App.link.build.json'
        $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
        if ($record.status -cne 'linked') { throw 'Unlinked module native build' }
        $expected = $utf8.GetBytes("module`nmodule`n")
        # A project run may print build lines first, so its stdout ends with the program's.
        $output = Invoke-Kimi "$level-run" @('run', $project, '--no-build')
        if (-not (Test-KimiExpectedOutput $expected ([Linq.Enumerable]::ToArray([Linq.Enumerable]::TakeLast($output, $expected.Length))))) { throw "Project module output mismatch: $($utf8.GetString($output))" }
        $output = Invoke-Kimi "$level-executable" @('run', (Join-Path $work "App/bin/x86_64-pc-windows-msvc/App.$level.exe"))
        if (-not (Test-KimiExpectedOutput $expected $output)) { throw "Module output mismatch: $($utf8.GetString($output))" }
        $output = Invoke-Kimi "$level-tests" @('test', $project, '--format', 'json')
        $tests = $utf8.GetString($output) | ConvertFrom-Json
        if ($tests.summary.selected -ne 1 -or $tests.summary.passed -ne 1) { throw 'Dependency tests leaked or the root test did not pass' }
    }
    ($library + "`nfunc unused() => missing()") | Set-Content -LiteralPath $librarySource -Encoding utf8
    $null = Invoke-Kimi 'invalid-restore' @('restore', $project)
    $null = Invoke-Kimi 'invalid-build' @('build', $project) 1
    $null = Invoke-Kimi 'invalid-run' @('run', $project) 1
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.status -cne 'incomplete') { throw 'Failed dependency build retained a success record' }
    $library | Set-Content -LiteralPath $librarySource -Encoding utf8
    $status = 'PASS'
}
finally {
    @{ status = $status; configuration = $Configuration; compilerSha256 = (Get-FileHash -LiteralPath $compiler).Hash; checks = $checks } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $work 'result.json') -Encoding utf8
}
Write-Output "Passed $($checks.Count) source-module CLI checks: $work"
