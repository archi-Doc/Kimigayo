[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [string] $ToolchainRoot = '',
    [string] $ResultRoot = 'bin/module-verification'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = Join-Path $repo "Kimi/bin/$Configuration/net10.0/Kimi.dll"
$work = Join-Path ([IO.Path]::GetFullPath($ResultRoot)) ([guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$checks = [Collections.Generic.List[object]]::new()
function Invoke-Kimi([string] $Name, [string[]] $Arguments, [int] $ExpectedExit = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['KIMI_TOOLCHAIN_ROOT'] = $ToolchainRoot
    $start.ArgumentList.Add($compiler)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { throw "Timed out: $Name" }
        $output = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $work "$Name.stdout"), $output)
        [IO.File]::WriteAllText((Join-Path $work "$Name.stderr"), $errorText)
        $checks.Add(@{ name = $Name; exit = $process.ExitCode; expectedExit = $ExpectedExit; arguments = $Arguments })
        if ($process.ExitCode -ne $ExpectedExit) { throw "Unexpected exit for ${Name}: $($process.ExitCode)`n$output`n$errorText" }
        return $output
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
$library = @'
public group Api
    public func run() => Child.Api.output(keep("module"))
    public func keep<T>(value?: T) -> T => value
    #Test
    func dependencyTest() => Missing.mustNotBeIncluded()
'@
$child = @'
public group Api
    public func output(text?: string) => Console.writeLine(text)
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
        if ($record.status -cne 'linked' -or -not $record.reportedVersionsMatched -or $record.unverifiedToolchain) { throw 'Unverified module native build' }
        $output = Invoke-Kimi "$level-run" @('run', $project)
        if (-not $output.EndsWith("module`nmodule`n", [StringComparison]::Ordinal)) { throw "Project module output mismatch: $output" }
        $output = Invoke-Kimi "$level-executable" @('run', (Join-Path $work "App/bin/x86_64-pc-windows-msvc/App.$level.exe"))
        if ($output -cne "module`nmodule`n") { throw "Module output mismatch: $output" }
        $output = Invoke-Kimi "$level-tests" @('test', $project, '--format', 'json')
        $tests = $output | ConvertFrom-Json
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
