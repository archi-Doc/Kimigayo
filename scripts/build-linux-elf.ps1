# Builds the Linux ELF programs CI's linux-elf job runs (Tatara B7b9, design §3.13; a development and CI script, never a test).
# With the built src/Kimi/bin/<Configuration>/net10.0/Kimi.dll it copies tests/linux-elf's sources (each program folder's *.kimi,
# *.kimiproj and native/*.c, never a local build's output) to a work folder and there:
# - compiles each native/<stem>.c with the toolchain's clang for x86_64-unknown-linux-musl and archives it as native/<stem>.a by a copy
#   of the toolchain's llvm-lib.exe named llvm-ar.exe in the work folder, never in toolchain\ (B7b7 ruling 7a);
# - builds each project tests/linux-elf/expectations.json names (a file <Project>-<O0|O2>: the one <Project>.kimiproj under
#   tests/linux-elf, restored, then built with that Optimization for the Linux target), copying its ELF to <Output>\<file>;
# - builds every natively verified milestone, tests/milestones/MilestoneN.kimi with a src/backend/windows-x64/test-milestoneN.ps1
#   harness (implicit projects, O2), for Linux and for Windows, runs the Windows build here and adds an entry MilestoneN-O2 whose
#   expectation is that run's stdout with exit 0 and empty stderr (linux-x64 behaves as windows-x64; no second copy of the harnesses'
#   texts). A Windows run that exits non-zero or writes stderr fails, as a milestone harness's run does.
# It writes <Output>\expectations.json (the committed entries, then the milestones') and <Output>\builds.txt (every command, its
# output and exit code). Every build runs the native build's checks; any failed build or Windows run is recorded and the script exits 1
# after the rest, so no program is skipped silently. An existing <Output> is replaced only when it holds builds.txt (this script's).
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [string] $Output = (Join-Path $PSScriptRoot '../artifacts/linux-elf')
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "No compiler $compiler" }
$toolchain = Join-Path $repo 'toolchain'
$linux = 'x86_64-unknown-linux-musl'
$Output = [IO.Path]::GetFullPath($Output)
$work = Join-Path $repo 'temp/linux-elf'
$log = Join-Path $Output 'builds.txt'
if ((Test-Path -LiteralPath $Output) -and -not (Test-Path -LiteralPath $log -PathType Leaf)) {
    throw "-Output $Output exists and holds no builds.txt, so it is not this script's output: name another folder"
}
foreach ($folder in @($Output, $work)) {
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory -Path $folder | Out-Null
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($log, '', $utf8)
$programs = Join-Path $repo 'tests/linux-elf'
foreach ($source in Get-ChildItem -LiteralPath $programs -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($programs, $source.FullName).Replace('\', '/')
    if ($relative -cnotmatch '^[^/]+/([^/]+\.kimi(proj)?|native/[^/]+\.c)$') { continue }
    $target = Join-Path $work $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source.FullName -Destination $target
}
$failures = [Collections.Generic.List[string]]::new()

# Runs a file with arguments, its stdout and stderr captured as bytes; returns them with the exit code and appends all to builds.txt.
function Invoke-Logged([string] $File, [string[]] $Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $output = [IO.MemoryStream]::new()
    $errorOutput = [IO.MemoryStream]::new()
    $copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
    $errorCopy = $process.StandardError.BaseStream.CopyToAsync($errorOutput)
    if (-not $process.WaitForExit(300000)) { $process.Kill($true); $process.WaitForExit(); throw "Timed out: $File $Arguments" }
    $null = $copy.GetAwaiter().GetResult(), $errorCopy.GetAwaiter().GetResult()
    $result = @{ stdout = $output.ToArray(); stderr = $errorOutput.ToArray(); exitCode = $process.ExitCode }
    $text = "== $File $($Arguments -join ' ') (exit $($result.exitCode))`n$($utf8.GetString($result.stdout))$($utf8.GetString($result.stderr))`n"
    [IO.File]::AppendAllText($log, $text, $utf8)
    return $result
}

function Invoke-Kimi([string[]] $Arguments) { return Invoke-Logged (Get-Command dotnet).Source (@($compiler) + $Arguments) }

# The C libraries: each native/<stem>.c as native/<stem>.a.
$archiver = Join-Path $work 'llvm-ar.exe'
Copy-Item -LiteralPath (Join-Path $toolchain 'llvm-lib.exe') -Destination $archiver
foreach ($source in Get-ChildItem -LiteralPath $work -Recurse -Filter '*.c' | Where-Object { $_.Directory.Name -ceq 'native' }) {
    $object = [IO.Path]::ChangeExtension($source.FullName, '.o')
    $compiled = Invoke-Logged (Join-Path $toolchain 'clang.exe') @("--target=$linux", '-O2', '-fno-builtin', '-fno-stack-protector', '-c', $source.FullName, '-o', $object)
    $archived = if ($compiled.exitCode -eq 0) { Invoke-Logged $archiver @('rcs', [IO.Path]::ChangeExtension($source.FullName, '.a'), $object) }
    if ($compiled.exitCode -ne 0 -or $archived.exitCode -ne 0) { $failures.Add("C library $($source.FullName)") }
}

# The committed programs: each expectation file <Project>-<level> built from its project with that Optimization.
$entries = @(Get-Content -LiteralPath (Join-Path $programs 'expectations.json') -Raw | ConvertFrom-Json)
foreach ($file in @($entries.file | Sort-Object -Unique)) {
    if ($file -notmatch '^(.+)-(O0|O2)$') { throw "An expectation file names no project and level: $file" }
    $name, $level = $Matches[1], $Matches[2]
    $project = @(Get-ChildItem -LiteralPath $work -Recurse -Filter "$name.kimiproj")
    if ($project.Count -ne 1) { throw "tests/linux-elf holds $($project.Count) projects $name.kimiproj" }
    $path = $project[0].FullName
    $committed = Join-Path $programs ([IO.Path]::GetRelativePath($work, $path))
    [IO.File]::WriteAllText($path, [IO.File]::ReadAllText($committed) + "Optimization=`"$level`"`n", $utf8)
    $restored = Invoke-Kimi @('restore', '--Target', $linux, $path)
    $built = if ($restored.exitCode -eq 0) { Invoke-Kimi @('build', '--Target', $linux, '--locked', $path) }
    $elf = Join-Path $project[0].DirectoryName "bin/$linux/$name.$level"
    if ($restored.exitCode -ne 0 -or $built.exitCode -ne 0 -or -not (Test-Path -LiteralPath $elf -PathType Leaf)) { $failures.Add($file); continue }
    Copy-Item -LiteralPath $elf -Destination (Join-Path $Output $file)
}

# The milestones: built for both targets in a folder of their own, the Windows build run here as the Linux entry's expectation.
$milestones = Join-Path $work 'milestones'
foreach ($harness in Get-ChildItem -LiteralPath (Join-Path $repo 'src/backend/windows-x64') -Filter 'test-milestone*.ps1' | Sort-Object { [int]($_.BaseName.Substring(14)) }) {
    $name = 'Milestone' + $harness.BaseName.Substring(14)
    $source = Get-Item -LiteralPath (Join-Path $repo "tests/milestones/$name.kimi")
    $folder = New-Item -ItemType Directory -Path (Join-Path $milestones $name)
    $copy = Join-Path $folder.FullName $source.Name
    Copy-Item -LiteralPath $source.FullName -Destination $copy
    $windows = Invoke-Kimi @('build', '--Target', 'x86_64-pc-windows-msvc', $copy)
    $ran = if ($windows.exitCode -eq 0) { Invoke-Logged (Join-Path $folder.FullName "bin/x86_64-pc-windows-msvc/$name.O2.exe") @() }
    $built = Invoke-Kimi @('build', '--Target', $linux, $copy)
    $elf = Join-Path $folder.FullName "bin/$linux/$name.O2"
    $failed = $windows.exitCode -ne 0 -or $null -eq $ran -or $ran.exitCode -ne 0 -or $ran.stderr.Length -ne 0 -or $built.exitCode -ne 0
    if ($failed -or -not (Test-Path -LiteralPath $elf -PathType Leaf)) { $failures.Add($name); continue }
    Copy-Item -LiteralPath $elf -Destination (Join-Path $Output "$name-O2")
    $entries += [ordered]@{
        file = "$name-O2"; arguments = @(); stdin = ''; stdout = $utf8.GetString($ran.stdout); stderr = ''; exitCode = 0
        redirect = 'none'
    }
}

[IO.File]::WriteAllText((Join-Path $Output 'expectations.json'), (ConvertTo-Json -InputObject $entries -Depth 4), $utf8)
foreach ($failure in $failures) { Write-Host "FAILED $failure (builds.txt)" }
Write-Host "$($entries.Count) entries; $($failures.Count) failed builds or runs: $Output"
if ($failures.Count -ne 0) { exit 1 }
