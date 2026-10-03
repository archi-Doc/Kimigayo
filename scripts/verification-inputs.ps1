# Source identities for Verify. Git supplies normalized blob identities; SHA-256 identifies the actual read bytes.
function Invoke-KimiVerificationGit([string] $Root, [string[]] $Arguments, [string] $InputText = '') {
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.WorkingDirectory = $Root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($InputText)
        $process.StandardInput.Close()
        $process.WaitForExit()
        $result = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Git verification input query failed: $errorText" }
        return $result
    }
    finally { $process.Dispose() }
}

function Test-KimiVerificationInput([string] $Path) {
    # Proposals are not compiler inputs. These two bookkeeping documents change after Session verification.
    return -not ($Path.StartsWith('draft/', [StringComparison]::Ordinal) -or
        $Path -ceq 'docs/dev/PLAN.md' -or $Path -ceq 'docs/dev/PLAN_HISTORY.md')
}

function Get-KimiVerificationFileHash([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}

function Get-KimiVerificationInputs([string] $Root) {
    $listed = Invoke-KimiVerificationGit $Root @('ls-files', '-z', '--cached', '--others', '--exclude-standard')
    $paths = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $listed.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
        if ((Test-KimiVerificationInput $path) -and [IO.File]::Exists((Join-Path $Root $path))) { $null = $paths.Add($path) }
    }
    $entries = [Collections.Generic.List[object]]::new()
    $stdin = [Text.StringBuilder]::new()
    foreach ($path in $paths) {
        if ($path.Contains("`n") -or $path.Contains("`r") -or $path.StartsWith('"')) { throw "Unsupported verification input path: $path" }
        $full = Join-Path $Root $path
        if (([IO.File]::GetAttributes($full) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Verification inputs must be regular files: $path" }
        $entries.Add([ordered]@{ path = $path; sha256 = Get-KimiVerificationFileHash $full; gitBlob = '' })
        $null = $stdin.Append($path).Append("`n")
    }
    if ($entries.Count -gt 0) {
        $blobs = (Invoke-KimiVerificationGit $Root @('hash-object', '--stdin-paths') $stdin.ToString()).Split("`n", [StringSplitOptions]::RemoveEmptyEntries)
        if ($blobs.Count -ne $entries.Count) { throw 'Git did not hash every verification input.' }
        for ($i = 0; $i -lt $entries.Count; $i++) { $entries[$i].gitBlob = $blobs[$i].Trim() }
    }
    # --no-restore consumes these ignored NuGet inputs. Record them separately from commit-owned sources.
    $dependencies = [Collections.Generic.List[object]]::new()
    foreach ($path in $paths) {
        if (-not $path.EndsWith('.csproj', [StringComparison]::Ordinal)) { continue }
        foreach ($name in @('project.assets.json', ([IO.Path]::GetFileName($path) + '.nuget.g.props'), ([IO.Path]::GetFileName($path) + '.nuget.g.targets'))) {
            $relative = ([IO.Path]::GetDirectoryName($path) + '/obj/' + $name).Replace('\', '/')
            $full = Join-Path $Root $relative
            $dependencies.Add([ordered]@{ path = $relative; sha256 = if ([IO.File]::Exists($full)) { Get-KimiVerificationFileHash $full } else { $null } })
        }
    }
    return [ordered]@{ schema = 1; policy = 'nonignored-and-tracked-except-draft-and-plan-bookkeeping'; files = $entries.ToArray(); dependencies = $dependencies.ToArray() }
}

function Assert-KimiVerificationInputsStable($Before, $After) {
    foreach ($group in @('files', 'dependencies')) {
        $left = $Before.$group
        $right = $After.$group
        if ($left.Count -ne $right.Count) { throw "Verification $group changed: input set differs." }
        for ($i = 0; $i -lt $left.Count; $i++) {
            if ($left[$i].path -cne $right[$i].path -or $left[$i].sha256 -cne $right[$i].sha256 -or
                ($group -eq 'files' -and $left[$i].gitBlob -cne $right[$i].gitBlob)) {
                throw "Verification $group changed: $($left[$i].path)."
            }
        }
    }
}

function Assert-KimiVerificationCommit([string] $Root, $Manifest, [string] $Commit = 'HEAD') {
    if ($Manifest.schema -ne 1 -or $Manifest.policy -cne 'nonignored-and-tracked-except-draft-and-plan-bookkeeping') { throw 'Unknown verification input policy.' }
    $resolved = (Invoke-KimiVerificationGit $Root @('rev-parse', '--verify', '--end-of-options', "$Commit^{commit}")).Trim()
    $tree = Invoke-KimiVerificationGit $Root @('ls-tree', '-r', '-z', $resolved)
    $expected = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($file in $Manifest.files) { $expected.Add($file.path, $file.gitBlob) }
    foreach ($entry in $tree.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($entry -notmatch '^([0-9]+) ([^ ]+) ([0-9a-f]+)\t([\s\S]+)$') { throw 'Malformed Git tree record.' }
        $kind = $Matches[2]
        $blob = $Matches[3]
        $path = $Matches[4]
        if (-not (Test-KimiVerificationInput $path)) { continue }
        if ($kind -ne 'blob' -or -not $expected.ContainsKey($path) -or $expected[$path] -cne $blob) { throw "Commit does not match verified input: $path" }
        $null = $expected.Remove($path)
    }
    if ($expected.Count -ne 0) { throw "Commit omits verified input: $($expected.Keys -join ', ')" }
    return $resolved
}
