#requires -Version 7.4
# Exercises the real runner with a local fake CLI. No Codex/API/compiler is invoked.
[CmdletBinding()]
param([switch]$IncludeTimeout, [switch]$Smoke)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('kimi-loop-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$runnerPath = Join-Path $PSScriptRoot 'codex-loop.ps1'
$pwshPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$fakePath = Join-Path $testRoot 'fake codex.ps1'
$fakeText = @'
$ErrorActionPreference = 'Stop'
if ('--approve-for-me' -in $args -and ('--sandbox' -in $args -or '-s' -in $args)) {
    [Console]::Error.WriteLine("error: --approve-for-me cannot be used with --sandbox")
    exit 2
}
$promptText = ($input | Out-String)
$rootPath = $args[[Array]::IndexOf($args, '-C') + 1]
$outputPath = $args[[Array]::IndexOf($args, '-o') + 1]
$config = Get-Content (Join-Path $rootPath '.codex-loop/mock.json') -Raw | ConvertFrom-Json
$stage = [regex]::Match($promptText, '(?m)^Stage: (.+)\r?$').Groups[1].Value.Trim()
if (-not $promptText.Contains('doc/ 以下は正式な仕様ではなく、検索・読込・参照・仕様統合・実装/監査の根拠から除外する。') -or
    -not $promptText.Contains('その記述をdoc/参照の例外にしない。') -or
    -not $promptText.Contains('未コミット')) { exit 21 }
foreach ($flag in @('exec', '--ephemeral', '--approve-for-me', '--output-schema', '-')) {
    if ($flag -notin $args) { throw "Missing invocation flag: $flag" }
}
if (-not $promptText.Contains('Hard deadline (UTC):') -or -not $promptText.Contains('verification-guide.md')) { exit 22 }
$callPath = Join-Path $rootPath '.codex-loop/calls.jsonl'
$priorCalls = if (Test-Path $callPath) { @(Get-Content $callPath | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
$occurrence = @($priorCalls | Where-Object stage -eq $stage).Count + 1
@{ stage = $stage; occurrence = $occurrence } | ConvertTo-Json -Compress | Add-Content $callPath
$mode = $config.mode
if ($mode -eq 'timeout') {
    $childInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })))
    $childInfo.UseShellExecute = $false
    $childInfo.CreateNoWindow = $true
    foreach ($arg in @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 100')) { $childInfo.ArgumentList.Add($arg) }
    $child = [Diagnostics.Process]::Start($childInfo)
    Set-Content (Join-Path $rootPath '.codex-loop/child.pid') $child.Id
    Start-Sleep -Seconds 75
}
if ($mode -eq 'process-error') {
    Add-Content (Join-Path $rootPath 'STATUS.md') 'partial work before failure'
    [Console]::Error.WriteLine('fake failure'); exit 7
}
if ($mode -eq 'missing-result') { exit 0 }
if ($mode -eq 'malformed') { Set-Content $outputPath '{broken'; exit 0 }
$status = switch ($stage) {
    'plan' { 'ready' }
    'plan-audit' { 'approved' }
    'implementation' { 'continue' }
    'completion-audit' { 'verified_complete' }
}
if ($mode -eq 'early' -and $stage -eq 'implementation') { $status = 'complete' }
if ($mode -eq 'pending-complete' -and $stage -eq 'implementation') { $status = 'complete' }
if ($mode -eq 'repeat' -and $stage -eq 'completion-audit' -and $occurrence -eq 1) { $status = 'not_complete' }
if ($mode -eq 'limit' -and $stage -eq 'completion-audit') { $status = 'not_complete' }
if ($mode -eq 'replan' -and $stage -eq 'implementation' -and $occurrence -eq 1) { $status = 'replan' }
if ($mode -eq 'late-replan' -and $stage -eq 'implementation' -and $occurrence -eq 5) { $status = 'replan' }
if ($mode -eq 'findings' -and $stage -eq 'plan-audit') { $status = 'findings' }
if ($mode -eq "blocked-$stage") { $status = 'blocked' }
if ($mode -eq 'bad-status') { $status = 'invented' }
$ledgerPath = Join-Path $rootPath 'AUDIT_FINDINGS.md'
if (-not (Test-Path $ledgerPath)) { Set-Content $ledgerPath '# Audit findings' }
if ($status -in @('not_complete', 'findings')) {
    Set-Content $ledgerPath '| AF-0001 | required | open | A-01 | Missing test |'
}
if ($stage -eq 'implementation') {
    if ($mode -notin @('pending-complete', 'pending-verified')) {
        Set-Content (Join-Path $rootPath 'IMPLEMENTATION_PLAN.md') "| [x] A-01 | Implemented | Verified | none | fake |`n| [ ] OPTIONAL-AOT-01 | Not requested |"
    }
    if ((Get-Content $ledgerPath -Raw).Contains('AF-0001')) {
        Set-Content $ledgerPath '| AF-0001 | required | resolved | A-01 | Test passed |'
    }
}
$findings = @()
if ((Get-Content $ledgerPath -Raw) -match '\| required \| open \|') { $findings = @('AF-0001') }
if ($stage -eq 'plan-audit' -and $status -eq 'approved' -and $findings.Count -gt 0) { $status = 'findings' }
Set-Content (Join-Path $rootPath 'STATUS.md') "$stage $occurrence"
if ($mode -eq 'forbidden' -and $stage -eq 'plan') {
    Set-Content (Join-Path $rootPath 'sample.cs') 'unauthorized modification'
}
if ($mode -eq 'audit-plan-edit' -and $stage -eq 'plan-audit') {
    Set-Content (Join-Path $rootPath 'IMPLEMENTATION_PLAN.md') 'unauthorized plan edit'
}
if ($mode -eq 'doc-edit' -and $stage -eq 'implementation') {
    Set-Content (Join-Path $rootPath 'doc/adopted.md') 'unauthorized doc edit'
}
if ($mode -eq 'automation-edit' -and $stage -eq 'implementation') {
    Set-Content (Join-Path $rootPath 'automation/protected.ps1') 'unauthorized runner edit'
}
if ($mode -eq 'agents-edit' -and $stage -eq 'implementation') {
    Set-Content (Join-Path $rootPath 'AGENTS.md') 'unauthorized instructions edit'
}
if ($mode -eq 'omitted-finding') { Set-Content $ledgerPath '| AF-0001 | required | planned | A-01 | Missing test |' }
if ($mode -eq 'legacy-finding') { Set-Content $ledgerPath 'AF-0001: required, open. Missing test.' }
if ($mode -eq 'duplicate-finding') {
    Set-Content $ledgerPath @('| AF-0001 | advisory | open | A-01 | Advice |', '| AF-0001 | advisory | open | A-01 | Advice |')
}
if ($mode -eq 'advisory') { Set-Content $ledgerPath '| AF-0001 | advisory | open | A-01 | Optional advice |' }
if ($mode -eq 'duplicate-task') { Add-Content (Join-Path $rootPath 'IMPLEMENTATION_PLAN.md') '| [x] A-01 | duplicate |' }
if ($mode -eq 'invalid-task') { Add-Content (Join-Path $rootPath 'IMPLEMENTATION_PLAN.md') '| [?] A-02 | Invalid checkbox |' }
if ($mode -eq 'empty-plan') { Set-Content (Join-Path $rootPath 'IMPLEMENTATION_PLAN.md') '# No tasks' }
$result = @{
    status = $status; summary = '疑似応答による遷移テスト'
    next_task = 'A-01: next action'; task_ids = @('A-01')
    finding_ids = $findings; evidence = @('fake test evidence')
}
if ($status -in @('complete', 'verified_complete')) { $result.next_task = $null }
if ($stage -eq 'implementation') {
    $result.progress = $mode -ne 'stall'
    if ($result.progress) { Add-Content (Join-Path $rootPath 'sample.cs') "// implementation $occurrence" }
}
if ($mode -eq 'no-evidence') {
    $result.evidence = @()
    if ($stage -eq 'implementation') { $result.status = 'complete'; $result.next_task = $null }
}
if ($mode -eq 'extra-key') { $result.unexpected = $true }
if ($mode -eq 'bad-type') { $result.task_ids = 'A-01' }
if ($mode -eq 'missing-finding' -and $stage -eq 'plan-audit') {
    $result.status = 'findings'; $result.finding_ids = @('AF-9999')
}
if ($mode -eq 'bad-next') { $result.next_task = $null }
if ($mode -eq 'unknown-task') { $result.task_ids = @('NONEXISTENT-01') }
if ($mode -eq 'resolved-finding') {
    Set-Content $ledgerPath '| AF-0001 | required | resolved | A-01 | Already fixed |'
    $result.finding_ids = @('AF-0001')
}
$result | ConvertTo-Json -Depth 10 | Set-Content $outputPath -Encoding utf8
Write-Output 'fake stdout must not enter the structured result'
[Console]::Error.WriteLine('fake stderr')
exit 0
'@
[IO.File]::WriteAllText($fakePath, $fakeText, [Text.UTF8Encoding]::new($false))

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function New-Case([string]$Name, [string]$Mode) {
    $caseRoot = Join-Path $testRoot "$Name space 日本語"
    [IO.Directory]::CreateDirectory((Join-Path $caseRoot '.codex-loop')) | Out-Null
    & git init --quiet $caseRoot
    Assert ($LASTEXITCODE -eq 0) 'git init failed'
    foreach ($name in @('AGENTS.md', 'SPEC.md', 'STATUS.md', 'IMPLEMENTATION_PLAN.md', 'sample.cs')) {
        Set-Content (Join-Path $caseRoot $name) 'initial'
    }
    Set-Content (Join-Path $caseRoot 'IMPLEMENTATION_PLAN.md') '| [ ] A-01 | Implement | Verified | none | fake |'
    Add-Content (Join-Path $caseRoot 'IMPLEMENTATION_PLAN.md') '| [Composition Root](doc/old%20design.md) | Historical reference, excluded from specification |'
    [IO.Directory]::CreateDirectory((Join-Path $caseRoot 'doc')) | Out-Null
    Set-Content (Join-Path $caseRoot 'doc/adopted.md') 'adopted design'
    [IO.Directory]::CreateDirectory((Join-Path $caseRoot 'automation')) | Out-Null
    Set-Content (Join-Path $caseRoot 'automation/protected.ps1') '# protected'
    @{ mode = $Mode } | ConvertTo-Json | Set-Content (Join-Path $caseRoot '.codex-loop/mock.json')
    return $caseRoot
}
function Run-Case([string]$Root, [int]$ExpectedExit, [int]$Rounds = 1, [switch]$ResumeRun) {
    $arguments = @('-NoProfile', '-File', $runnerPath, '-ProjectRoot', $Root, '-CodexCommand', $fakePath,
        '-MaxRounds', $Rounds, '-StageTimeoutMinutes', 1)
    if ($ResumeRun) { $arguments += '-Resume' }
    & $pwshPath @arguments *> (Join-Path $Root '.codex-loop/test-output.log')
    $actualExit = $LASTEXITCODE
    Assert ($actualExit -eq $ExpectedExit) "$Root : exit $actualExit, expected $ExpectedExit. See test-output.log"
    Write-Host "PASS exit $actualExit : $(Split-Path $Root -Leaf)"
    return (Get-Content (Join-Path $Root '.codex-loop/state.json') -Raw | ConvertFrom-Json)
}
function Get-Stages([string]$Root) {
    return @(Get-Content (Join-Path $Root '.codex-loop/calls.jsonl') | ForEach-Object { ($_ | ConvertFrom-Json).stage })
}
$normalSequence = (@('plan', 'plan-audit', 'implementation', 'implementation') * 3) + @('completion-audit')
$passed = 0
try {
    & $pwshPath -NoProfile -File $fakePath exec --approve-for-me --sandbox workspace-write *> (Join-Path $testRoot 'argument-conflict.log')
    Assert ($LASTEXITCODE -eq 2) 'Fake CLI must reject the real CLI argument conflict'
    $passed++
    if ($Smoke) {
        $smokeRoot = New-Case 'smoke-error-resume' 'process-error'
        $failedState = Run-Case $smokeRoot 5
        @{ mode = 'early' } | ConvertTo-Json | Set-Content (Join-Path $smokeRoot '.codex-loop/mock.json')
        $resumedState = Run-Case $smokeRoot 0 -ResumeRun
        Assert ($failedState.run_id -eq $resumedState.run_id) 'Resume replaced the failed run'
        Assert (((Get-Stages $smokeRoot) -join ',') -eq 'plan,plan,plan-audit,implementation,completion-audit') 'Error resume did not traverse all four stages'
        $passed++
        Write-Host "PASS: $passed smoke scenarios (argument conflict and error/resume through all four stages). Logs: $testRoot"
        exit 0
    }
    $normal = New-Case 'normal' 'normal'
    $normalState = Run-Case $normal 0
    Assert (((Get-Stages $normal) -join ',') -eq ($normalSequence -join ',')) 'Normal order is incorrect'
    Assert ($normalState.stop_reason -eq 'verified_complete') 'Completion was not persisted'
    $null = Run-Case $normal 0 -ResumeRun
    Assert ((Get-Stages $normal).Count -eq 13) 'Completed resume invoked Codex'
    Add-Content (Join-Path $normal 'sample.cs') '// changed after completion'
    @{ mode = 'early' } | ConvertTo-Json | Set-Content (Join-Path $normal '.codex-loop/mock.json')
    $null = Run-Case $normal 0 -ResumeRun
    Assert ((Get-Stages $normal).Count -eq 17) 'Changed completed inputs were not re-audited'
    Add-Content (Join-Path $normal 'doc/adopted.md') 'changed adopted contract'
    $null = Run-Case $normal 0 -ResumeRun
    Assert ((Get-Stages $normal).Count -eq 21) 'Changed adopted document was not re-audited'
    $passed++

    $missingInput = New-Case 'missing-input' 'normal'
    Remove-Item -LiteralPath (Join-Path $missingInput 'SPEC.md')
    & $pwshPath -NoProfile -File $runnerPath -ProjectRoot $missingInput -CodexCommand $fakePath *> (Join-Path $missingInput '.codex-loop/test-output.log')
    Assert ($LASTEXITCODE -eq 5) 'Missing SPEC input was not rejected'
    Assert (-not (Test-Path (Join-Path $missingInput '.codex-loop/calls.jsonl'))) 'Missing input invoked Codex'
    $passed++

    $oldState = New-Case 'old-state' 'early'
    $savedState = Run-Case $oldState 0
    $savedState.version = 2
    $oldStatePath = Join-Path $oldState '.codex-loop/state.json'
    $savedState | ConvertTo-Json -Depth 10 | Set-Content $oldStatePath
    $oldHash = (Get-FileHash -LiteralPath $oldStatePath).Hash
    $null = Run-Case $oldState 5 -ResumeRun
    Assert ((Get-FileHash -LiteralPath $oldStatePath).Hash -eq $oldHash) 'Rejected legacy state was overwritten'
    Assert ((Get-Stages $oldState).Count -eq 4) 'Legacy resume invoked Codex'
    $passed++

    $advisory = New-Case 'advisory' 'advisory'
    $null = Run-Case $advisory 0
    $passed++

    $lateReplan = New-Case 'late-replan' 'late-replan'
    $null = Run-Case $lateReplan 0 2
    $lateStages = @(Get-Stages $lateReplan)
    Assert (($lateStages[8..12] -join ',') -eq 'plan,plan-audit,implementation,plan,plan-audit') 'Cycle 3 replan did not restart Plan'
    $passed++

    $repeat = New-Case 'repeat' 'repeat'
    $repeatState = Run-Case $repeat 0 2
    Assert (((Get-Stages $repeat) -join ',') -eq (($normalSequence * 2) -join ',')) 'Incomplete audit did not return to Plan'
    Assert ($repeatState.round -eq 2) 'Round did not advance'
    $passed++

    $early = New-Case 'early' 'early'
    $null = Run-Case $early 0
    Assert (((Get-Stages $early) -join ',') -eq 'plan,plan-audit,implementation,completion-audit') 'Early audit did not run'
    $passed++

    $replan = New-Case 'replan' 'replan'
    $null = Run-Case $replan 0
    Assert (((Get-Stages $replan)[0..4] -join ',') -eq 'plan,plan-audit,implementation,plan,plan-audit') 'Replan did not cancel slot 2'
    $passed++

    $findings = New-Case 'findings' 'findings'
    $null = Run-Case $findings 0
    Assert ((Get-Stages $findings).Count -eq 13) 'Findings prevented implementation from responding'
    $passed++

    foreach ($stage in @('plan', 'plan-audit', 'implementation', 'completion-audit')) {
        $blocked = New-Case "blocked-$stage" "blocked-$stage"
        $blockedState = Run-Case $blocked 3
        Assert ($blockedState.stop_reason -eq 'blocked') "Blocked $stage did not stop"
        Assert (@(Get-Stages $blocked)[-1] -eq $stage) 'A stage ran after blocked'
        $passed++
    }
    $resume = New-Case 'resume' 'blocked-implementation'
    $beforeResume = Run-Case $resume 3
    @{ mode = 'early' } | ConvertTo-Json | Set-Content (Join-Path $resume '.codex-loop/mock.json')
    $afterResume = Run-Case $resume 0 -ResumeRun
    Assert ($beforeResume.run_id -eq $afterResume.run_id) 'Resume replaced the run'
    Assert (((Get-Stages $resume)[3..6] -join ',') -eq 'plan,plan-audit,implementation,completion-audit') 'Resume did not restart from Plan'
    $passed++

    $stall = New-Case 'stall' 'stall'
    $stallState = Run-Case $stall 4 2
    Assert ($stallState.no_progress -eq 4) 'Stall counter is wrong'
    Assert (@(Get-Stages $stall | Where-Object { $_ -eq 'plan' }).Count -ge 2) 'Stall did not allow replanning'
    $passed++

    $limit = New-Case 'limit' 'limit'
    $limitState = Run-Case $limit 2
    Assert ($limitState.round -eq 2 -and $limitState.stop_reason -eq 'limit_reached') 'Limit was treated as completion'
    @{ mode = 'early' } | ConvertTo-Json | Set-Content (Join-Path $limit '.codex-loop/mock.json')
    $null = Run-Case $limit 0 2 -ResumeRun
    $passed++

    foreach ($mode in @('malformed', 'bad-status', 'missing-result', 'process-error', 'forbidden',
        'audit-plan-edit', 'no-evidence', 'extra-key', 'bad-type', 'missing-finding', 'bad-next',
        'pending-complete', 'pending-verified', 'doc-edit', 'automation-edit', 'agents-edit',
        'omitted-finding', 'legacy-finding', 'duplicate-finding', 'duplicate-task', 'invalid-task', 'empty-plan',
        'unknown-task', 'resolved-finding')) {
        $bad = New-Case $mode $mode
        $badState = Run-Case $bad 5
        Assert ($badState.stop_reason -eq 'error') "$mode did not preserve an error state"
        $expectedMaximum = if ($mode -eq 'pending-verified') { 13 } else { 3 }
        Assert (@(Get-Stages $bad).Count -le $expectedMaximum) "$mode was retried automatically"
        Assert (Test-Path (Join-Path $badState.run_dir ('{0:D4}-{1}.after.json' -f $badState.sequence, $badState.stage))) "$mode did not preserve the post-attempt snapshot"
        if ($mode -eq 'forbidden') {
            Assert ((Get-Content (Join-Path $bad 'sample.cs') -Raw).Contains('unauthorized')) 'Forbidden edit was silently rolled back'
        }
        if ($mode -eq 'process-error') {
            Assert ((Get-Content (Join-Path $bad 'STATUS.md') -Raw).Contains('partial work')) 'Partial failed work was lost'
        }
        $passed++
    }

    $locked = New-Case 'locked' 'normal'
    $lockStream = [IO.File]::Open((Join-Path $locked '.codex-loop/loop.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        & $pwshPath -NoProfile -File $runnerPath -ProjectRoot $locked -CodexCommand $fakePath *> (Join-Path $locked '.codex-loop/test-output.log')
        Assert ($LASTEXITCODE -eq 5) 'Concurrent invocation was not rejected'
        Assert (-not (Test-Path (Join-Path $locked '.codex-loop/calls.jsonl'))) 'Locked invocation started Codex'
    } finally { $lockStream.Dispose() }
    $passed++

    if ($IncludeTimeout) {
        $timeout = New-Case 'timeout' 'timeout'
        $timeoutState = Run-Case $timeout 6
        Assert ($timeoutState.stop_reason -eq 'timeout') 'Timeout state was not persisted'
        $childId = [int](Get-Content (Join-Path $timeout '.codex-loop/child.pid'))
        Assert ($null -eq (Get-Process -Id $childId -ErrorAction SilentlyContinue)) 'Timeout left a child process running'
        Assert (Test-Path (Join-Path $timeoutState.run_dir '0001-plan.after.json')) 'Timeout did not save the post-attempt snapshot'
        $passed++
    }
    Write-Host "PASS: $passed cases. No real Codex/API/compiler calls. Logs: $testRoot"
} catch {
    Write-Host "FAILED. Logs: $testRoot"
    throw
}
