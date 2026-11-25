# Runs Linux ELF executables under WSL and compares each run with its expectation (Tatara B7b7, design §3.11; development, and the
# file B7b9's CI reads). -Expectations (tests/linux-elf/expectations.json by default) is an array of entries: file (a name in
# -Directory), arguments (strings), stdin (text), stdout (the exact text), stderr (a .NET regular expression the whole stderr matches),
# exitCode, and redirect, one of none (stdout captured), closed-stdout (stdout closed) and closed-pipe (stdout a pipe whose read end
# is closed, so a write fails with EPIPE); any other redirect fails the script.
# Each file runs through `wsl.exe -d <Distribution> --exec /bin/sh -c`: from its drvfs path under /mnt, or, when WSL does not let that
# path execute, from a copy in a new folder under WSL's /tmp. The script prints WSL's kernel, writes results.json beside the files
# (each entry's expected and actual stdout, stderr and exit code) and exits 1 on any mismatch. It never installs, updates or
# reconfigures WSL.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [string] $Expectations = (Join-Path $PSScriptRoot '../tests/linux-elf/expectations.json'),
    [string] $Distribution = 'Ubuntu'
)
$ErrorActionPreference = 'Stop'
$Directory = [IO.Path]::GetFullPath($Directory)
$entries = @(Get-Content -LiteralPath $Expectations -Raw | ConvertFrom-Json)
$kernel = wsl.exe -d $Distribution --exec uname -r
if ($LASTEXITCODE -ne 0) { throw "WSL's $Distribution cannot run uname ($LASTEXITCODE)" }
Write-Host "WSL $Distribution kernel $kernel"

# A text as one sh word.
function ConvertTo-ShWord([string] $Text) { return "'" + $Text.Replace("'", "'\''") + "'" }

# A Windows path as WSL's drvfs mounts it.
function ConvertTo-WslPath([string] $Path) {
    if ($Path -notmatch '^([A-Za-z]):\\(.*)$') { throw "A path WSL does not mount: $Path" }
    return '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2].Replace('\', '/')
}

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
    $script = @(
        'd=$(mktemp -d)'
        "f=$(ConvertTo-ShWord (ConvertTo-WslPath $file))"
        'if [ ! -x "$f" ]; then cp "$f" "$d/elf" && chmod +x "$d/elf" && f="$d/elf"; fi'
        $setup
        "printf '%s' $(ConvertTo-ShWord ([string]$Entry.stdin)) | `"`$f`" $arguments $stdout 2>`"`$d/err`""
        's=$?'
        'exec 4>&-'
        'if [ -f "$d/out" ]; then cat "$d/out"; fi'
        'cat "$d/err" >&2'
        'rm -rf "$d"'
        'exit $s'
    ) -join '; '
    $start = [Diagnostics.ProcessStartInfo]::new('wsl.exe')
    foreach ($argument in @('-d', $Distribution, '--exec', '/bin/sh', '-c', $script)) { $start.ArgumentList.Add($argument) }
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
    $matched = $outputText -ceq [string]$Entry.stdout -and
        [regex]::IsMatch($errorText, '\A(?:' + [string]$Entry.stderr + ')\z') -and
        $process.ExitCode -eq [int]$Entry.exitCode
    return [ordered]@{
        file = $Entry.file
        redirect = $Entry.redirect
        matched = $matched
        exitCode = [ordered]@{ expected = [int]$Entry.exitCode; actual = $process.ExitCode }
        stdout = [ordered]@{ expected = [string]$Entry.stdout; actual = $outputText }
        stderr = [ordered]@{ pattern = [string]$Entry.stderr; actual = $errorText }
    }
}

$results = @($entries | ForEach-Object { Invoke-Elf $_ })
$report = [ordered]@{ distribution = $Distribution; kernel = "$kernel"; entries = $results }
[IO.File]::WriteAllText((Join-Path $Directory 'results.json'), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$failed = @($results | Where-Object { -not $_.matched })
foreach ($result in $failed) { Write-Host "MISMATCH $($result.file) ($($result.redirect))" }
Write-Host "$($results.Count - $failed.Count) of $($results.Count) matched"
if ($failed.Count -ne 0) { exit 1 }
