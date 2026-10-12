# Approval policy consumes canonical baseline keys and the analyzer's complete annotation reports.
# No attribute parser, historical report cache or source-change allowlist is maintained here.
function Get-KimiCodeRulesApprovalTrailer([string] $Root, [string] $Commit) {
    $text = Invoke-KimiVerificationGit $Root @('show', '-s', '--format=%(trailers:key=Code-Rules-Approved,unfold)', $Commit)
    $trailers = @($text.Split([char]10, [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
    foreach ($trailer in $trailers) {
        $date = [datetime]::MinValue
        if ($trailer -cnotmatch '^Code-Rules-Approved:[ \t]+(.+?) ([0-9]{4}-[0-9]{2}-[0-9]{2}):[ \t]+(\S.*)$' -or
            -not [datetime]::TryParseExact($Matches[2], 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None, [ref]$date)) {
            throw 'Malformed Code-Rules-Approved trailer; expected <approver> <yyyy-MM-dd>: <reason>.'
        }
    }
    return ,$trailers
}

function Assert-KimiCodeRulesBaselineApproval($Before, $After) {
    foreach ($pair in $After.GetEnumerator()) {
        if (-not $Before.ContainsKey($pair.Key) -or $pair.Value.value -gt $Before[$pair.Key].value) {
            throw "Code-Rules-Approved trailer required: baseline added or increased ($($pair.Key))."
        }
    }
}

function Assert-KimiCodeRulesAnnotationApproval($Before, $After) {
    foreach ($pair in $After.GetEnumerator()) {
        $row = $pair.Value
        $old = if ($Before.ContainsKey($pair.Key)) { $Before[$pair.Key] } else { $null }
        if (($row.kind -ceq 'exception' -and ($null -eq $old -or $old.kind -cne 'exception' -or $row.limit -gt $old.limit)) -or
            ($row.kind -ceq 'transitional' -and $null -ne $old -and $old.kind -ceq 'transitional' -and $row.value -gt $old.value)) {
            throw "Code-Rules-Approved trailer required: annotation added or relaxed ($($pair.Key))."
        }
    }
}

function Get-KimiCodeRulesCommitReport([string] $Root, [string] $Commit, [string] $Evidence) {
    $temporaryRoot = [IO.Path]::GetFullPath((Join-Path $Root 'temp/code-rules-approval'))
    $snapshot = Join-Path $temporaryRoot ([Guid]::NewGuid().ToString('N'))
    $created = $false
    try {
        $null = Invoke-KimiVerificationGit $Root @('worktree', 'add', '--detach', $snapshot, $Commit)
        $created = $true
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Evidence))
        Push-Location $snapshot
        try {
            & dotnet restore 'src/Kimi/Kimi.csproj' --disable-parallel --disable-build-servers *> ($Evidence + '-restore.log')
            if ($LASTEXITCODE -ne 0) { throw "Approval report restore failed: $Evidence-restore.log" }
            $result = Invoke-KimiCodeRulesReport $snapshot $Evidence
        }
        finally { Pop-Location }
        $null = Assert-KimiVerificationCommit $snapshot $result.inputs $Commit
        Assert-KimiVerificationInputsStable $result.inputs (Get-KimiVerificationInputs $snapshot)
        return ,$result.observations
    }
    finally {
        if ($created) {
            # Evidence is outside the disposable checkout, including failures. Keep the two checksum inputs as well.
            $retained = Join-Path $Evidence 'src/Kimi'
            $null = [IO.Directory]::CreateDirectory($retained)
            foreach ($name in @('CodeRules.globalconfig', 'CodeRules.baseline.tsv')) {
                $path = Join-Path $snapshot "src/Kimi/$name"
                if ([IO.File]::Exists($path)) { Copy-Item -LiteralPath $path -Destination (Join-Path $retained $name) }
            }
            $prefix = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not [IO.Path]::GetFullPath($snapshot).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
                [IO.Path]::GetFileName($snapshot) -cnotmatch '^[0-9a-f]{32}$') { throw 'Unsafe approval snapshot cleanup target.' }
            $null = Invoke-KimiVerificationGit $Root @('worktree', 'remove', '--force', $snapshot)
        }
    }
}

function Assert-KimiCodeRulesCommitApproval([string] $Root, [string] $Commit, [string] $Evidence) {
    $trailers = Get-KimiCodeRulesApprovalTrailer $Root $Commit
    if ($trailers.Count) { return [pscustomobject]@{ result = 'approved'; trailers = $trailers } }
    $parent = (Invoke-KimiVerificationGit $Root @('rev-parse', '--verify', "$Commit^")).Trim()
    $config = 'src/Kimi/CodeRules.globalconfig'
    $beforeConfig = Invoke-KimiVerificationGit $Root @('rev-parse', "${parent}:$config")
    $afterConfig = Invoke-KimiVerificationGit $Root @('rev-parse', "${Commit}:$config")
    if ($beforeConfig -cne $afterConfig) { throw 'Code-Rules-Approved trailer required: CodeRules.globalconfig changed.' }
    $audit = Join-Path $Evidence ('code-rules-approval-' + [Guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($audit)
    $baseline = 'src/Kimi/CodeRules.baseline.tsv'
    $beforePath = Join-Path $audit 'parent-baseline.tsv'
    $afterPath = Join-Path $audit 'commit-baseline.tsv'
    [IO.File]::WriteAllText($beforePath, (Invoke-KimiVerificationGit $Root @('show', "${parent}:$baseline")))
    [IO.File]::WriteAllText($afterPath, (Invoke-KimiVerificationGit $Root @('show', "${Commit}:$baseline")))
    Assert-KimiCodeRulesBaselineApproval (Get-KimiCodeRulesBaseline $beforePath) (Get-KimiCodeRulesBaseline $afterPath)
    $before = Get-KimiCodeRulesCommitReport $Root $parent (Join-Path $audit 'parent')
    $after = Get-KimiCodeRulesCommitReport $Root $Commit (Join-Path $audit 'commit')
    Assert-KimiCodeRulesAnnotationApproval $before $after
    return [pscustomobject]@{ result = 'not-required'; parent = $parent; reports = $audit }
}
