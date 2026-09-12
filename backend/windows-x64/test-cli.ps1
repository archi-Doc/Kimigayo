[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmBin,
    [string] $MismatchedLlvmBin = '',
    [string] $NativeCompiler = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = Join-Path $repo "Kimi/bin/$Configuration/net10.0/Kimi.dll"
$work = Join-Path $repo ('bin/cli-tests/' + [guid]::NewGuid().ToString('N') + '/project with spaces')
New-Item -ItemType Directory -Path $work -Force | Out-Null
$project = Join-Path $work 'Hello.kimiproj'
$source = Join-Path $work 'Hello.kimi'
$archive = Join-Path $PSScriptRoot 'bin/kimi_backend_windows_x64_v1.lib'
$report = Join-Path $work 'verification.json'
@{ status = 'incomplete' } | ConvertTo-Json | Set-Content -LiteralPath $report
function Write-Project([string] $Level, [string] $Bin) {
    $binPath = $Bin.Replace('\', '/')
    $archivePath = [IO.Path]::GetFullPath($archive).Replace('\', '/')
    @"
Targets=
  "x86_64-pc-windows-msvc"
OutputKind="Application"
Optimization="$Level"
LlvmBin="$binPath"
NativeLibraries=
  x86_64-pc-windows-msvc=
    kimi_backend=
      Kind="static"
      Input="$archivePath"
"@ | Set-Content -LiteralPath $project -Encoding utf8
}
function Invoke-Kimi([string[]] $Arguments, [int] $ExitCode = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new($(if ($NativeCompiler) { [IO.Path]::GetFullPath($NativeCompiler) } else { (Get-Command dotnet).Source }))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if (-not $NativeCompiler) { $start.ArgumentList.Add($compiler) }
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw 'CLI timed out' }
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    $actual = $process.ExitCode
    $process.Dispose()
    if ($actual -ne $ExitCode) { throw "CLI exit $actual, expected $ExitCode`: $($Arguments -join ' ')`n$output" }
    return $output
}

Write-Project 'O2' 'missing LLVM directory'
'::Core.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
$ir = Join-Path $work 'bin/x86_64-pc-windows-msvc/Hello.ll'
$recordPath = [IO.Path]::ChangeExtension($ir, '.link.build.json')
$output = Invoke-Kimi @('emit-llvm', $project)
if (-not (Test-Path $ir) -or (Test-Path $recordPath)) { throw 'emit-llvm must only publish LLVM inputs' }
$output = Invoke-Kimi @('run', $project) 1
$output = Invoke-Kimi @('build', $project) 1
foreach ($level in @('O0', 'O2')) {
    Write-Project $level 'missing LLVM directory'
    $output = Invoke-Kimi @('build', $project, '--LlvmBin', $LlvmBin)
    if ($output -match '(?m)^Hello, world!') { throw 'Build executed the Application' }
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.status -cne 'linked' -or -not $record.reportedVersionsMatched -or $record.unverifiedToolchain) { throw 'Invalid successful build record' }
    if ($record.kernel32.generator -cne 'llvm-dlltool' -or -not $record.tools.'llvm-dlltool'.hashMatched -or
        $record.kernel32.sha256 -cne (Get-FileHash (Join-Path (Split-Path $ir) "Hello.$level.kernel32.lib")).Hash.ToLowerInvariant()) { throw 'Missing generated import identity' }
    $exe = Join-Path (Split-Path $ir) "Hello.$level.exe"
    $hash = (Get-FileHash $exe).Hash
    $before = (Get-Item $recordPath).LastWriteTimeUtc
    $output = Invoke-Kimi @('run', $project)
    if (-not $output.Contains("Hello, world!")) { throw 'Run did not forward stdout' }
    if ((Get-Item $recordPath).LastWriteTimeUtc -ne $before) { throw 'Run rewrote build metadata' }
    $output = Invoke-Kimi @('run', $exe)
    if ($output.Trim() -cne 'Hello, world!') { throw 'Direct binary execution stdout mismatch' }
    'let broken =' | Set-Content -LiteralPath $source
    $output = Invoke-Kimi @('run', $project)
    if (-not $output.Contains('Hello, world!')) { throw 'Run compiled modified sources' }
    $output = Invoke-Kimi @('build', $project, '--LlvmBin', $LlvmBin) 1
    $output = Invoke-Kimi @('run', $project) 1
    if ((Get-FileHash $exe).Hash -cne $hash) { throw 'Failed build overwrote the last executable' }
    '::Core.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
}
if ($MismatchedLlvmBin) {
    $output = Invoke-Kimi @('build', $project, '--LlvmBin', $MismatchedLlvmBin) 1
    if (-not $output.Contains('LLVM version mismatch')) { throw 'Missing mismatch diagnostic' }
    $output = Invoke-Kimi @('build', $project, '--LlvmBin', $MismatchedLlvmBin, '--AllowUnpinnedToolchain', 'true')
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if (-not $record.unverifiedToolchain -or -not $output.Contains('unverified toolchain')) { throw 'Exploratory mode must warn and record actual versions' }
}
# A separate tiny executable verifies that run forwards the child's nonzero exit code.
# Missing/hash-mismatched dlltool must invalidate success and must never reuse a stale import library.
$incompleteTools = Join-Path $work 'incomplete tools'
New-Item -ItemType Directory -Path $incompleteTools | Out-Null
foreach ($name in @('opt','llc','lld-link','llvm-nm','llvm-readobj')) {
    Copy-Item -LiteralPath (Join-Path $LlvmBin "$name.exe") -Destination (Join-Path $incompleteTools "$name.exe")
}
$output = Invoke-Kimi @('build', $project, '--LlvmBin', $incompleteTools) 1
$output = Invoke-Kimi @('run', $project) 1
Copy-Item -LiteralPath (Join-Path $LlvmBin 'llvm-readobj.exe') -Destination (Join-Path $incompleteTools 'llvm-dlltool.exe')
$output = Invoke-Kimi @('build', $project, '--LlvmBin', $incompleteTools) 1
if (-not $output.Contains('SHA-256 mismatch')) { throw "Missing dlltool identity diagnostic: $output" }
$output = Invoke-Kimi @('build', $project, '--LlvmBin', $incompleteTools, '--AllowUnpinnedToolchain', 'true') 1
$output = Invoke-Kimi @('run', $project) 1
$output = Invoke-Kimi @('build', $project, '--LlvmBin', $LlvmBin)
foreach ($name in @('opt','llc','lld-link','llvm-nm','llvm-readobj','llvm-dlltool')) { Remove-Item -LiteralPath (Join-Path $incompleteTools "$name.exe") -Force }
Remove-Item -LiteralPath $incompleteTools -Force
$exitIr = Join-Path $work 'exit.ll'
@'
target triple = "x86_64-pc-windows-msvc"
declare dllimport void @ExitProcess(i32) noreturn
define void @entry() noreturn {
  call void @ExitProcess(i32 37)
  unreachable
}
'@ | Set-Content -LiteralPath $exitIr -Encoding utf8
& (Join-Path $LlvmBin 'llc.exe') -filetype=obj $exitIr -o "$exitIr.obj"
if ($LASTEXITCODE -ne 0) { throw 'Exit fixture object generation failed' }
$kernel32 = Join-Path (Split-Path $ir) 'Hello.O2.kernel32.lib'
& (Join-Path $LlvmBin 'lld-link.exe') "$exitIr.obj" $kernel32 /entry:entry /subsystem:console /nodefaultlib "/out:$exitIr.exe"
if ($LASTEXITCODE -ne 0) { throw 'Exit fixture link failed' }
$output = Invoke-Kimi @('run', "$exitIr.exe") 37
$output = Invoke-Kimi @('run', (Join-Path $work 'missing.exe')) 1
$output = Invoke-Kimi @('build', (Join-Path $work 'missing.kimiproj')) 1
$output = Invoke-Kimi @('emit-llvm', (Join-Path $work 'missing.kimisln')) 1
$output = Invoke-Kimi @('build', $project, '--LlvmBin') 1
@{ status = 'passed'; configuration = $Configuration; scenarios = @('emit without LLVM', 'O0/O2 native build', 'run without compilation', 'failure invalidates old success', 'toolchain policy', 'spaces in paths', 'exit code forwarding', 'missing inputs') } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $report
Write-Output "CLI integration tests passed: $report"
