[CmdletBinding()]
param(
    [string] $ToolchainRoot = '', [string] $LlvmBin = '',
    [string] $MismatchedLlvmBin = '',
    [string] $NativeCompiler = '',
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'expected-output.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
if (-not $LlvmBin) { $LlvmBin = $ToolchainRoot }
$catalog = Read-KimiWindowsProfile
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
$utf8 = [Text.UTF8Encoding]::new($false)
$work = Join-Path $repo ('artifacts/verify/cli-tests/' + [guid]::NewGuid().ToString('N') + '/project with spaces')
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
    $stdout = [IO.MemoryStream]::new()
    $stderr = [IO.MemoryStream]::new()
    $copies = @($process.StandardOutput.BaseStream.CopyToAsync($stdout), $process.StandardError.BaseStream.CopyToAsync($stderr))
    if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw 'CLI timed out' }
    foreach ($copy in $copies) { $null = $copy.GetAwaiter().GetResult() }
    # The captured bytes are the CLI's (and a run child's) output; messages are read from their UTF-8 text.
    $script:childStdout = $stdout.ToArray()
    $script:childStderr = $stderr.ToArray()
    $output = $utf8.GetString($script:childStdout) + $utf8.GetString($script:childStderr)
    $actual = $process.ExitCode
    $process.Dispose()
    if ($actual -ne $ExitCode) { throw "CLI exit $actual, expected $ExitCode`: $($Arguments -join ' ')`n$output" }
    return $output
}

Write-Project 'O2' ''
'::Kimi.Console.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
$output = Invoke-Kimi @('run', $project)
if (-not $output.Contains('Hello, world!')) { throw "Automatic toolchain resolution failed: $output" }
$output = Invoke-Kimi @('build', $project, '--ToolchainRoot', (Join-Path $work 'missing toolchain')) 1
if (-not $output.Contains('Tatara not found') -or -not $output.Contains('tatara.dll') -or -not $output.Contains('setup.ps1 -LlvmRoot')) {
    throw "Explicit toolchain root must override the environment, and a root without tatara.dll must request setup: $output"
}
# IMPL 20.3.2, 20.4: a native build runs no LLVM tool and links the compiler's own libraries. From a root holding a copy of tatara.dll
# (each kimi process maps its own) and nothing else, Hello builds and runs, build --Manifest and a test project's build do too, and the
# root stays as it was.
$tataraRoot = Join-Path $work 'tatara only'
New-Item -ItemType Directory -Path $tataraRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'tatara.dll') -Destination $tataraRoot
$output = Invoke-Kimi @('build', $project, '--ToolchainRoot', $tataraRoot)
$output = Invoke-Kimi @('run', $project, '--no-build')
if (-not $output.Contains('Hello, world!')) { throw "A native build needed more than tatara.dll: $output" }
$output = Invoke-Kimi @('build', '--Manifest', (Join-Path $work 'bin/x86_64-pc-windows-msvc/Hello.link.json'), '--ToolchainRoot', $tataraRoot)
$output = Invoke-Kimi @('run', $project, '--no-build')
if (-not $output.Contains('Hello, world!')) { throw "A manifest build needed more than tatara.dll: $output" }
# kimi test takes a path with spaces as every command does (PLAN G1214): its project and root have spaces, beside the work folder.
$testRoot = Join-Path (Split-Path $work) 'test tatara only'
New-Item -ItemType Directory -Path $testRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $ToolchainRoot 'tatara.dll') -Destination $testRoot
$testDirectory = Join-Path (Split-Path $work) 'test project'
New-Item -ItemType Directory -Path $testDirectory | Out-Null
@'
#Test
func passes()
    $require(1 == 1)
'@ | Set-Content -LiteralPath (Join-Path $testDirectory 'tests.kimi') -Encoding utf8
"Targets = { `"x86_64-pc-windows-msvc`" }`nOutputKind = `"Library`"`nTestSources = { `"tests.kimi`" }" | Set-Content -LiteralPath (Join-Path $testDirectory 'Tests.kimiproj') -Encoding utf8
$output = Invoke-Kimi @('test', (Join-Path $testDirectory 'Tests.kimiproj'), '--ToolchainRoot', $testRoot)
foreach ($root in @($tataraRoot, $testRoot)) {
    $rootFiles = @(Get-ChildItem -LiteralPath $root -Name) -join ','
    if ($rootFiles -cne 'tatara.dll') { throw "A build wrote into the toolchain root: $rootFiles" }
}
$output = Invoke-Kimi @('build', $project, '--ToolchainRoot', $ToolchainRoot)
# A project's LlvmBin is accepted and not read: with a missing directory emit publishes only the LLVM inputs, and the build succeeds.
Write-Project 'O2' 'missing LLVM directory'
'::Kimi.Console.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
$ir = Join-Path $work 'bin/x86_64-pc-windows-msvc/Hello.ll'
$recordPath = [IO.Path]::ChangeExtension($ir, '.link.build.json')
$previousRecord = [IO.File]::ReadAllBytes($recordPath)
$output = Invoke-Kimi @('emit', $project)
if (-not (Test-Path $ir) -or -not (Test-KimiExpectedOutput $previousRecord ([IO.File]::ReadAllBytes($recordPath)))) { throw 'emit must only publish LLVM inputs' }
$output = Invoke-Kimi @('build', $project)
$output = Invoke-Kimi @('run', $project, '--no-build')
if (-not $output.Contains('Hello, world!')) { throw "A project's missing LlvmBin failed the build: $output" }
foreach ($level in @('O0', 'O2')) {
    Write-Project $level 'missing LLVM directory'
    $output = Invoke-Kimi @('build', $project, '--LlvmBin', $LlvmBin)
    if ($output -match '(?m)^Hello, world!') { throw 'Build executed the Application' }
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.status -cne 'linked' -or $record.toolchainVerification -cne 'not-performed' -or $null -ne $record.reportedVersionsMatched -or -not $record.unverifiedToolchain) { throw 'Invalid successful build record' }
    # The record's libraries are the compiler's, each its staged snapshot (<stem>.native/<hash>.lib) with the catalog's hash.
    $libraries = @($record.libraries | ForEach-Object { "$($_.name) $($_.sha256) $($_.path.Replace('\', '/').EndsWith(".native/$($_.sha256).lib"))" }) -join ','
    if ($null -ne $record.kernel32 -or $libraries -cne "kernel32 $($catalog.kernel32.sha256) True,kimi_backend $($catalog.artifactSha256) True") {
        throw "Ordinary build must link the compiler's staged libraries: $libraries"
    }
    # The record's tools name Tatara alone: the root's tatara.dll with its build, ABI and LLVM version.
    $tatara = $record.tools.tatara
    if ((@($record.tools.PSObject.Properties.Name) -join ',') -cne 'tatara' -or -not $tatara.path.Replace('\', '/').EndsWith('/tatara.dll') -or
        -not $tatara.build -or $tatara.abi -cnotmatch '^[0-9a-f]{64}$' -or $tatara.llvmVersion -cne $record.llvmVersion) { throw "The record's tools must name Tatara: $($record.tools | ConvertTo-Json)" }
    $exe = Join-Path (Split-Path $ir) "Hello.$level.exe"
    $before = (Get-Item $recordPath).LastWriteTimeUtc
    $output = Invoke-Kimi @('run', $project, '--no-build')
    if (-not $output.Contains("Hello, world!")) { throw 'Run did not forward stdout' }
    if ((Get-Item $recordPath).LastWriteTimeUtc -ne $before) { throw 'Run --no-build rewrote build metadata' }
    $output = Invoke-Kimi @('run', $exe)
    if (-not (Test-KimiExpectedOutput $utf8.GetBytes("Hello, world!`n") $childStdout) -or -not (Test-KimiExpectedOutput ([byte[]]@()) $childStderr)) {
        throw 'Direct binary execution stdout mismatch'
    }
    '::Kimi.Console.writeLine("Updated source")' | Set-Content -LiteralPath $source -Encoding utf8
    $output = Invoke-Kimi @('run', $project, '--LlvmBin', $LlvmBin)
    if ($output -notmatch '(?m)^Updated source\r?$' -or $output -match '(?m)^Hello, world!\r?$') { throw 'Run did not build the current source' }
    $hash = (Get-FileHash $exe).Hash
    'let broken =' | Set-Content -LiteralPath $source
    $output = Invoke-Kimi @('run', $project, '--no-build')
    if ($output -notmatch '(?m)^Updated source\r?$') { throw 'Run --no-build compiled modified sources' }
    $output = Invoke-Kimi @('run', $project, '--LlvmBin', $LlvmBin) 1
    if ($output -match '(?m)^Updated source\r?$') { throw 'Run executed a stale binary after build failure' }
    $output = Invoke-Kimi @('run', $project, '--no-build') 1
    if ((Get-FileHash $exe).Hash -cne $hash) { throw 'Failed build overwrote the last executable' }
    '::Kimi.Console.writeLine("Hello, world!")' | Set-Content -LiteralPath $source -Encoding utf8
}
if ($MismatchedLlvmBin) {
    $output = Invoke-Kimi @('toolchain', 'verify', '--LlvmBin', $MismatchedLlvmBin) 1
    if (-not $output.Contains('mismatch')) { throw 'Explicit verification must reject a changed toolchain' }
}
# The command line's LlvmBin names the tools toolchain verify checks; a build accepts it and does not read it: a bin without tools builds.
$noTools = Join-Path $work 'no tools'
New-Item -ItemType Directory -Path $noTools | Out-Null
$output = Invoke-Kimi @('build', $project, '--LlvmBin', $noTools)
$output = Invoke-Kimi @('run', $project, '--no-build')
if (-not $output.Contains('Hello, world!')) { throw "A bin without tools failed the build: $output" }
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
$kernel32 = Join-Path $ToolchainRoot 'windows_x64/kernel32.lib'
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
$output = Invoke-Kimi @('run', $projectStem, '--no-build')
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
$output = Invoke-Kimi @('run', $singleStem, '--no-build') 1
if (Test-Path $singleIr) { throw 'Implicit run --no-build generated LLVM inputs' }
$output = Invoke-Kimi @('emit', $singleStem, '--ToolchainRoot', (Join-Path $work 'missing toolchain'))
if (-not (Test-Path $singleIr) -or (Test-Path $singleRecordPath) -or (Test-Path $singleProject)) { throw 'Implicit emit must only publish LLVM inputs' }
$output = Invoke-Kimi @('run', $singleStem)
if (-not $output.Contains('Single source')) { throw 'Implicit source build/run failed' }
$singleRecord = Get-Content -LiteralPath $singleRecordPath -Raw | ConvertFrom-Json
if ($singleRecord.optimization -cne 'O2' -or $singleRecord.status -cne 'linked') { throw 'Implicit project did not use O2' }
'::Kimi.Console.writeLine("Updated single source")' | Set-Content -LiteralPath $singleSource -Encoding utf8
$output = Invoke-Kimi @('run', $singleSource)
if ($output -notmatch '(?m)^Updated single source\r?$') { throw 'Implicit run did not rebuild changed source' }
$singleBefore = [IO.File]::ReadAllBytes($singleRecordPath)
'let broken =' | Set-Content -LiteralPath $singleSource -Encoding utf8
$output = Invoke-Kimi @('run', $singleSource, '--no-build')
if ($output -notmatch '(?m)^Updated single source\r?$' -or -not (Test-KimiExpectedOutput $singleBefore ([IO.File]::ReadAllBytes($singleRecordPath)))) { throw 'Source run --no-build must not read or rebuild changed source' }
$output = Invoke-Kimi @('run', $singleSource) 1
if ($output -match '(?m)^Updated single source\r?$') { throw 'Implicit run executed a stale binary after build failure' }
$output = Invoke-Kimi @('run', $singleSource, '--no-build') 1
$singleText | Set-Content -LiteralPath $singleSource -Encoding utf8
'OutputKind="Invalid"' | Set-Content -LiteralPath $singleProject -Encoding utf8
foreach ($command in @('build', 'run', 'emit')) {
    $output = Invoke-Kimi @($command, $singleStem) 1
}
# Explicit .kimi bypasses the invalid same-stem project and excludes its sibling source.
$output = Invoke-Kimi @('emit', $singleSource)
$output = Invoke-Kimi @('build', $singleSource)
$output = Invoke-Kimi @('run', $singleSource, '--no-build')
if (-not $output.Contains('Single source')) { throw 'Explicit source input was not honored' }
Remove-Item -LiteralPath $singleProject
New-Item -ItemType Directory -Path $singleStem | Out-Null
foreach ($command in @('build', 'run', 'emit')) {
    $output = Invoke-Kimi @($command, $singleStem) 1
}
Remove-Item -LiteralPath $singleStem

# Abort forwards a normal stdout prefix, stderr and nonzero exit for source/project/exe.
$abortDirectory = Join-Path $work 'abort'
New-Item -ItemType Directory -Path $abortDirectory | Out-Null
$abortSource = Join-Path $abortDirectory 'Abort.kimi'
$abortProject = Join-Path $abortDirectory 'Abort.kimiproj'
'::Kimi.Console.writeLine("before abort")', '$abort("cli abort")' | Set-Content -LiteralPath $abortSource -Encoding utf8
"Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"" | Set-Content -LiteralPath $abortProject -Encoding utf8
$abortStdout = $utf8.GetBytes("before abort`n")
$abortStderr = $utf8.GetBytes("Abort.kimi:2:1: abort KIMI_E_ABORT: cli abort`n")
# A source or project run may print build lines first, so its stdout ends with the child's.
$output = Invoke-Kimi @('run', $abortSource) 1
if (-not (Test-KimiExpectedOutput $abortStdout ([Linq.Enumerable]::ToArray([Linq.Enumerable]::TakeLast($childStdout, $abortStdout.Length)))) -or -not (Test-KimiExpectedOutput $abortStderr $childStderr)) { throw "Source run lost child output/error: $output" }
$output = Invoke-Kimi @('run', $abortProject, '--no-build') 1
if (-not (Test-KimiExpectedOutput $abortStdout ([Linq.Enumerable]::ToArray([Linq.Enumerable]::TakeLast($childStdout, $abortStdout.Length)))) -or -not (Test-KimiExpectedOutput $abortStderr $childStderr)) { throw "Project run lost child output/error: $output" }
$output = Invoke-Kimi @('run', (Join-Path $abortDirectory 'bin/x86_64-pc-windows-msvc/Abort.O2.exe'), '--no-build') 1
if (-not (Test-KimiExpectedOutput $abortStdout $childStdout) -or -not (Test-KimiExpectedOutput $abortStderr $childStderr)) { throw "Executable run lost child output/error: $output" }
# A self-targeted static supply links #LibraryImport calls with mixed scalar arguments (SPEC 20.4, 22.6).
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
    $output = Invoke-Kimi @('run', $foreignProject, '--no-build')
    if (-not $output.Contains('foreign ok')) { throw "Foreign static supply failed at $level`: $output" }
    $manifestPath = Join-Path $foreignDirectory 'bin/x86_64-pc-windows-msvc/Foreign.link.json'
    $output = Invoke-Kimi @('build', '--Manifest', $manifestPath)
    $output = Invoke-Kimi @('run', $foreignProject, '--no-build')
    if (-not $output.Contains('foreign ok')) { throw "Manifest static supply failed at $level" }
    & (Join-Path $PSScriptRoot 'manual-build.ps1') -Manifest $manifestPath -ToolchainRoot $ToolchainRoot -Compiler $compiler -Run

}
$foreignRecord = Get-Content -LiteralPath (Join-Path $foreignDirectory 'bin/x86_64-pc-windows-msvc/Foreign.link.build.json') -Raw | ConvertFrom-Json
$codecRecord = $foreignRecord.libraries | Where-Object name -ceq 'codec'
$codecHash = (Get-FileHash (Join-Path $foreignNative 'codec.lib')).Hash.ToLowerInvariant()
if ($codecRecord.sha256 -cne $codecHash -or -not $codecRecord.path.Replace('\', '/').EndsWith(".native/$codecHash.lib")) { throw "Foreign supply was not linked from its staged snapshot: $($codecRecord | ConvertTo-Json)" }
$foreignManifest = Get-Content -LiteralPath (Join-Path $foreignDirectory 'bin/x86_64-pc-windows-msvc/Foreign.link.json') -Raw | ConvertFrom-Json
if (($foreignManifest.libraries | ForEach-Object name) -join ',' -cne 'codec,kernel32,kimi_backend' -or $foreignManifest.libraries[0].kind -cne 'static') { throw 'Foreign supply manifest entries are not sorted/complete' }
# The standalone manifest retains the expanded assertion, and failure cannot publish a partial executable.
$manifestPath = Join-Path $foreignDirectory 'bin/x86_64-pc-windows-msvc/Foreign.link.json'
$foreignIr = Join-Path (Split-Path $manifestPath) 'Foreign.ll'
$previousExecutable = Join-Path (Split-Path $manifestPath) 'Foreign.O2.exe'
$previousHash = (Get-FileHash -LiteralPath $previousExecutable).Hash
$originalIr = [IO.File]::ReadAllText($foreignIr)
$originalManifest = [IO.File]::ReadAllText($manifestPath)
try {
    [IO.File]::WriteAllText($foreignIr, $originalIr.Replace('@read_total(', '@missing_foreign_symbol('))
    $foreignManifest.irSha256 = (Get-FileHash -LiteralPath $foreignIr).Hash.ToLowerInvariant()
    ($foreignManifest.externalDeclarations | Where-Object symbol -ceq 'read_total').symbol = 'missing_foreign_symbol' # The pair states its declarations.
    $foreignManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $output = Invoke-Kimi @('build', '--Manifest', $manifestPath) 1
    if (-not $output.Contains('missing_foreign_symbol') -or -not $output.Contains('lld-link:')) { throw "Expected link failure: $output" }
    if ((Get-FileHash -LiteralPath $previousExecutable).Hash -cne $previousHash) { throw 'A failed link replaced the previous executable' }
    $failedRecord = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($manifestPath, '.build.json')) -Raw | ConvertFrom-Json
    if ($failedRecord.status -cne 'incomplete') { throw 'A failed manifest build retained success' }
}
finally {
    [IO.File]::WriteAllText($foreignIr, $originalIr)
    [IO.File]::WriteAllText($manifestPath, $originalManifest)
}
Write-ForeignProject 'O2' ($foreignSupply.Replace('Input=', 'Sha256="' + $codecHash + '" Input='))
$output = Invoke-Kimi @('emit', $foreignProject)
$asserted = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($asserted.libraries[0].sha256 -cne $codecHash) { throw 'Manifest lost the supplied hash assertion' }
$output = Invoke-Kimi @('build', '--Manifest', $manifestPath)
$asserted.libraries[0].sha256 = '0' * 64
$asserted | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$output = Invoke-Kimi @('build', '--Manifest', $manifestPath) 1
if (-not $output.Contains('manifest Sha256 assertion')) { throw "Standalone manifest ignored its hash assertion: $output" }
$output = Invoke-Kimi @('build', $foreignProject, '--Manifest', $manifestPath) 1
if (-not $output.Contains('without source, project')) { throw 'Manifest and source arguments must not be combined' }

# Clock imports are declared foreign dependencies, beyond the runtime's kernel32 subset.
$clockSource = Join-Path $work 'Clock.kimi'
@'
var watch = Time.Stopwatch.init()
watch.start()
watch.stop()
let elapsed = watch.elapsed()
Console.writeLine("clock ok")
'@ | Set-Content -LiteralPath $clockSource -Encoding utf8
$output = Invoke-Kimi @('emit', $clockSource)
$clockManifest = Join-Path $work 'bin/x86_64-pc-windows-msvc/Clock.link.json'
& (Join-Path $PSScriptRoot 'manual-build.ps1') -Manifest $clockManifest -ToolchainRoot $ToolchainRoot -Compiler $compiler -Run

Write-ForeignProject 'O0' ($foreignSupply.Replace('Input=', 'Sha256="' + ('0' * 64) + '" Input='))
$output = Invoke-Kimi @('build', $foreignProject) 1
if (-not $output.Contains('Sha256 assertion')) { throw "Foreign Sha256 assertion was not checked: $output" }
Write-ForeignProject 'O0' "NativeRequirements=`n  `"x86_64-pc-windows-msvc`"=`n    codec={ Kind=`"static`" }"
$output = Invoke-Kimi @('emit', $foreignProject) 1
if (-not $output.Contains('has no NativeLibraries supply')) { throw "A required foreign supply was not diagnosed: $output" }

@{ status = 'passed'; configuration = $Configuration; scenarios = @('emit without LLVM', 'O0/O2 native build', 'build and run by default', 'explicit no-build', 'failure invalidates old success', 'toolchain policy', 'native build runs no LLVM tool', 'compiler-supplied libraries from a tatara-only root', 'LlvmBin accepted and not read', 'record names Tatara', 'spaces in paths', 'exit code forwarding', 'missing inputs', 'emit rename', 'extensionless project lookup', 'implicit single-source Application/O2', 'implicit source build and run', 'exact path precedence', 'invalid selection never falls back', 'foreign static supply O0/O2 and assertion/supply failures', 'shared manifest/manual build', 'declared clock imports', 'failed link preserves executable', 'manifest hash assertions') } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $report
Write-Output "CLI integration tests passed: $report"
