# Boot each x86_64-unknown-none program of an expectations file under QEMU and Limine and judge its serial output and its end
# (Tatara B9h, design 3.9). A development step, never a test and no CI job: QEMU and Limine are downloads (design decision 34).
# Inputs: the expectations file (-Expectations: the tools' SHA-256 pins, the timeout and the rows, each with its sources relative to
# the file's folder, its level, its expected serial text and its end), the built src/Kimi/bin/<Configuration>/net10.0/Kimi.dll (run
# with dotnet; this script builds no compiler), QEMU for Windows (-QemuRoot) and Limine's binary release (-LimineRoot); relative
# paths resolve against the repository root.
# Outputs: artifacts/verify/none-qemu/<yyyyMMdd-HHmmss>/ with tools.json, results.json and one folder per row (project, esp, vars.fd,
# build.txt, serial.bin, qemu-stderr.txt, result.json), one console line per row, exit 1 when any row fails.
# A pinned file that is missing or whose SHA-256 differs throws before any build; nothing is downloaded and nothing falls back.
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [string] $QemuRoot = 'artifacts/qemu',
    [string] $LimineRoot = 'artifacts/downloads/limine',
    [string] $Expectations = 'scripts/run-none-qemu/expectations.json'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'src/backend/windows-x64/toolchain.ps1')
$QemuRoot = [IO.Path]::GetFullPath($QemuRoot, $repo)
$LimineRoot = [IO.Path]::GetFullPath($LimineRoot, $repo)
$Expectations = [IO.Path]::GetFullPath($Expectations, $repo)
$expected = Get-Content -LiteralPath $Expectations -Raw | ConvertFrom-Json
$programs = Split-Path -Parent $Expectations
$compiler = Join-Path $repo "src/Kimi/bin/$Configuration/net10.0/Kimi.dll"
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "The compiler is not built: $compiler" }
$utf8 = [Text.UTF8Encoding]::new($false)

# One child process (the CLI's build, QEMU): standard input empty, both output streams read asynchronously as bytes (test-cli's
# CopyToAsync form), the process and its children killed at the timeout.
function Invoke-Child([string] $FileName, [string[]] $Arguments, [string] $Directory, [int] $TimeoutSeconds) {
    $start = [Diagnostics.ProcessStartInfo]::new($FileName)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $Directory
    $start.RedirectStandardInput = $start.RedirectStandardOutput = $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $process.StandardInput.Close()
    $stdout = [IO.MemoryStream]::new()
    $stderr = [IO.MemoryStream]::new()
    $copies = @($process.StandardOutput.BaseStream.CopyToAsync($stdout), $process.StandardError.BaseStream.CopyToAsync($stderr))
    $exited = $process.WaitForExit($TimeoutSeconds * 1000)
    $seconds = [Math]::Round($clock.Elapsed.TotalSeconds, 1)
    if (-not $exited) { $process.Kill($true); $process.WaitForExit() }
    foreach ($copy in $copies) { $null = $copy.GetAwaiter().GetResult() }
    $result = @{ exited = $exited; status = $(if ($exited) { $process.ExitCode } else { $null }); stdout = $stdout.ToArray(); stderr = $stderr.ToArray(); seconds = $seconds }
    $process.Dispose()
    return $result
}

# 1. The pins: every file this script reads from the downloads, by its path under its root.
$read = [ordered]@{}
foreach ($file in @(('qemu', $QemuRoot, 'qemu-system-x86_64.exe'), ('qemu', $QemuRoot, 'share/edk2-x86_64-code.fd'),
        ('qemu', $QemuRoot, 'share/edk2-i386-vars.fd'), ('limine', $LimineRoot, 'BOOTX64.EFI'))) {
    $tool, $root, $name = $file
    $path = Join-Path $root $name
    $pin = $expected.tools.$tool.files.$name
    $hash = if (Test-Path -LiteralPath $path -PathType Leaf) { Get-KimiSha256 $path } else { 'missing' }
    if ($hash -cne $pin) { throw "$path`: SHA-256 $hash; $Expectations pins '$pin'" }
    $read[$name] = $path
}
$qemu = $read['qemu-system-x86_64.exe']

# 2. The run folder (verification evidence) and the tools' record.
$run = Join-Path $repo ('artifacts/verify/none-qemu/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
$version = Invoke-Child $qemu @('--version') $run 30
@{ files = @($read.GetEnumerator() | ForEach-Object { @{ path = $_.Value; sha256 = Get-KimiSha256 $_.Value } })
    qemuVersion = ($utf8.GetString($version.stdout) -split "`r?`n")[0] } | ConvertTo-Json -Depth 3 |
    Set-Content -LiteralPath (Join-Path $run 'tools.json') -Encoding utf8

# 3. Each row in the file's order: its project built, the EFI system partition Limine reads, QEMU, the judge.
$results = @(foreach ($row in $expected.rows) {
        $folder = Join-Path $run ($row.row -replace ' ', '-')
        $project = Join-Path $folder 'project'
        New-Item -ItemType Directory -Path $project | Out-Null
        "Targets=`n  `"x86_64-unknown-none`"`nOutputKind=`"Application`"`nOptimization=`"$($row.level)`"`n" |
            Set-Content -LiteralPath (Join-Path $project 'Kernel.kimiproj') -Encoding utf8
        foreach ($source in $row.sources) { Copy-Item -LiteralPath (Join-Path $programs $source) -Destination $project }
        $build = Invoke-Child (Get-Command dotnet).Source @($compiler, 'build', (Join-Path $project 'Kernel.kimiproj')) $folder 60
        [IO.File]::WriteAllBytes((Join-Path $folder 'build.txt'), [byte[]] ($build.stdout + $build.stderr))
        $failures = @()
        $observed = $seconds = $difference = $null
        if (-not $build.exited -or $build.status -ne 0) { $failures += "build $(if ($build.exited) { "exit $($build.status)" } else { 'timed out' })" }
        else {
            $kernel = "Kernel.$($row.level)"
            $esp = Join-Path $folder 'esp'
            New-Item -ItemType Directory -Path (Join-Path $esp 'EFI/BOOT'), (Join-Path $esp 'boot/limine') | Out-Null
            Copy-Item -LiteralPath $read['BOOTX64.EFI'] -Destination (Join-Path $esp 'EFI/BOOT/BOOTX64.EFI')
            [IO.File]::WriteAllText((Join-Path $esp 'boot/limine/limine.conf'), "timeout: 0`n`n/Kernel`n    protocol: limine`n    path: boot():/$kernel`n", $utf8)
            Copy-Item -LiteralPath (Join-Path $project "bin/x86_64-unknown-none/$kernel") -Destination (Join-Path $esp $kernel)
            # The firmware writes its variables: each row boots on its own copy of the package's template.
            Copy-Item -LiteralPath $read['share/edk2-i386-vars.fd'] -Destination (Join-Path $folder 'vars.fd')
            # The esp folder is a read-only virtio disk: q35's default SATA disk needs a writable node, and nothing writes the image.
            $machine = Invoke-Child $qemu @('-machine', 'q35', '-m', '256M', '-no-reboot', '-display', 'none', '-monitor', 'none', '-nic', 'none',
                '-serial', 'stdio', '-device', 'isa-debug-exit,iobase=0xf4,iosize=0x04',
                '-drive', "if=pflash,format=raw,unit=0,readonly=on,file=$($read['share/edk2-x86_64-code.fd'])",
                '-drive', 'if=pflash,format=raw,unit=1,file=vars.fd', '-drive', 'if=virtio,format=raw,file=fat:esp,readonly=on') $folder $expected.timeoutSeconds
            [IO.File]::WriteAllBytes((Join-Path $folder 'serial.bin'), $machine.stdout)
            [IO.File]::WriteAllBytes((Join-Path $folder 'qemu-stderr.txt'), $machine.stderr)
            $observed = if ($machine.exited) { $machine.status } else { 'running at timeout' }
            $seconds = $machine.seconds

            # The judge: the serial bytes end with the row's text (the firmware's console text may precede it, nothing follows it).
            # The first difference is the offset in serial.bin where the tail aligned with the text first differs, 0 when shorter.
            $text = $utf8.GetBytes($row.text)
            $offset = $machine.stdout.Length - $text.Length
            $difference = if ($offset -lt 0) { 0 } else { $null }
            for ($i = 0; $null -eq $difference -and $i -lt $text.Length; $i++) {
                if ($machine.stdout[$offset + $i] -ne $text[$i]) { $difference = $offset + $i }
            }
            if ($null -ne $difference) { $failures += "text differs at serial byte $difference" }
            # The end: { "exit": s } is QEMU's status 2 * s + 1 through isa-debug-exit before the timeout; "halts" is QEMU still
            # running at the timeout (the runtime's __kimi_halt); status 0 is a reset (a triple fault under -no-reboot).
            if ($row.end -is [string]) {
                if ($row.end -cne 'halts') { throw "$($row.row): the end '$($row.end)' is neither { `"exit`": s } nor `"halts`"" }
                if ($machine.exited) { $failures += "exited with status $($machine.status) before the timeout, expected a halt" }
            }
            else {
                $status = 2 * [int] $row.end.exit + 1
                if (-not $machine.exited) { $failures += "running at the timeout, expected status $status" }
                elseif ($machine.status -eq 0) { $failures += 'status 0 (a reset)' }
                elseif ($machine.status -ne $status) { $failures += "status $($machine.status), expected $status" }
            }
        }
        $result = [ordered]@{ row = $row.row; end = $row.end; observed = $observed; seconds = $seconds
            verdict = $(if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL: ' + ($failures -join '; ') }); firstDifference = $difference }
        $result | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $folder 'result.json') -Encoding utf8
        Write-Host "$($row.row): $($result.verdict) (observed $observed, $seconds s)"
        $result
    })

# 4. The end.
ConvertTo-Json -InputObject $results -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'results.json') -Encoding utf8
Write-Host "Run folder: $run"
if (@($results | Where-Object verdict -cne 'PASS').Count -ne 0) { exit 1 }
exit 0
