# Code-rules SARIF validation and baseline reconciliation. The analyzer alone computes keys and observed values.
function Test-KimiProtectedDiagnostic([string] $Id) {
    return $Id -cmatch '^KR[0-9]{4}$' -or $Id -cin @('IDE0051', 'IDE0052', 'IDE0060')
}

function Read-KimiCodeRulesSarif([string] $Path, [string] $Root, [switch] $RequireComplete) {
    $sarif = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($sarif.version -cne '2.1.0' -or @($sarif.runs).Count -ne 1 -or $null -eq $sarif.runs[0].results) {
        throw 'Missing or malformed code-rules SARIF results.'
    }
    $results = @($sarif.runs[0].results)
    foreach ($result in $results) {
        if ($result.ruleId -ceq 'AD0001') { throw 'Analyzer exception in code-rules SARIF.' }
        if ((Test-KimiProtectedDiagnostic $result.ruleId) -and @($result.suppressions).Where({ $null -ne $_ }).Count -gt 0) {
            throw "Suppressed protected diagnostic: $($result.ruleId)."
        }
        if ($RequireComplete -and $result.ruleId -cin @('KR9003', 'KR9004', 'KR9005')) {
            throw "Invalid code-rule report: $($result.ruleId)."
        }
    }
    if ($RequireComplete) {
        $markers = @($results | Where-Object ruleId -CEQ 'KR9000')
        if ($markers.Count -ne 1) { throw 'A complete report needs exactly one KR9000 marker.' }
        $properties = $markers[0].properties.customProperties
        if ($properties.annotationKinds -cne 'exception,transitional') { throw 'Report lacks complete annotation inventory support.' }
        $treeCount = 0
        if (-not [int]::TryParse($properties.treeCount, [ref]$treeCount) -or $treeCount -le 0) { throw 'Report analyzed no source trees.' }
        foreach ($entry in @(@('config', 'CodeRules.globalconfig'), @('baseline', 'CodeRules.baseline.tsv'))) {
            $algorithm = $properties.($entry[0] + 'Algorithm')
            $checksum = $properties.($entry[0] + 'Checksum')
            if ($algorithm -cnotin @('Sha1', 'Sha256')) { throw 'Unknown report checksum algorithm.' }
            $current = (Get-FileHash -LiteralPath (Join-Path $Root "src/Kimi/$($entry[1])") -Algorithm $algorithm).Hash
            if (-not $checksum -or $checksum -ine $current) { throw "Report checksum mismatch: $($entry[1])." }
        }
    }
    return ,$results
}

function Get-KimiCodeRulesObservations($Results) {
    $rows = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($result in $Results) {
        if ($result.ruleId -cne 'KR9001') { continue }
        $p = $result.properties.customProperties
        $value = 0
        $limit = 0
        $exception = $p.kind -ceq 'exception'
        $transitional = $p.kind -ceq 'transitional'
        $annotation = $exception -or $transitional
        if ($p.rule -cnotmatch '^KR[123][0-9]{3}$' -or -not $p.scope -or -not $p.anchor -or
            -not [int]::TryParse($p.value, [ref]$value) -or $value -lt 0 -or ($value -eq 0 -and -not $annotation) -or
            ($p.kind -and -not $annotation) -or
            ($annotation -and ([string]::IsNullOrWhiteSpace($p.reason) -or $p.baselineAllowed -cne 'false')) -or
            ($exception -and ($p.rule -cnotin @('KR2006', 'KR2007') -or -not [int]::TryParse($p.limit, [ref]$limit) -or $limit -le 0 -or $p.stage)) -or
            ($transitional -and ($p.rule -cne 'KR1007' -or [string]::IsNullOrWhiteSpace($p.stage) -or $p.limit)) -or
            $p.path -cnotmatch '^src/Kimi/' -or $p.baselineAllowed -cnotin @('true', 'false')) {
            throw 'Malformed code-rule observation.'
        }
        $key = "$($p.scope)`t$($p.rule)`t$($p.anchor)"
        if ($rows.ContainsKey($key)) {
            $row = $rows[$key]
            if ($row.value -ne $value -or $row.kind -cne $p.kind -or $row.reason -cne $p.reason -or $row.limit -ne $limit -or $row.stage -cne $p.stage) { throw 'Inconsistent values for one report key.' }
            $row.allowed = $row.allowed -and $p.baselineAllowed -ceq 'true'
            if ([StringComparer]::Ordinal.Compare($p.path, $row.path) -lt 0) { $row.path = $p.path }
            $null = $row.paths.Add($p.path)
        }
        else {
            $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $null = $paths.Add($p.path)
            $rows.Add($key, [pscustomobject][ordered]@{ rule = $p.rule; scope = $p.scope; anchor = $p.anchor; value = $value; path = $p.path; paths = $paths; allowed = $p.baselineAllowed -ceq 'true'; kind = $p.kind; reason = $p.reason; limit = $limit; stage = $p.stage })
        }
    }
    return ,$rows
}

function Get-KimiCodeRulesBaseline([string] $Path) {
    $rows = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if (-not $line -or $line.StartsWith('#')) { continue }
        $parts = $line.Split("`t")
        # This file was already validated by the completed analyzer report.
        if ($parts.Count -ne 5) { throw 'Malformed baseline row.' }
        $rows.Add("$($parts[1])`t$($parts[0])`t$($parts[2])",
            [pscustomobject][ordered]@{ rule = $parts[0]; scope = $parts[1]; anchor = $parts[2]; value = [int]$parts[3]; path = $parts[4] })
    }
    return ,$rows
}

function Update-KimiCodeRulesBaseline($Baseline, $Observed, [string] $Mode, [string[]] $Rule, [string] $Path) {
    if ($Mode -ceq 'Prune') {
        foreach ($key in @($Baseline.Keys)) {
            if (-not $Observed.ContainsKey($key) -or -not $Observed[$key].allowed) { $null = $Baseline.Remove($key); continue }
            $Baseline[$key].value = [Math]::Min($Baseline[$key].value, $Observed[$key].value)
            $Baseline[$key].path = $Observed[$key].path
        }
    }
    else {
        $matched = 0
        foreach ($pair in $Observed.GetEnumerator()) {
            $row = $pair.Value
            if ($row.kind) { continue }
            if ($Rule.Count -gt 0 -and $row.rule -cnotin $Rule) { continue }
            if ($Mode -ceq 'Accept' -and -not @($row.paths).Where({ $_ -ceq $Path -or ($Path.EndsWith('/') -and $_.StartsWith($Path, [StringComparison]::Ordinal)) }).Count) { continue }
            if (-not $row.allowed) {
                if ($Mode -ceq 'Accept') { throw "Baseline is forbidden for $($row.rule) at $($row.path)." }
                continue
            }
            if ($Mode -ceq 'Initialize' -and $Baseline.ContainsKey($pair.Key)) { throw 'Initialize cannot replace an existing baseline key; use Prune or an approved Accept.' }
            $Baseline[$pair.Key] = $row
            $matched++
        }
        if ($Mode -ceq 'Accept' -and $matched -eq 0) { throw 'No observations match the requested rule and path.' }
    }
    return ,$Baseline
}

function Write-KimiCodeRulesBaseline([string] $Path, $Rows) {
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("# Rule`tScope`tAnchor`tValue`tPath")
    foreach ($row in $Rows.Values) { $lines.Add("$($row.rule)`t$($row.scope)`t$($row.anchor)`t$($row.value)`t$($row.path)") }
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
