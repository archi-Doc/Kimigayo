#requires -Version 7.4
<#
.SYNOPSIS
Runs (Plan -> Plan Audit -> Implementation -> Implementation) three times,
then Completion Audit. See automation/README.md for limits and recovery.
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [ValidateRange(1, 1000)][int]$MaxRounds = 10,
    [ValidateRange(1, 1440)][int]$StageTimeoutMinutes = 90,
    [string]$CodexCommand = 'codex',
    [switch]$Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$stateDir = Join-Path $ProjectRoot '.codex-loop'
$statePath = Join-Path $stateDir 'state.json'
$lock = $null
$state = $null
$exitCode = 5
$ownsState = $false

function Write-JsonFile([string]$Path, $Value) {
    $temporaryPath = "$Path.tmp"
    [IO.File]::WriteAllText($temporaryPath, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryPath, $Path, $true)
}

function Save-State {
    $state.updated_at = [DateTime]::UtcNow.ToString('o')
    Write-JsonFile $statePath $state
    Write-JsonFile (Join-Path $state.run_dir 'state.json') $state
}

function Get-WorkspaceSnapshot {
    $paths = & git -C $ProjectRoot -c core.quotepath=false ls-files --cached --others --exclude-standard -- ':!:.codex-loop/**'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository files.' }
    $snapshot = @{}
    foreach ($relativePath in ($paths | Sort-Object -Unique)) {
        $path = Join-Path $ProjectRoot $relativePath
        $snapshot[$relativePath] = if (Test-Path -LiteralPath $path -PathType Leaf) {
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        } else { '<missing>' }
    }
    return $snapshot
}

function Assert-EditScope([string]$Stage, [hashtable]$Before, [hashtable]$After) {
    $allowed = switch ($Stage) {
        'plan' { @('IMPLEMENTATION_PLAN.md', 'STATUS.md', 'AUDIT_FINDINGS.md') }
        'plan-audit' { @('AUDIT_FINDINGS.md', 'STATUS.md') }
        'completion-audit' { @('AUDIT_FINDINGS.md', 'STATUS.md') }
        default { @() }
    }
    foreach ($path in (@($Before.Keys) + @($After.Keys) | Sort-Object -Unique)) {
        if ($Before[$path] -eq $After[$path]) { continue }
        if (($Stage -ne 'implementation' -and $path -notin $allowed) -or
            ($Stage -eq 'implementation' -and ($path -like 'automation/*' -or $path -like 'doc/*' -or
                $path -eq 'AGENTS.md' -or $path -like '*/AGENTS.md' -or $path -like '.codex/*' -or $path -like '.agents/*'))) {
            throw "Stage $Stage changed a protected file: $path. Changes were preserved for review."
        }
    }
}

function New-StageSchema([string]$Stage) {
    $statuses = switch ($Stage) {
        'plan' { @('ready', 'blocked') }
        'plan-audit' { @('approved', 'findings', 'blocked') }
        'implementation' { @('continue', 'complete', 'replan', 'blocked') }
        'completion-audit' { @('verified_complete', 'not_complete', 'blocked') }
    }
    $properties = [ordered]@{
        status = @{ type = 'string'; enum = @($statuses) }
        summary = @{ type = 'string'; minLength = 1 }
        next_task = @{ type = @('string', 'null') }
        task_ids = @{ type = 'array'; items = @{ type = 'string'; minLength = 1 } }
        finding_ids = @{ type = 'array'; items = @{ type = 'string'; minLength = 1 } }
        evidence = @{ type = 'array'; items = @{ type = 'string'; minLength = 1 } }
    }
    if ($Stage -eq 'implementation') { $properties.progress = @{ type = 'boolean' } }
    return @{
        type = 'object'
        additionalProperties = $false
        properties = $properties
        required = @($properties.Keys)
    }
}

function Assert-Result([string]$Stage, $Result) {
    $finished = $Result.status -in @('complete', 'verified_complete')
    if ($finished) {
        if ($null -ne $Result.next_task -or $Result.finding_ids.Count -gt 0 -or $Result.evidence.Count -eq 0) {
            throw 'Completion requires next_task=null, no unresolved findings and verification evidence.'
        }
    } elseif ([string]::IsNullOrWhiteSpace($Result.next_task)) {
        throw 'A non-complete result must identify the next action or unblock condition.'
    }
    if ($Stage -eq 'implementation' -and $Result.progress -and $Result.evidence.Count -eq 0) {
        throw 'Progress requires concrete evidence.'
    }
    if ($Result.status -in @('findings', 'not_complete') -and $Result.finding_ids.Count -eq 0) {
        throw 'Audit findings must have persistent finding IDs.'
    }
    if ($Result.status -eq 'approved' -and $Result.finding_ids.Count -gt 0) {
        throw 'An approved plan must have no unresolved findings.'
    }
    if ($Result.status -in @('ready', 'approved') -and $Result.evidence.Count -eq 0) {
        throw 'Planning and approval require concrete evidence.'
    }
    if ($Result.finding_ids.Count -gt 0) {
        $findingsPath = Join-Path $ProjectRoot 'AUDIT_FINDINGS.md'
        if (-not (Test-Path -LiteralPath $findingsPath -PathType Leaf)) { throw 'AUDIT_FINDINGS.md is missing.' }
        $findingsText = [IO.File]::ReadAllText($findingsPath)
        foreach ($id in $Result.finding_ids) {
            if ($id -notmatch '^AF-\d{4,}$' -or $findingsText -notmatch ('(?<![\w-])' + [regex]::Escape($id) + '(?![\w-])')) {
                throw "Finding ID is invalid or not recorded: $id"
            }
        }
    }
    Assert-DurableResult $Result
}

function Invoke-Stage {
    $stage = $state.stage
    $previousOutput = $state.last_output
    $state.sequence++
    $state.in_flight = $true
    $state.stop_reason = 'running'
    $prefix = '{0:D4}-{1}' -f $state.sequence, $stage
    $logBase = Join-Path $state.run_dir $prefix
    $state.last_output = "$logBase.result.json"
    Save-State
    $before = Get-WorkspaceSnapshot
    Write-JsonFile "$logBase.before.json" $before
    $schemaPath = "$logBase.schema.json"
    Write-JsonFile $schemaPath (New-StageSchema $stage)
    $commonText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'common-prompt.md'))
    $roleText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "$stage-prompt.md"))
    $deadline = [DateTime]::UtcNow.AddMinutes($StageTimeoutMinutes).ToString('o')
    $context = @"

# Automation context
Project root: $ProjectRoot
Stage: $stage
Round: $($state.round) / $MaxRounds
Cycle: $($state.cycle) / 3
Implementation slot: $($state.implementation_slot) / 2
Consecutive implementation runs without progress: $($state.no_progress)
Reason: $($state.reason)
Previous validated result: $($state.last_result)
Previous attempt output (may be missing or invalid): $previousOutput
Current evidence prefix: $logBase
Stage time limit: $StageTimeoutMinutes minutes. Hard deadline (UTC): $deadline
Reserve the final 5 minutes (or 10% for shorter stages) for verification, durable handoff and the final JSON.
Read $PSScriptRoot/verification-guide.md for repository-specific test commands and fixture precautions.
State belongs to the runner. Do not edit .codex-loop/state.json or runner-owned files.
Use IMPLEMENTATION_PLAN.md, STATUS.md and AUDIT_FINDINGS.md for durable handoff.
"@
    $promptPath = "$logBase.prompt.md"
    [IO.File]::WriteAllText($promptPath, (@($commonText, $roleText, $context) -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    $invocationPath = "$logBase.invocation.json"
    Write-JsonFile $invocationPath @{
        command = $resolvedCodex
        # --approve-for-me already selects workspace-write; --sandbox conflicts with it.
        arguments = @('exec', '--ephemeral', '--approve-for-me',
            '--color', 'never', '-C', $ProjectRoot, '--output-schema', $schemaPath, '-o', $state.last_output, '-')
        prompt_path = $promptPath
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $pwshName = if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
    $startInfo.FileName = Join-Path $PSHOME $pwshName
    $startInfo.WorkingDirectory = $ProjectRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'invoke-codex.ps1'), '-InvocationPath', $invocationPath)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stdout = [IO.File]::Create("$logBase.stdout.log")
    $stderr = [IO.File]::Create("$logBase.stderr.log")
    $started = $false
    $outCopy = $null
    $errCopy = $null
    $stageError = $null
    $copyCancellation = [Threading.CancellationTokenSource]::new()
    try {
        Write-Host "Round $($state.round), cycle $($state.cycle): $stage (slot $($state.implementation_slot)). Logs: $logBase"
        $started = $process.Start()
        $outCopy = $process.StandardOutput.BaseStream.CopyToAsync($stdout, $copyCancellation.Token)
        $errCopy = $process.StandardError.BaseStream.CopyToAsync($stderr, $copyCancellation.Token)
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while (-not $process.WaitForExit(1000)) {
            if ($watch.Elapsed.TotalMinutes -ge $StageTimeoutMinutes) {
                $process.Kill($true)
                $process.WaitForExit()
                throw [TimeoutException]::new("Stage $stage exceeded $StageTimeoutMinutes minutes.")
            }
        }
        # A descendant can retain the pipes even after the CLI exits. Bound log draining too.
        $remaining = [TimeSpan]::FromMinutes($StageTimeoutMinutes) - $watch.Elapsed
        if ($remaining -le [TimeSpan]::Zero) { throw [TimeoutException]::new("Stage $stage exhausted its time budget.") }
        $copies = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($outCopy, $errCopy))
        try { [void]$copies.WaitAsync($remaining).GetAwaiter().GetResult() }
        catch [TimeoutException] { throw [TimeoutException]::new("Stage $stage exceeded its time budget while draining CLI logs.") }
        if ($process.ExitCode -ne 0) { throw "Codex exited with code $($process.ExitCode). See $logBase.stderr.log" }
    } catch {
        $stageError = $_
    } finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $copyCancellation.Cancel()
        foreach ($copy in @($outCopy, $errCopy)) {
            if ($null -ne $copy) {
                try { [void]$copy.GetAwaiter().GetResult() }
                catch [OperationCanceledException] { }
            }
        }
        $copyCancellation.Dispose()
        $stdout.Dispose()
        $stderr.Dispose()
        $process.Dispose()
    }
    $after = Get-WorkspaceSnapshot
    Write-JsonFile "$logBase.after.json" $after
    Assert-EditScope $stage $before $after
    if ($null -ne $stageError) { throw $stageError }
    if (-not (Test-Path -LiteralPath $state.last_output -PathType Leaf)) { throw 'Codex did not create a final result.' }
    $json = [IO.File]::ReadAllText($state.last_output)
    if (-not (Test-Json -Json $json -SchemaFile $schemaPath -ErrorAction Stop)) { throw 'Invalid result schema.' }
    $result = ConvertFrom-Json -InputObject $json -AsHashtable
    Assert-Result $stage $result
    $state.in_flight = $false
    $state.last_result = $state.last_output
    Write-Host "$($result.status): $($result.summary)"
    return $result
}

function Start-NextCycle([string]$Reason, [switch]$Replan) {
    $state.cycle++
    $state.implementation_slot = 1
    $state.reason = $Reason
    if ($state.cycle -gt 3) {
        if ($Replan) {
            $state.round++
            $state.cycle = 1
            $state.stage = 'plan'
        } else {
            $state.cycle = 3
            $state.stage = 'completion-audit'
        }
    } else { $state.stage = 'plan' }
}

try {
    $gitRoot = & git -C $ProjectRoot rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot) -ne [IO.Path]::GetFullPath($ProjectRoot)) {
        throw 'ProjectRoot must be the Git repository root.'
    }
    $commandInfo = Get-Command $CodexCommand -CommandType Application, ExternalScript -ErrorAction Stop
    $resolvedCodex = $commandInfo.Source
    foreach ($name in @('common-prompt.md', 'plan-prompt.md', 'plan-audit-prompt.md', 'implementation-prompt.md', 'completion-audit-prompt.md', 'invoke-codex.ps1', 'result-contract.ps1', 'verification-guide.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name) -PathType Leaf)) { throw "Missing automation file: $name" }
    }
    foreach ($name in @('AGENTS.md', 'SPEC.md', 'STATUS.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $name) -PathType Leaf)) { throw "Missing project input: $name" }
    }
    . (Join-Path $PSScriptRoot 'result-contract.ps1')
    [IO.Directory]::CreateDirectory($stateDir) | Out-Null
    $lock = [IO.File]::Open((Join-Path $stateDir 'loop.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $stateIgnore = Join-Path $stateDir '.gitignore'
    if (-not (Test-Path -LiteralPath $stateIgnore)) {
        [IO.File]::WriteAllText($stateIgnore, '*' + [Environment]::NewLine)
    }
    if ($Resume) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable
        foreach ($key in @('version', 'project_root', 'round', 'cycle', 'stage', 'run_id', 'run_dir',
            'sequence', 'implementation_slot', 'no_progress', 'in_flight', 'stop_reason',
            'reason', 'last_result', 'last_output', 'updated_at')) {
            if (-not $state.Contains($key)) { throw "Missing state field: $key" }
        }
        if ($state.version -ne 3 -or $state.project_root -ne $ProjectRoot -or
            ($state.round -isnot [long] -and $state.round -isnot [int])) { throw 'Invalid or incompatible state.' }
        if ($state.round -lt 1 -or $state.cycle -notin 1, 2, 3 -or
            $state.stage -notin @('plan', 'plan-audit', 'implementation', 'completion-audit')) { throw 'Invalid stage counters.' }
        foreach ($key in @('cycle', 'sequence', 'implementation_slot', 'no_progress')) {
            if (($state[$key] -isnot [long] -and $state[$key] -isnot [int]) -or $state[$key] -lt 0) {
                throw "Invalid state counter: $key"
            }
        }
        if ($state.implementation_slot -notin 1, 2 -or $state.in_flight -isnot [bool]) { throw 'Invalid state fields.' }
        $expectedRunDir = Join-Path (Join-Path $stateDir 'runs') $state.run_id
        if ($state.run_id -notmatch '^[a-f0-9]{32}$' -or $state.run_dir -ne $expectedRunDir) { throw 'Invalid run directory.' }
        $ownsState = $true
        if ($state.stop_reason -eq 'verified_complete') {
            $completedSnapshotPath = Join-Path $state.run_dir ('{0:D4}-completion-audit.after.json' -f $state.sequence)
            $completedSnapshot = Get-Content -LiteralPath $completedSnapshotPath -Raw | ConvertFrom-Json -AsHashtable
            $currentSnapshot = Get-WorkspaceSnapshot
            $changed = @(@($completedSnapshot.Keys) + @($currentSnapshot.Keys) | Sort-Object -Unique |
                Where-Object { $completedSnapshot[$_] -ne $currentSnapshot[$_] })
            if ($changed.Count -eq 0) {
                Write-Host 'This run is already complete and the recorded repository inputs are unchanged.'
                $exitCode = 0
            } else {
                $state.stop_reason = 'changed_after_completion'
            }
        }
        if ($state.stop_reason -ne 'verified_complete') {
            $resumeReason = "Explicit resume after $($state.stop_reason): $($state.reason)"
            $state.stage = 'plan'
            $state.implementation_slot = 1
            $state.no_progress = 0
            $state.in_flight = $false
            $state.reason = "$resumeReason. Inspect preserved partial work and external changes before continuing."
            $state.stop_reason = 'running'
        }
    } else {
        $runId = [Guid]::NewGuid().ToString('N')
        $runDir = Join-Path (Join-Path $stateDir 'runs') $runId
        [IO.Directory]::CreateDirectory($runDir) | Out-Null
        $state = [ordered]@{
            version = 3; project_root = $ProjectRoot; run_id = $runId; run_dir = $runDir
            round = 1; cycle = 1; implementation_slot = 1; sequence = 0; stage = 'plan'
            no_progress = 0; in_flight = $false; stop_reason = 'running'
            reason = 'Start from the current repository and previous audit findings.'
            last_result = $null; last_output = $null; updated_at = $null
        }
        $ownsState = $true
    }
    if ($state.stop_reason -ne 'verified_complete') {
        Save-State
        while ($state.round -le $MaxRounds) {
            $result = Invoke-Stage
            if ($result.status -eq 'blocked') {
                $state.stop_reason = 'blocked'
                $state.reason = $result.next_task
                $exitCode = 3
                break
            }
            switch ($state.stage) {
                'plan' {
                    $state.stage = 'plan-audit'
                    $state.reason = 'Critique the plan before implementation; record findings without editing the plan.'
                }
                'plan-audit' {
                    $state.stage = 'implementation'
                    $state.implementation_slot = 1
                    $state.reason = 'Resolve or document audit findings in the plan before implementing the selected scope.'
                }
                'implementation' {
                    if ($result.progress) { $state.no_progress = 0 } else { $state.no_progress++ }
                    if ($result.status -eq 'complete') {
                        $state.stage = 'completion-audit'
                        $state.reason = 'Early completion claim: independently verify all required scope.'
                    } elseif ($state.no_progress -ge 4) {
                        $state.stop_reason = 'stalled'
                        $state.reason = 'Four implementation runs made no progress, including an opportunity to replan.'
                        $exitCode = 4
                    } elseif ($result.status -eq 'replan' -or $state.no_progress -ge 2) {
                        Start-NextCycle 'Replan after a material plan issue or repeated lack of progress.' -Replan
                    } elseif ($state.implementation_slot -eq 1) {
                        $state.implementation_slot = 2
                        $state.reason = 'Continue unfinished implementation and verification from slot 1 before selecting another item.'
                    } else {
                        Start-NextCycle 'The two implementation slots ended. Update the plan from the resulting repository.'
                    }
                }
                'completion-audit' {
                    if ($result.status -eq 'verified_complete') {
                        $state.stop_reason = 'verified_complete'
                        $state.reason = $result.summary
                        $exitCode = 0
                    } else {
                        $state.round++
                        $state.cycle = 1
                        $state.implementation_slot = 1
                        $state.stage = 'plan'
                        $state.reason = 'Completion audit found missing work. Incorporate AUDIT_FINDINGS.md into the plan.'
                    }
                }
            }
            Save-State
            if ($state.stop_reason -ne 'running') { break }
        }
        if ($state.stop_reason -eq 'running') {
            $state.stop_reason = 'limit_reached'
            $state.reason = 'Round limit reached without verified completion. Increase MaxRounds when resuming.'
            $exitCode = 2
        }
        Save-State
    }
} catch {
    $exitCode = if ($_.Exception -is [TimeoutException]) { 6 } else { 5 }
    Write-Warning $_.Exception.Message
    if ($ownsState) {
        $state.stop_reason = if ($exitCode -eq 6) { 'timeout' } else { 'error' }
        $state.reason = $_.Exception.Message
        Save-State
    }
} finally {
    if ($null -ne $lock) { $lock.Dispose() }
}
Write-Host "Loop stopped (exit $exitCode). State: $statePath"
exit $exitCode
