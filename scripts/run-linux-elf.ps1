# Runs Linux ELF executables on a Linux kernel and compares each run with its expectation (Tatara B7b7, design §3.11; B7b9: the
# launcher by platform and the measurements, CI's linux-elf job). -Expectations (tests/linux-elf/expectations.json by default) is an
# array of entries: file (a name in -Directory), arguments (strings), stdin (text), stdout (the exact text), stderr (a .NET regular
# expression the whole stderr matches), exitCode, redirect, one of none (stdout captured), closed-stdout (stdout closed) and
# closed-pipe (stdout a pipe whose read end is closed, so a write fails with EPIPE; any other redirect fails the script), and
# optionally measure (true: design §8's records, not gates).
# The launcher follows the platform: on Linux each file runs through `/bin/sh -c` after `chmod +x`; on Windows through `wsl.exe -d
# <Distribution> --exec /bin/sh -c`, from its drvfs path under /mnt or, when WSL does not let that path execute, from a copy under
# WSL's /tmp. Every entry runs in every invocation (no run reuse). The script prints the kernel (`uname -r`) first, writes results.json
# beside the files (the kernel; each entry's expected and actual stdout, stderr and exit code, and a measured entry's size, wall time
# and peak RSS from `/usr/bin/time -f '%e %M'` where it exists, else "not measured") and exits 1 on any mismatch. It never installs,
# updates or reconfigures WSL.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [string] $Expectations = (Join-Path $PSScriptRoot '../tests/linux-elf/expectations.json'),
    [string] $Distribution = 'Ubuntu'
)
$ErrorActionPreference = 'Stop'
$Directory = [IO.Path]::GetFullPath($Directory)
$entries = @(Get-Content -LiteralPath $Expectations -Raw | ConvertFrom-Json)

# A text as one sh word.
function ConvertTo-ShWord([string] $Text) { return "'" + $Text.Replace("'", "'\''") + "'" }

# The launcher and a file's path as the launched shell sees it: the host's own on Linux, WSL's drvfs mount on Windows.
if ($IsLinux) {
    $launcher = @('/bin/sh', '-c')
    $launcherName = '/bin/sh'
    function ConvertTo-ShellPath([string] $Path) { return $Path }
    foreach ($file in @($entries.file | Sort-Object -Unique)) { chmod +x (Join-Path $Directory $file) }
}
else {
    $launcher = @('wsl.exe', '-d', $Distribution, '--exec', '/bin/sh', '-c')
    $launcherName = "wsl.exe -d $Distribution"
    function ConvertTo-ShellPath([string] $Path) {
        if ($Path -notmatch '^([A-Za-z]):\\(.*)$') { throw "A path WSL does not mount: $Path" }
        return '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2].Replace('\', '/')
    }
}

# Runs an sh script through the launcher; returns its stdout, stderr and exit code.
function Invoke-Shell([string] $Script) {
    $start = [Diagnostics.ProcessStartInfo]::new($launcher[0])
    foreach ($argument in @($launcher | Select-Object -Skip 1) + $Script) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $process = [Diagnostics.Process]::Start($start)
    $errorText = $process.StandardError.ReadToEndAsync()
    $outputText = $process.StandardOutput.ReadToEnd()
    $errorText = $errorText.GetAwaiter().GetResult()
    $process.WaitForExit()
    return @{ stdout = $outputText; stderr = $errorText; exitCode = $process.ExitCode }
}

$kernel = Invoke-Shell 'uname -r'
if ($kernel.exitCode -ne 0) { throw "The launcher cannot run uname ($($kernel.exitCode)): $($launcher -join ' ')" }
$kernel = $kernel.stdout.Trim()
Write-Host "Linux kernel $kernel ($launcherName)"

# The program's stdout per redirect; closed-pipe's FIFO is opened read-write on 3 (so the write-only open on 4 does not block), then 3
# is closed, which leaves the pipe with no reader.
$redirects = @{
    'none' = @(':', '>"$d/out"')
    'closed-stdout' = @(':', '>&-')
    'closed-pipe' = @('mkfifo "$d/pipe"; exec 3<>"$d/pipe"; exec 4>"$d/pipe"; exec 3<&-', '>&4')
}

function Invoke-Elf($Entry) {
    if (-not $redirects.ContainsKey([string]$Entry.redirect)) {
        throw "$($Entry.file): redirect '$($Entry.redirect)' is not none, closed-stdout or closed-pipe"
    }

    $file = Join-Path $Directory $Entry.file
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "No file $file" }
    $setup, $stdout = $redirects[[string]$Entry.redirect]
    $arguments = @($Entry.arguments | ForEach-Object { ConvertTo-ShWord $_ }) -join ' '
    $prefix = @(
        'd=$(mktemp -d)'
        "f=$(ConvertTo-ShWord (ConvertTo-ShellPath $file))"
        'if [ ! -x "$f" ]; then cp "$f" "$d/elf" && chmod +x "$d/elf" && f="$d/elf"; fi'
    )
    $run = Invoke-Shell (@($prefix) + @(
        $setup
        "printf '%s' $(ConvertTo-ShWord ([string]$Entry.stdin)) | `"`$f`" $arguments $stdout 2>`"`$d/err`""
        's=$?'
        'exec 4>&-'
        'if [ -f "$d/out" ]; then cat "$d/out"; fi'
        'cat "$d/err" >&2'
        'rm -rf "$d"'
        'exit $s'
    ) -join '; ')
    $result = [ordered]@{
        file = $Entry.file
        redirect = $Entry.redirect
        matched = $run.stdout -ceq [string]$Entry.stdout -and
            [regex]::IsMatch($run.stderr, '\A(?:' + [string]$Entry.stderr + ')\z') -and
            $run.exitCode -eq [int]$Entry.exitCode
        exitCode = [ordered]@{ expected = [int]$Entry.exitCode; actual = $run.exitCode }
        stdout = [ordered]@{ expected = [string]$Entry.stdout; actual = $run.stdout }
        stderr = [ordered]@{ pattern = [string]$Entry.stderr; actual = $run.stderr }
    }
    if ($Entry.measure -eq $true) {
        # A run of its own, its streams discarded: GNU time's elapsed seconds and peak RSS (KiB) into a file of the temporary folder.
        $time = Invoke-Shell (@($prefix) + @(
            'if [ -x /usr/bin/time ]; then printf "%s" "" | /usr/bin/time -f "%e %M" -o "$d/time" "$f" >/dev/null 2>&1; cat "$d/time"; fi'
            'rm -rf "$d"'
        ) -join '; ')
        $fields = $time.stdout.Trim() -split ' '
        $result.measurement = if ($fields.Count -eq 2) {
            [ordered]@{ bytes = (Get-Item -LiteralPath $file).Length; seconds = [double]$fields[0]; peakRssKiB = [long]$fields[1] }
        }
        else { [ordered]@{ bytes = (Get-Item -LiteralPath $file).Length; time = 'not measured' } }
    }
    return $result
}

$results = @($entries | ForEach-Object { Invoke-Elf $_ })
$report = [ordered]@{ launcher = $launcherName; kernel = $kernel; entries = $results }
[IO.File]::WriteAllText((Join-Path $Directory 'results.json'), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$failed = @($results | Where-Object { -not $_.matched })
foreach ($result in $failed) { Write-Host "MISMATCH $($result.file) ($($result.redirect))" }
foreach ($result in @($results | Where-Object { $_.Contains('measurement') })) { Write-Host "MEASURED $($result.file): $($result.measurement | ConvertTo-Json -Compress)" }
Write-Host "$($results.Count - $failed.Count) of $($results.Count) matched"
if ($failed.Count -ne 0) { exit 1 }
