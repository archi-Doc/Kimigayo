# Durable Markdown records are the source of truth for IDs and completion gates.
# This does not prove that implementation or test evidence is semantically correct.
function Get-PlanTasks {
    $path = Join-Path $ProjectRoot 'IMPLEMENTATION_PLAN.md'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'IMPLEMENTATION_PLAN.md is missing.' }
    $tasks = @{}
    foreach ($line in [IO.File]::ReadLines($path)) {
        # A normal Markdown link in the first column is not a task checkbox.
        if ($line -match '^\|\s*\[[^\]]+\]\(') { continue }
        if ($line -notmatch '^\|\s*\[') { continue }
        if ($line -notmatch '^\|\s*\[([ xX])\]\s+([A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+)\s*\|') {
            throw "Invalid plan task row: $line"
        }
        $id = $Matches[2]
        if ($tasks.ContainsKey($id)) { throw "Duplicate plan task ID: $id" }
        $tasks[$id] = $Matches[1] -ne ' '
    }
    if ($tasks.Count -eq 0) { throw 'The plan must contain task rows: | [ ] TASK-01 | ... |' }
    return $tasks
}

function Get-RequiredFindings {
    $path = Join-Path $ProjectRoot 'AUDIT_FINDINGS.md'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'AUDIT_FINDINGS.md is missing; Plan must create it.' }
    $content = [IO.File]::ReadAllText($path)
    $ids = @{}
    $required = [Collections.Generic.List[string]]::new()
    foreach ($line in ($content -split '\r?\n')) {
        if ($line -notmatch '^\|\s*AF-') { continue }
        if ($line -notmatch '^\|\s*(AF-\d{4,})\s*\|\s*(blocking|required|advisory)\s*\|\s*(open|planned|resolved|dismissed|blocked)\s*\|') {
            throw "Invalid finding row: $line"
        }
        $id = $Matches[1]
        if ($ids.ContainsKey($id)) { throw "Duplicate finding ID: $id" }
        $ids[$id] = $true
        if ($Matches[2] -ne 'advisory' -and $Matches[3] -in @('open', 'planned', 'blocked')) {
            $required.Add($id)
        }
    }
    foreach ($match in [regex]::Matches($content, '(?<![\w-])AF-\d{4,}(?![\w-])')) {
        if (-not $ids.ContainsKey($match.Value)) { throw "Finding needs a canonical table row: $($match.Value)" }
    }
    return $required.ToArray()
}

function Assert-DurableResult($Result) {
    # A first Plan may be blocked before it can create these records.
    $planExists = Test-Path -LiteralPath (Join-Path $ProjectRoot 'IMPLEMENTATION_PLAN.md') -PathType Leaf
    if ($Result.status -ne 'blocked' -or $planExists -or $Result.task_ids.Count -gt 0) {
        $tasks = Get-PlanTasks
        foreach ($id in $Result.task_ids) {
            if (-not $tasks.ContainsKey($id)) { throw "Unknown plan task ID: $id" }
        }
        if ($Result.status -in @('complete', 'verified_complete')) {
            $pending = @($tasks.Keys | Where-Object { -not $tasks[$_] -and $_ -notlike 'OPTIONAL-*' } | Sort-Object)
            if ($pending.Count -gt 0) { throw "Completion has unfinished required tasks: $($pending -join ', ')" }
        }
    }
    $findingsExist = Test-Path -LiteralPath (Join-Path $ProjectRoot 'AUDIT_FINDINGS.md') -PathType Leaf
    if ($Result.status -ne 'blocked' -or $findingsExist -or $Result.finding_ids.Count -gt 0) {
        $required = @(Get-RequiredFindings)
        $reported = @($Result.finding_ids)
        $requiredText = ($required | Sort-Object) -join ','
        $reportedText = ($reported | Sort-Object) -join ','
        if ($reported.Count -ne @($reported | Sort-Object -Unique).Count -or
            $requiredText -cne $reportedText) {
            throw 'finding_ids must exactly match all unresolved blocking/required rows in AUDIT_FINDINGS.md.'
        }
    }
}
