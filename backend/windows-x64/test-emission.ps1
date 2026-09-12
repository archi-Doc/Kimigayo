[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LlvmBin,
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'toolchain.ps1')
. (Join-Path $PSScriptRoot 'artifact-paths.ps1')
. (Join-Path $PSScriptRoot 'kernel32.ps1')
$profile = Read-KimiWindowsProfile
$expectedVersion = $profile.llvmVersion
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtures = Join-Path $repo "bin/emission-fixtures/$Configuration"
$out = Join-Path $repo "bin/emission-native/$Configuration"
New-Item -ItemType Directory -Force $out | Out-Null
$report = Join-Path $out 'verification.json'
@{ status = 'incomplete' } | ConvertTo-Json | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $report -Encoding utf8
$tools = @{}
$identities = @{}
foreach ($name in @('clang', 'opt', 'llc', 'lld-link', 'llvm-nm', 'llvm-readobj')) {
    $tools[$name] = Join-Path $LlvmBin "$name.exe"
    $identities[$name] = Get-KimiLlvmToolIdentity $tools[$name] $expectedVersion
}
@{ status = 'incomplete'; llvmVersion = $expectedVersion; reportedVersionsMatched = $true; unverifiedToolchain = $false; tools = $identities } | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $report -Encoding utf8
$tools['llvm-dlltool'] = Join-Path $LlvmBin 'llvm-dlltool.exe'
$identities['llvm-dlltool'] = Get-KimiDlltoolIdentity $tools['llvm-dlltool']
@{ status = 'incomplete'; llvmVersion = $expectedVersion; reportedVersionsMatched = $true; unverifiedToolchain = $false; tools = $identities } | ConvertTo-Json -Depth 8 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $report -Encoding utf8
$kernel = New-KimiKernel32Library $tools (Join-Path $out 'kernel32.lib')
$Kernel32 = $kernel.path
$archive = Join-Path $PSScriptRoot 'bin/kimi_backend_windows_x64_v1.lib'
$candidate = Get-Content (Join-Path $PSScriptRoot 'bin/verification.json') -Raw | ConvertFrom-Json
if (-not $candidate.reportedVersionsMatched -or $candidate.unverifiedToolchain -or $candidate.llvmVersion -cne $expectedVersion -or $candidate.status -cne 'tested-candidate' -or
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $candidate.artifactSha256) { throw 'Run the pinned native backend verification first' }
function Invoke-Tool([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE)" }
}
function Compile-Ir([string] $ir, [string] $stem, [string] $level) {
    Invoke-Tool $tools.opt @('-passes=verify', '-disable-output', $ir)
    if ($level -eq 'O2') {
        $optimized = "$stem.ll"
        Invoke-Tool $tools.opt @('-S', '-passes=default<O2>', $ir, '-o', $optimized)
        Protect-KimiLlvmPaths $optimized
        Invoke-Tool $tools.opt @('-passes=verify', '-disable-output', $optimized)
        $ir = $optimized
    }
    Invoke-KimiLlvmOutput $tools.llc @("-$level", '-filetype=obj', '-mtriple=x86_64-pc-windows-msvc', '-mcpu=x86-64', '-mattr=+sse2', '-relocation-model=pic', '-code-model=small', $ir) "$stem.obj"
    $undefined = & $tools['llvm-nm'] --undefined-only --format=posix "$stem.obj" | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect dependencies' }
    $undefined | ConvertTo-KimiArtifactText | Set-Content -LiteralPath "$stem.undefined.txt" -Encoding utf8
    $unwind = & $tools['llvm-readobj'] --unwind --coff-directives "$stem.obj" | Out-String
    if ($LASTEXITCODE -ne 0 -or $unwind -notmatch 'RuntimeFunction' -or $unwind -match '(?i)DEFAULTLIB') { throw 'Invalid unwind/CRT dependency' }
    $unwind | ConvertTo-KimiArtifactText | Set-Content -LiteralPath "$stem.unwind.txt" -Encoding utf8
}
function Execute([string] $exe, [byte[]] $stdout) {
    $start = [Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $p = [Diagnostics.Process]::Start($start)
    $output = [IO.MemoryStream]::new()
    $copy = $p.StandardOutput.BaseStream.CopyToAsync($output)
    $errors = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(30000)) { $p.Kill(); $p.WaitForExit(); throw "Timed out: $exe" }
    $null = $copy.GetAwaiter().GetResult()
    $stderr = $errors.GetAwaiter().GetResult()
    $actual = $output.ToArray()
    if ($p.ExitCode -ne 0 -or $stderr -or [Convert]::ToHexString($actual) -cne [Convert]::ToHexString($stdout)) { throw "Execution failed: $exe, exit=$($p.ExitCode), stderr=$stderr, stdout=$([Convert]::ToHexString($actual))" }
    $p.Dispose()
    $output.Dispose()
}
$testResults = @()
foreach ($level in @('O0', 'O2')) {
    foreach ($name in @('Hello', 'Empty', 'Unicode')) {
        $stem = Join-Path $out "$name.$level"
        Compile-Ir (Join-Path $fixtures "$name.ll") $stem $level
        Invoke-Tool $tools['lld-link'] @("$stem.obj", $archive, $Kernel32, '/entry:__kimi_start', '/subsystem:console', '/nodefaultlib', '/Brepro', "/out:$stem.exe")
        [byte[]] $expected = switch ($name) {
            'Hello' { [Text.Encoding]::UTF8.GetBytes("Hello, world!`n") }
            'Empty' { ,([byte[]]@(10)) }
            'Unicode' { [Text.Encoding]::UTF8.GetBytes("日本語`0x`n") }
        }
        Execute "$stem.exe" $expected
        $testResults += "$name.$level"
    }
}

# Use the actual emitted module, changing only OS declarations to explicitly named test adapters.
$faultIr = Get-Content (Join-Path $fixtures 'Hello.ll') -Raw
foreach ($name in @('GetProcessHeap', 'HeapAlloc', 'HeapFree', 'GetStdHandle', 'WriteFile', 'GetLastError', 'ExitProcess')) {
    $faultIr = $faultIr.Replace("@$name(", "@test_$name(")
}
$faultIr = $faultIr.Replace('declare dllimport ', 'declare ')
$faultIr += @'

@test_mode = external global i32
@test_location = private constant [13 x i8] c"Test.kimi:1:1"
declare ptr @test_memory()
declare void @test_layout(i64, i64, i64, i64)
'@
for ($mode = 0; $mode -le 28; $mode++) {
    $body = switch ($mode) {
        { $_ -ge 8 -and $_ -le 12 } {
            $ptr, $length = switch ($mode) {
                8 { 'null', '0' }
                9 { 'null', '1' }
                10 { '@__kimi_text', '9223372036854775808' }
                11 { 'inttoptr (i64 -3 to ptr)', '4' }
                12 { 'inttoptr (i64 65536 to ptr)', '4294967298' }
            }
            $expectedError = if ($mode -eq 8 -or $mode -eq 12) { '-1' } else { '-2' }
            "  %result = call i64 @__kimi_write_bytes(i32 -11, ptr $ptr, i64 $length)`n  %ok = icmp eq i64 %result, $expectedError`n  %code = select i1 %ok, i32 0, i32 111`n  call void @__kimi_exit(i32 %code)`n  unreachable"
        }
        13 { "  call void @__kimi_free(ptr null, ptr @test_location, i64 13)`n  call void @__kimi_exit(i32 0)`n  unreachable" }
        14 { "  %memory = call ptr @__kimi_alloc(i64 0, ptr @test_location, i64 13)`n  call void @__kimi_free(ptr %memory, ptr @test_location, i64 13)`n  call void @__kimi_stdout(ptr @__kimi_lf, i64 1, ptr @test_location, i64 13)`n  call void @__kimi_exit(i32 0)`n  unreachable" }
        { $_ -eq 17 -or $_ -eq 19 } { "  %memory = call ptr @test_memory()`n  call void @__kimi_free(ptr %memory, ptr @test_location, i64 13)`n  call void @__kimi_exit(i32 0)`n  unreachable" }
        18 { "  %slot = alloca %kimi.string, align 8`n  store %kimi.string { ptr @__kimi_text, i64 13, i8 3 }, ptr %slot, align 8`n  call void @__kimi_destroy_string(ptr %slot, ptr @test_location, i64 13)`n  call void @__kimi_exit(i32 0)`n  unreachable" }
        20 { "  %memory = call ptr @__kimi_alloc(i64 -1, ptr @test_location, i64 13)`n  call void @__kimi_exit(i32 0)`n  unreachable" }
        default {
            $heap = $mode -in @(14, 15, 16, 22, 24, 26)
            $length = if ($mode -in @(14, 21, 24)) { 0 } else { 13 }
            $value = if ($heap) { "%memory" } else { "@__kimi_text" }
            $kind = if ($heap) { 1 } else { 0 }
            $acquire = if ($heap) { "  %memory = call ptr @__kimi_alloc(i64 $length, ptr @test_location, i64 13)`n" } else { '' }
            "$acquire  %slot = alloca %kimi.string, align 8`n  %handle = insertvalue %kimi.string { ptr null, i64 $length, i8 $kind }, ptr $value, 0`n  store %kimi.string %handle, ptr %slot, align 8`n  call void @__kimi_write_line(ptr %slot, ptr @test_location, i64 13)`n  call void @__kimi_exit(i32 0)`n  unreachable"
        }
    }
    $faultIr += "`ndefine void @test_entry_$mode() noreturn #0 {`nentry:`n  store i32 $mode, ptr @test_mode, align 4`n  call void @test_layout(i64 ptrtoint (ptr getelementptr (%kimi.string, ptr null, i64 1) to i64), i64 ptrtoint (ptr getelementptr ({ i8, %kimi.string }, ptr null, i32 0, i32 1) to i64), i64 ptrtoint (ptr getelementptr (%kimi.string, ptr null, i32 0, i32 1) to i64), i64 ptrtoint (ptr getelementptr (%kimi.string, ptr null, i32 0, i32 2) to i64))`n$body`n}`n"
}
$faultPath = Join-Path $out 'faults.ll'
[IO.File]::WriteAllText($faultPath, $faultIr, [Text.UTF8Encoding]::new($false))
$adapter = Join-Path $out 'runtime-adapter.obj'
Invoke-Tool $tools.clang @('--target=x86_64-pc-windows-msvc', '-c', '-O2', '-ffreestanding', '-fno-builtin', '-fno-stack-protector', '-fasynchronous-unwind-tables', '-march=x86-64', '-mno-avx', (Join-Path $PSScriptRoot 'tests/runtime-adapter.c'), '-o', $adapter)
foreach ($level in @('O0', 'O2')) {
    $stem = Join-Path $out "faults.$level"
    Compile-Ir $faultPath $stem $level
    for ($mode = 0; $mode -le 28; $mode++) {
        $exe = "$stem.$mode.exe"
        Invoke-Tool $tools['lld-link'] @("$stem.obj", $adapter, $archive, $Kernel32, "/entry:test_entry_$mode", '/subsystem:console', '/nodefaultlib', '/Brepro', "/out:$exe")
        Execute $exe ([byte[]]@())
        $testResults += "runtime.$mode.$level"
    }
}
@{ status = 'passed'; compilerConfiguration = $Configuration; llvmVersion = $expectedVersion; reportedVersionsMatched = $true; unverifiedToolchain = $false; tools = $identities; kernel32 = $kernel; archiveSha256 = $candidate.artifactSha256; tests = $testResults } | ConvertTo-Json -Depth 5 | ConvertTo-KimiArtifactText | Set-Content -LiteralPath $report -Encoding utf8
Write-Output "Passed $($testResults.Count) native executions ($Configuration): $report"
