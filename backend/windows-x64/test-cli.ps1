[CmdletBinding()]
param(
    [string] $ToolchainRoot = '', [string] $LlvmBin = '',
    [string] $MismatchedLlvmBin = '',
    [string] $NativeCompiler = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$compiler = Join-Path $repo "Kimi/bin/$Configuration/net10.0/Kimi.dll"
$work = Join-Path $repo ('bin/cli-tests/' + [guid]::NewGuid().ToString('N') + '/project with spaces')
New-Item -ItemType Directory -Path $work -Force | Out-Null
$project = Join-Path $work 'Hello.kimiproj'
$source = Join-Path $work 'Hello.kimi'
$report = Join-Path $work 'verification.json'
@{ status = 'incomplete' } | ConvertTo-Json | Set-Content -LiteralPath $report
function Write-Project([string] $Level, [string] $Bin) {
    $binSetting = if ($Bin) { 'LlvmBin="' + $Bin.Replace('\', '/') + '"' } else { '' }
    @"
Targets=
  "x86_64-pc-windows-msvc"
OutputKind="Application"
Optimization="$Level"
$binSetting
"@ | Set-Content -LiteralPath $project -Encoding utf8
}
function Invoke-Kimi([string[]] $Arguments, [int] $ExitCode = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new($(if ($NativeCompiler) { [IO.Path]::GetFullPath($NativeCompiler) } else { (Get-Command dotnet).Source }))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['KIMI_TOOLCHAIN_ROOT'] = $ToolchainRoot
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

Write-Project 'O2' ''
'::Kimi.Console.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
$output = Invoke-Kimi @('build', $project)
$output = Invoke-Kimi @('run', $project)
if (-not $output.Contains('Hello, world!')) { throw "Automatic toolchain resolution failed: $output" }
$output = Invoke-Kimi @('build', $project, '--ToolchainRoot', (Join-Path $work 'missing toolchain')) 1
if (-not $output.Contains('Cannot obtain LLVM version')) { throw 'Explicit toolchain root must override the environment' }
$missingRoot = Join-Path $work 'missing backend'
$output = Invoke-Kimi @('build', $project, '--ToolchainRoot', $missingRoot, '--LlvmBin', $LlvmBin) 1
if (-not $output.Contains('Backend archive not found')) { throw 'Missing backend must not fall back to the installed library' }
$badDirectory = Join-Path $missingRoot 'windows_x64'
New-Item -ItemType Directory -Path $badDirectory -Force | Out-Null
$badArchive = Join-Path $badDirectory 'kimi_backend_windows_x64_v1.lib'
'not the adopted backend' | Set-Content -LiteralPath $badArchive
$output = Invoke-Kimi @('build', $project, '--ToolchainRoot', $missingRoot, '--LlvmBin', $LlvmBin) 1
if (-not $output.Contains('backend SHA-256 mismatch')) { throw 'A substituted automatic backend must fail its hash check' }
Remove-Item -LiteralPath $badArchive -Force
Remove-Item -LiteralPath $badDirectory -Force
Remove-Item -LiteralPath $missingRoot -Force
$output = Invoke-Kimi @('build', $project, '--ToolchainRoot', $ToolchainRoot)
Write-Project 'O2' 'missing LLVM directory'
'::Kimi.Console.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
$ir = Join-Path $work 'bin/x86_64-pc-windows-msvc/Hello.ll'
$recordPath = [IO.Path]::ChangeExtension($ir, '.link.build.json')
$previousRecord = [IO.File]::ReadAllText($recordPath)
$output = Invoke-Kimi @('emit', $project)
if (-not (Test-Path $ir) -or [IO.File]::ReadAllText($recordPath) -cne $previousRecord) { throw 'emit must only publish LLVM inputs' }
$output = Invoke-Kimi @('build', $project) 1
$output = Invoke-Kimi @('run', $project) 1
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
    '::Kimi.Console.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
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
$output = Invoke-Kimi @('emit', (Join-Path $work 'missing.kimisln')) 1
$output = Invoke-Kimi @('build', $project, '--LlvmBin') 1

# Shared input resolution and an implicit Application must work through the real CLI.
$output = Invoke-Kimi @('emit-llvm', $project) 1
$projectStem = [IO.Path]::ChangeExtension($project, $null)
$output = Invoke-Kimi @('emit', $projectStem)
$output = Invoke-Kimi @('build', $projectStem, '--LlvmBin', $LlvmBin)
$output = Invoke-Kimi @('run', $projectStem)
if (-not $output.Contains('Hello, world!')) { throw 'Extensionless project lookup failed' }
$singleDirectory = Join-Path $work 'single source'
New-Item -ItemType Directory -Path $singleDirectory | Out-Null
$singleStem = Join-Path $singleDirectory 'Single'
$singleSource = "$singleStem.kimi"
$singleProject = "$singleStem.kimiproj"
$singleText = '::Kimi.Console.writeLine("Single source")'
$singleText | Set-Content -LiteralPath $singleSource -Encoding utf8
'let broken =' | Set-Content -LiteralPath (Join-Path $singleDirectory 'BrokenSibling.kimi') -Encoding utf8
$singleIr = Join-Path $singleDirectory 'bin/x86_64-pc-windows-msvc/Single.ll'
$singleRecordPath = [IO.Path]::ChangeExtension($singleIr, '.link.build.json')
$output = Invoke-Kimi @('run', $singleStem) 1
if (Test-Path $singleIr) { throw 'Implicit run generated LLVM inputs' }
$output = Invoke-Kimi @('emit', $singleStem, '--ToolchainRoot', (Join-Path $work 'missing toolchain'))
if (-not (Test-Path $singleIr) -or (Test-Path $singleRecordPath) -or (Test-Path $singleProject)) { throw 'Implicit emit must only publish LLVM inputs' }
$output = Invoke-Kimi @('build', $singleStem)
$singleRecord = Get-Content -LiteralPath $singleRecordPath -Raw | ConvertFrom-Json
if ($singleRecord.optimization -cne 'O2' -or $singleRecord.status -cne 'linked') { throw 'Implicit project did not use O2' }
$output = Invoke-Kimi @('run', $singleStem)
if (-not $output.Contains('Single source')) { throw 'Implicit source build/run failed' }
$singleBefore = [IO.File]::ReadAllText($singleRecordPath)
'let broken =' | Set-Content -LiteralPath $singleSource -Encoding utf8
$output = Invoke-Kimi @('run', $singleSource)
if (-not $output.Contains('Single source') -or [IO.File]::ReadAllText($singleRecordPath) -cne $singleBefore) { throw 'Source run must not read or rebuild changed source' }
$singleText | Set-Content -LiteralPath $singleSource -Encoding utf8
'OutputKind="Invalid"' | Set-Content -LiteralPath $singleProject -Encoding utf8
foreach ($command in @('build', 'run', 'emit')) {
    $output = Invoke-Kimi @($command, $singleStem) 1
}
# Explicit .kimi bypasses the invalid same-stem project and excludes its sibling source.
$output = Invoke-Kimi @('emit', $singleSource)
$output = Invoke-Kimi @('build', $singleSource)
$output = Invoke-Kimi @('run', $singleSource)
if (-not $output.Contains('Single source')) { throw 'Explicit source input was not honored' }
Remove-Item -LiteralPath $singleProject
New-Item -ItemType Directory -Path $singleStem | Out-Null
foreach ($command in @('build', 'run', 'emit')) {
    $output = Invoke-Kimi @($command, $singleStem) 1
}
Remove-Item -LiteralPath $singleStem

# A self-targeted static supply links #LibraryImport calls with mixed scalar arguments (SPEC 20.8.2, 22.3).
$foreignDirectory = Join-Path $work 'foreign supply'
$foreignNative = Join-Path $foreignDirectory 'native'
New-Item -ItemType Directory -Path $foreignNative -Force | Out-Null
@'
static int total;
double mix(signed char a, unsigned short b, int c, long long d, float e, double f) { return a + b + c + d + e + f; }
void notify(unsigned int value) { total += (int)value; }
int read_total(void) { return total; }
int mangled(void) __asm__("?value@@YAHXZ");
int mangled(void) { return 42; }
'@ | Set-Content -LiteralPath (Join-Path $foreignNative 'codec.c') -Encoding ascii
& (Join-Path $ToolchainRoot 'clang.exe') --target=x86_64-pc-windows-msvc -O2 -fno-autolink -fno-stack-protector -c (Join-Path $foreignNative 'codec.c') -o (Join-Path $foreignNative 'codec.obj')
if ($LASTEXITCODE -ne 0) { throw 'clang failed for the foreign supply' }
& (Join-Path $ToolchainRoot 'llvm-lib.exe') "/out:$(Join-Path $foreignNative 'codec.lib')" (Join-Path $foreignNative 'codec.obj')
if ($LASTEXITCODE -ne 0) { throw 'llvm-lib failed for the foreign supply' }
$foreignProject = Join-Path $foreignDirectory 'Foreign.kimiproj'
function Write-ForeignProject([string] $Level, [string] $Supply) {
    @"
Targets=
  "x86_64-pc-windows-msvc"
OutputKind="Application"
Optimization="$Level"
$Supply
"@ | Set-Content -LiteralPath $foreignProject -Encoding utf8
}
$foreignSupply = "NativeLibraries=`n  `"x86_64-pc-windows-msvc`"=`n    codec={ Kind=`"static`" Input=`"native/codec.lib`" }"
@'
group Native
    #LibraryImport("codec", "mix")
    public unsafe func mix(a: i8, b: u16, c: i32, d: i64, e: f32, f: f64) -> f64
    #LibraryImport("codec", "notify")
    public unsafe func notify(value: u32) -> ()
    #LibraryImport("codec", "read_total")
    public unsafe func readTotal() -> i32
    #LibraryImport("codec", "?value@@YAHXZ")
    public unsafe func mangled() -> i32
public func main()
    var total: f64 = 0.0
    unsafe => total = Native.mix(-1, 65535, 3, -4, 0.5, 2.25)
    require total == 65535.75 else => $abort("mix")
    unsafe => Native.notify(7)
    unsafe => Native.notify(5)
    var sum: i32 = 0
    unsafe => sum = Native.readTotal()
    require sum == 12 else => $abort("notify")
    var answer: i32 = 0
    unsafe => answer = Native.mangled()
    require answer == 42 else => $abort("mangled")
    Console.writeLine("foreign ok")
'@ | Set-Content -LiteralPath (Join-Path $foreignDirectory 'Foreign.kimi') -Encoding utf8
foreach ($level in @('O0', 'O2')) {
    Write-ForeignProject $level $foreignSupply
    $output = Invoke-Kimi @('build', $foreignProject)
    $output = Invoke-Kimi @('run', $foreignProject)
    if (-not $output.Contains('foreign ok')) { throw "Foreign static supply failed at $level`: $output" }
}
$foreignRecord = Get-Content -LiteralPath (Join-Path $foreignDirectory 'bin/x86_64-pc-windows-msvc/Foreign.link.build.json') -Raw | ConvertFrom-Json
$codecRecord = $foreignRecord.libraries | Where-Object name -ceq 'codec'
$codecHash = (Get-FileHash (Join-Path $foreignNative 'codec.lib')).Hash.ToLowerInvariant()
if ($codecRecord.sha256 -cne $codecHash -or -not $codecRecord.path.Replace('\', '/').EndsWith(".native/$codecHash.lib")) { throw "Foreign supply was not linked from its staged snapshot: $($codecRecord | ConvertTo-Json)" }
$foreignManifest = Get-Content -LiteralPath (Join-Path $foreignDirectory 'bin/x86_64-pc-windows-msvc/Foreign.link.json') -Raw | ConvertFrom-Json
if (($foreignManifest.libraries | ForEach-Object name) -join ',' -cne 'codec,kernel32,kimi_backend' -or $foreignManifest.libraries[0].kind -cne 'static') { throw 'Foreign supply manifest entries are not sorted/complete' }
Write-ForeignProject 'O0' ($foreignSupply.Replace('Input=', 'Sha256="' + ('0' * 64) + '" Input='))
$output = Invoke-Kimi @('build', $foreignProject) 1
if (-not $output.Contains('Sha256 assertion')) { throw "Foreign Sha256 assertion was not checked: $output" }
Write-ForeignProject 'O0' "NativeRequirements=`n  `"x86_64-pc-windows-msvc`"=`n    codec={ Kind=`"static`" }"
$output = Invoke-Kimi @('emit', $foreignProject) 1
if (-not $output.Contains('has no NativeLibraries supply')) { throw "A required foreign supply was not diagnosed: $output" }

@{ status = 'passed'; configuration = $Configuration; scenarios = @('emit without LLVM', 'O0/O2 native build', 'run without compilation', 'failure invalidates old success', 'toolchain policy', 'spaces in paths', 'exit code forwarding', 'missing inputs', 'emit rename', 'extensionless project lookup', 'implicit single-source Application/O2', 'source run without compilation', 'exact path precedence', 'invalid selection never falls back', 'foreign static supply O0/O2 and assertion/supply failures') } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $report
Write-Output "CLI integration tests passed: $report"
