#requires -Version 7.4
[CmdletBinding()]
param([switch]$KeepFixtures,[string]$Filter='*')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../lib/core.ps1')
. (Join-Path $PSScriptRoot '../lib/manifest.ps1')
. (Join-Path $PSScriptRoot '../lib/transition.ps1')
. (Join-Path $PSScriptRoot '../invoke-worker.ps1')
. (Join-Path $PSScriptRoot '../lib/engine.ps1')
$script:Passed=0
$fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('autoframe-tests-'+[guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory($fixtureRoot)
$runner=Join-Path $script:FrameRoot 'run.ps1'
$fake=Join-Path $PSScriptRoot 'fake-codex.ps1'
$pwsh=Resolve-Cli pwsh
function Assert([bool]$Condition,[string]$Message) { if(-not $Condition) { throw "ASSERT: $Message" } }
function Test([string]$Name,[scriptblock]$Body) { if($Name -notlike $Filter) { return }; & $Body; $script:Passed++; Write-Host "PASS $Name" }
function Reject([scriptblock]$Body) { $failed=$false; try { & $Body | Out-Null } catch { $failed=$true }; Assert $failed 'expected rejection' }
function Fixture([string]$Name,[string]$Mode='success',[int]$Count=1,[switch]$ReadOnly) {
    $root=Join-Path $fixtureRoot $Name; $null=[IO.Directory]::CreateDirectory($root)
    $plan=@{schema_version=1;project_id=$Name;objective='fixture objective';scope=@('finite fixture');non_goals=@();constraints=@();completion_criteria=@(@{id='C1';condition='fixture is correct';verification='check fixture'});work_scope=@(if(-not $ReadOnly) { 'out/**' });tasks=@()}
    for($i=1;$i -le $Count;$i++) { $plan.tasks+=@(@{id="T$i";description='fixture task';required=$true;depends_on=@();criterion_ids=@('C1');acceptance='fixture';verification='check fixture'}) }
    Write-Plan $root $plan
    Save-Json (Join-Path $root 'scenario.json') @{mode=$Mode}
    [IO.File]::WriteAllText((Join-Path $root 'protected.txt'),'keep')
    return $root
}
function Write-Plan([string]$Root,$Plan) {
    $text='<!-- autoframe:begin -->'+"`n"+'```json'+"`n"+(ConvertTo-Json $Plan -Depth 30)+"`n"+'```'+"`n"+'<!-- autoframe:end -->'
    [IO.File]::WriteAllText((Join-Path $Root 'PLAN.md'),$text,[Text.UTF8Encoding]::new($false))
}
function Run([string]$Root,[string[]]$Extra=@(),[int]$Expected=0) {
    $log=Join-Path $fixtureRoot ([guid]::NewGuid().ToString('N')+'.txt')
    & $pwsh -NoProfile -NonInteractive -File $runner -ProjectRoot $Root -CodexCommand $fake -MaxRunMinutes 10 -PhaseTimeoutMinutes 2 -SaveReserveMinutes 1 -StopTimeoutSeconds 5 @Extra *> $log
    if($LASTEXITCODE -ne $Expected) { throw "Exit $LASTEXITCODE expected $Expected : $([IO.File]::ReadAllText($log))" }
    if(Test-Path -LiteralPath (Join-Path $Root '.autoframe/state.json')) {
        try { return Read-Json (Join-Path $Root '.autoframe/state.json') 'state' } catch { if($Expected -ne 6) { throw } }
    }
}
try {
    Test 'duplicate JSON keys including escaped keys' { Reject { [Autoframe.Json]::Canonical('{"a":1,"\u0061":2}') } }
    Test 'canonical keys are ordinal' { Assert ((Get-ObjectHash @{b=2;a=1}) -ceq (Get-ObjectHash @{a=1;b=2})) 'canonical hash' }
    Test 'PLAN strict schema, references, cycles, stable bytes' {
        $r=Fixture 'plan-validation'; $p=Read-Plan $r; $h=$p.hash
        $p.data.bad=1; Reject { Assert-Schema $p.data 'plan' }; $p.data.Remove('bad')
        $p.data.tasks[0].depends_on=@('T1'); Reject { Assert-Graph $p.data.tasks $p.data.completion_criteria @() }
        $p.data.tasks[0].depends_on=@('t1'); Reject { Assert-Graph $p.data.tasks $p.data.completion_criteria @() }
        Assert ((Read-Plan $r).hash -ceq $h) 'PLAN is read-only'
    }
    Test 'glob zero-depth, boundaries and invalid paths' {
        Assert ((Convert-Glob 'src/**/a?.txt').IsMatch('src/a1.txt')) 'zero levels'
        Assert (-not (Convert-Glob 'src/*.txt').IsMatch('src/x/a.txt')) '* boundary'
        Reject { Convert-Glob '../x' }; Reject { Convert-Glob 'src/a**b' }; Reject { Resolve-Safe $fixtureRoot '../outside' }
    }
    Test 'manifest additions deletions and content, no mtime shortcut' {
        $r=Fixture 'manifest'; $s=Get-Snapshot $r; $h=Get-ObjectHash (New-Manifest $s @('new.txt'))
        [IO.File]::WriteAllText((Join-Path $r 'new.txt'),'a'); $m=New-Manifest (Get-Snapshot $r) @('new.txt')
        Assert ((Get-ObjectHash $m) -cne $h) 'addition'
        [IO.File]::WriteAllText((Join-Path $r 'new.txt'),'b'); Assert ((Get-ObjectHash (New-Manifest (Get-Snapshot $r) @('new.txt'))) -cne (Get-ObjectHash $m)) 'same length content'
        [IO.File]::Delete((Join-Path $r 'new.txt')); Assert ((Get-ObjectHash (New-Manifest (Get-Snapshot $r) @('new.txt'))) -ceq $h) 'deletion'
    }
    Test 'successful six phases; final Work always verified; PLAN unchanged' {
        $r=Fixture 'success with spaces'; $before=[Autoframe.Json]::FileHash((Join-Path $r 'PLAN.md'))
        $s=Run $r
        Assert ($s.status -ceq 'Complete' -and $s.attempts -eq 6) 'six phases completed'
        Assert ([Autoframe.Json]::FileHash((Join-Path $r 'PLAN.md')) -ceq $before) 'PLAN unchanged'
        Assert ($s.base_plan_version -lt $s.attempts+1) 'heartbeat does not advance logical version'
    }
    Test 'read-only task project completes' { $r=Fixture 'document review' -ReadOnly; $s=Run $r; Assert ($s.status -ceq 'Complete') 'review complete' }
    Test 'three independent targets in one Work' { $r=Fixture 'batch' 'success' 3; $s=Run $r; Assert ($s.attempts -eq 6) 'one batch' }
    Test 'workless loop stalls; Resume cannot clear stall' {
        $r=Fixture 'workless' 'workless'; $s=Run $r @() 5; Assert ($s.counters.workless_plan_returns -eq 3) 'stalled at three returns'
        $n=$s.attempts; $s=Run $r @('-Resume') 5; Assert ($s.attempts -eq $n) 'no automatic stall reset'
    }
    Test 'Audit revision loop stalls before attempt cap' { $r=Fixture 'audit-loop' 'audit-loop'; $s=Run $r @() 5; Assert ($s.attempts -lt 30) 'workless audit loop stopped' }
    Test 'no-progress Work stops at four verified reports' { $r=Fixture 'no-progress' 'no-progress'; $s=Run $r @() 5; Assert ($s.counters.no_progress_works -eq 4) 'four reports' }
    Test 'protected writes are rejected and recoverable' {
        $r=Fixture 'protected' 'protected-write'; $s=Run $r @() 6; Assert ($s.uncertain.Count -eq 1) 'uncertain retained'
        Assert (Test-Path -LiteralPath (Join-Path $s.uncertain[0].directory 'after.json')) 'after manifest exists'
    }
    Test 'missing result on Work crash keeps manifest and recovery targets' {
        $r=Fixture 'crash' 'work-crash'; $s=Run $r @() 6; Assert ($s.uncertain[0].targets[0] -ceq 'T1') 'recovery target'
        Save-Json (Join-Path $r 'scenario.json') @{mode='success'}
        $s=Run $r @('-Resume'); Assert ($s.status -ceq 'Complete') 'recovered through Verify'
    }
    Test 'malformed result error does not become complete' { $r=Fixture 'bad-result' 'invalid-result'; $s=Run $r @() 6; Assert ($s.attempts -eq 1) 'single error attempt' }
    Test 'argument errors preserve saved state' {
        $r=Fixture 'arguments'; $s=Run $r @('-MaxPhaseAttempts','1') 2; $h=[Autoframe.Json]::FileHash((Join-Path $r '.autoframe/state.json'))
        $null=Run $r @('-Resume','-NewRun') 6
        Assert ([Autoframe.Json]::FileHash((Join-Path $r '.autoframe/state.json')) -ceq $h) 'state preserved'
    }
    Test 'all delivered prompts contain common rules' {
        $files=Get-ChildItem -LiteralPath $fixtureRoot -Filter prompt.md -Recurse
        Assert ($files.Count -gt 0) 'prompts were actually delivered'
        foreach($f in $files) { $t=[IO.File]::ReadAllText($f.FullName); foreach($rule in @('PLAN.mdは読取専用','次のWorkerを起動しない','未実行','save_from_utc','最小限','base_plan_version')) { Assert ($t.Contains($rule)) "common rule: $rule" } }
    }
    Test 'completion audit semantic deduplication prevents loops' {
        $r=Fixture 'completion-loop' 'completion-loop'; $s=Run $r @() 5
        $results=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ } | Where-Object { $_.Contains('phase') -and $_.phase -ceq 'CompletionAudit' })
        Assert ($results.Count -eq 1) 'identical incomplete audit is not relaunched'
    }
    Test 'unknown necessary environment blocks only affected work' {
        $r=Fixture 'environment-unknown' 'unknown-environment'; $s=Run $r @() 3
        $l=Read-Record (Join-Path $r '.autoframe') $s.logical_ref 'logical'; Assert ($l.tasks[0].status -ceq 'blocked') 'unknown environment not verified'
    }
    Test 'environment probes propagate native and PowerShell failures' {
        foreach($command in @('Write-Error "fixture error"','pwsh -NoProfile -Command "exit 7"')) {
            $r=Fixture ([guid]::NewGuid().ToString('N')); $p=(Read-Plan $r).data; $p.environment_checks=@($command); Write-Plan $r $p
            $s=Run $r @() 6; Assert ($s.attempts -eq 0 -and $s.uncertain.Count -gt 0) 'failed probe is uncertain, not accepted environment'
        }
    }
    Test 'required findings persist until authorized resolution, with history' {
        $r=Fixture 'finding-open' 'finding-open'; $s=Run $r @() 6
        $l=Read-Record (Join-Path $r '.autoframe') $s.logical_ref 'logical'; Assert ($l.findings[0].status -ceq 'open') 'omission does not resolve'
        $r=Fixture 'finding-resolved' 'finding-resolved'; $s=Run $r
        $l=Read-Record (Join-Path $r '.autoframe') $s.logical_ref 'logical'; Assert ($l.findings[0].status -ceq 'resolved' -and $s.history.Count -ge 6) 'resolution evidence and history retained'
    }
    Test 'dependency evidence invalidates on input changes and recovers without blind Work' {
        $r=Fixture 'dependency' 'success' 2; $p=(Read-Plan $r).data; $p.tasks[1].depends_on=@('T1'); Write-Plan $r $p
        $s=Run $r
        $l=Read-Record (Join-Path $r '.autoframe') $s.logical_ref 'logical'
        Assert (@($l.tasks | Where-Object { $_.status -cne 'verified' }).Count -eq 0) 'dependencies reverified'
        $results=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ } | Where-Object { $_.Contains('phase') })
        Assert (@($results | Where-Object { $_.phase -ceq 'Work' }).Count -eq 2) 'revalidation does not repeat Work'
        Assert (@($results | Where-Object { $_.phase -ceq 'Verify' }).Count -ge 3) 'invalidated dependency reverified'
    }
    Test 'heartbeat saves time without invalidating in-flight result' { $r=Fixture 'heartbeat' 'heartbeat'; $s=Run $r @('-MaxPhaseAttempts','1') 2; Assert ($s.accepted_attempts.Count -eq 1 -and $s.elapsed_seconds -ge 20) 'heartbeat result accepted' }
    Test 'budget during recovery preserves pending verification' {
        $r=Fixture 'recovery-budget'; $s=Run $r @('-MaxPhaseAttempts','4') 2
        Assert ($s.pending_work -and $s.attempts -eq 4) 'Work saved at cap'
        $s=Run $r @('-Resume','-MaxPhaseAttempts','4') 2
        Assert ($s.pending_work -and $s.attempts -eq 4) 'resume does not reset cap'
        $s=Run $r @('-Resume','-MaxPhaseAttempts','20'); Assert ($s.status -ceq 'Complete') 'raised cap completes recovery'
    }
    Test 'inherited settings, explicit replacement, NewRun and corrupt-state archive' {
        $r=Fixture 'resume-settings'; $s=Run $r @('-MaxTasksPerWork','1','-MaxPhaseAttempts','1') 2; $id=$s.run_id
        $log=Join-Path $fixtureRoot 'resume-settings.log'
        & $pwsh -NoProfile -File $runner -ProjectRoot $r -Resume -MaxPhaseAttempts 30 *> $log
        Assert ($LASTEXITCODE -eq 0) ([IO.File]::ReadAllText($log))
        $s=Read-Json (Join-Path $r '.autoframe/state.json') 'state'
        Assert ($s.settings.MaxTasksPerWork -eq 1 -and $s.settings.CodexCommand -ceq $fake -and $s.run_id -ceq $id) 'saved settings inherited'
        [IO.File]::WriteAllText((Join-Path $r '.autoframe/state.json'),'{corrupt')
        $null=Run $r @('-Resume') 6
        $s=Run $r @('-NewRun'); Assert ($s.run_id -cne $id) 'NewRun starts new identity'
        Assert (@(Get-ChildItem -LiteralPath (Join-Path $r '.autoframe/records') -Filter 'previous-*.json').Count -gt 0) 'corrupt state archived'
    }
    Test 'lock excludes simultaneous runner without overwriting state' {
        $r=Fixture 'lock'; $null=[IO.Directory]::CreateDirectory((Join-Path $r '.autoframe'))
        $lock=[IO.File]::Open((Join-Path $r '.autoframe/lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        try { $null=Run $r @() 6; Assert (-not (Test-Path -LiteralPath (Join-Path $r '.autoframe/state.json'))) 'no state published' } finally { $lock.Dispose() }
    }
    Test 'adapter streams large stdout and stderr; timeout stops process tree' {
        $dir=Join-Path $fixtureRoot 'large-output'
        $r=Invoke-Child $pwsh @('-NoProfile','-Command','for($i=0;$i -lt 4000;$i++){[Console]::WriteLine(("x"*1024));[Console]::Error.WriteLine(("y"*1024))}') $fixtureRoot $dir '' 30 5
        Assert ($r.exit_code -eq 0 -and (Get-Item -LiteralPath (Join-Path $dir 'events.jsonl')).Length -gt 4000000 -and (Get-Item -LiteralPath (Join-Path $dir 'stderr.log')).Length -gt 4000000) 'both pipes drained'
        $r=Invoke-Child $pwsh @('-NoProfile','-Command','Start-Sleep -Seconds 30') $fixtureRoot (Join-Path $fixtureRoot 'timeout') '' 1 5
        Assert ($r.timed_out) 'short adapter deadline used independently of minute API'
        Reject { Invoke-Child (Join-Path $fixtureRoot 'not-found.exe') @() $fixtureRoot (Join-Path $fixtureRoot 'spawn-error') '' 5 5 | ForEach-Object { if($_.exit_code -ne 0) { throw 'spawn failure' } } }
    }
    Test 'child remaining after parent exit rejects result' {
        $scriptPath=Join-Path $fixtureRoot 'orphan.ps1'
        [IO.File]::WriteAllText($scriptPath,'Start-Process pwsh -WindowStyle Hidden -ArgumentList @("-NoProfile","-Command","Start-Sleep -Seconds 30") | Out-Null')
        Reject { Invoke-Child $pwsh @('-NoProfile','-File',$scriptPath) $fixtureRoot (Join-Path $fixtureRoot 'orphan') '' 10 5 }
    }
    Test 'unreferenced records cannot change canonical state' {
        $r=Fixture 'atomic-state'; $s=Run $r @('-MaxPhaseAttempts','1') 2
        $path=Join-Path $r '.autoframe/state.json'; $before=[Autoframe.Json]::FileHash($path)
        $null=Save-Record (Join-Path $r '.autoframe') @{kind='unaccepted';attempt_id='never-accepted'}
        Assert ([Autoframe.Json]::FileHash($path) -ceq $before) 'record alone does not publish state'
        $bad=Copy-Value $s; $bad.schema_version=2; Reject { Assert-Schema $bad 'state' }
    }
    Test 'PLAN without initial tasks is decomposed by Plan' { $r=Fixture 'generated tasks' -Count 0 -ReadOnly; $s=Run $r; Assert ($s.status -ceq 'Complete') 'generated task completed' }
    Test 'partial batch reports preserve per-task status' {
        $r=Fixture 'partial batch' 'partial' 2; $s=Run $r @('-MaxPhaseAttempts','5') 2
        $l=Read-Record (Join-Path $r '.autoframe') $s.logical_ref 'logical'
        Assert ($l.tasks[0].status -ceq 'verified' -and $l.tasks[1].status -ceq 'pending') 'partial success is not whole success'
    }
    Test 'Complete Resume rechecks evidence even at launch cap' {
        $r=Fixture 'complete-resume'; $s=Run $r @('-MaxPhaseAttempts','6'); $n=$s.attempts
        $s=Run $r @('-Resume','-MaxPhaseAttempts','6'); Assert ($s.attempts -eq $n -and $s.status -ceq 'Complete') 'reuse without additional launch'
        [IO.File]::WriteAllText((Join-Path $r 'out/T1.txt'),'changed')
        $s=Run $r @('-Resume','-MaxPhaseAttempts','6') 2
        Assert ($s.status -ceq 'Paused' -and $s.attempts -eq $n) 'changed artifact cannot reuse Complete'
    }
    Test 'explicit stall reset preserves run budget and records reason' {
        $r=Fixture 'reset' 'workless'; $s=Run $r @() 5; $id=$s.run_id; $attempts=$s.attempts
        $s=Run $r @('-Resume','-ResetStallCounters','-ResetReason','fixture correction') 5
        Assert ($s.run_id -ceq $id -and $s.attempts -eq $attempts+3) 'reset did not create new budget'
        $history=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ })
        Assert (@($history | Where-Object { $_.Contains('kind') -and $_.kind -ceq 'reset-stall' -and $_.reason -ceq 'fixture correction' }).Count -eq 1) 'reset reason recorded'
    }
    Test 'schema and phase identity reject forged acceptance and replay' {
        $r=Fixture 'contracts'; $s=Run $r @('-MaxPhaseAttempts','1') 2
        $dir=Get-ChildItem -LiteralPath (Join-Path $r ".autoframe/runs/$($s.run_id)") -Directory | Select-Object -First 1
        $context=Read-Json (Join-Path $dir.FullName 'input.json'); $result=Read-Json (Join-Path $dir.FullName 'output/result.json')
        Reject { Assert-ResultEnvelope $result $s $context }
        $s.accepted_attempts=@(); $result.base_plan_version++; Reject { Assert-ResultEnvelope $result $s $context }
        $result.base_plan_version--; $result.phase='Work'; Reject { Assert-ResultEnvelope $result $s $context }
        $result.phase='Plan'; $result.unknown=1; Reject { Assert-Schema $result 'result' }
    }
    Test 'scope exclusion and link rejection' {
        $r=Fixture 'scope'; $p=(Read-Plan $r).data; $p.input_scope=@('protected.txt'); $p.generated_scope=@('protected.txt')
        Reject { Assert-PlanScopes $r $p }
        $outside=Join-Path $fixtureRoot 'junction-target'; $null=[IO.Directory]::CreateDirectory($outside)
        $link=Join-Path $r 'junction'; $null=New-Item -ItemType Junction -Path $link -Target $outside
        try { Reject { Get-Snapshot $r }; Reject { Resolve-Safe $r 'junction/file' } } finally { [IO.Directory]::Delete($link) }
    }
    Test 'cancel request stops child and leaves stop receipt' {
        $script:Tick={ [Autoframe.Cancellation]::Requested=$true; Invoke-Tick }
        $dir=Join-Path $fixtureRoot 'cancel'
        try { Reject { Invoke-Child $pwsh @('-NoProfile','-Command','Start-Sleep -Seconds 30') $fixtureRoot $dir '' 30 5 } }
        finally { $script:Tick=$null; [Autoframe.Cancellation]::Requested=$false }
        Assert ((Read-Json (Join-Path $dir 'process.json')).stopped) 'cancelled tree stopped'
    }
    Test 'killed runner with corrupt before manifest recovers, without replaying Work' {
        $r=Fixture 'forced crash' 'work-sleep'
        $si=[Diagnostics.ProcessStartInfo]::new($pwsh)
        $si.UseShellExecute=$false; $si.CreateNoWindow=$true; $si.RedirectStandardOutput=$true; $si.RedirectStandardError=$true
        foreach($arg in @('-NoProfile','-File',$runner,'-ProjectRoot',$r,'-CodexCommand',$fake,'-MaxRunMinutes','10','-PhaseTimeoutMinutes','2','-SaveReserveMinutes','1','-StopTimeoutSeconds','5')) { $si.ArgumentList.Add($arg) }
        $outStream=[IO.File]::Create((Join-Path $fixtureRoot 'killed.stdout')); $errStream=[IO.File]::Create((Join-Path $fixtureRoot 'killed.stderr'))
        $p=[Diagnostics.Process]::Start($si); $outTask=$p.StandardOutput.BaseStream.CopyToAsync($outStream); $errTask=$p.StandardError.BaseStream.CopyToAsync($errStream)
        try {
            $timer=[Diagnostics.Stopwatch]::StartNew()
            while(-not [IO.File]::Exists((Join-Path $r 'out/T1.txt'))) {
                if($p.HasExited -or $timer.Elapsed.TotalSeconds -gt 90) { throw 'Fixture did not reach active Work.' }
                Start-Sleep -Milliseconds 100
            }
            $s=Read-Json (Join-Path $r '.autoframe/state.json') 'state'
            Assert ($s.running -and $s.active.phase -ceq 'Work') 'active receipt persisted before mutation'
            $p.Kill(); Assert ($p.WaitForExit(5000)) 'owned runner terminated'
            $null=$outTask.GetAwaiter().GetResult(); $null=$errTask.GetAwaiter().GetResult()
            $path=Join-Path $r ".autoframe/manifests/$($s.active.before_manifest).json"
            [IO.File]::WriteAllText($path,'{corrupt')
            Save-Json (Join-Path $r 'scenario.json') @{mode='success'}
            $s=Run $r @('-Resume')
            $history=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ })
            Assert (@($history | Where-Object { $_.Contains('kind') -and $_.kind -ceq 'estimated-time' }).Count -eq 1) 'crash time charged once'
            Assert (@($history | Where-Object { $_.Contains('kind') -and $_.kind -ceq 'incomplete-before' }).Count -eq 1) 'incomplete diff recorded'
            Assert (@($history | Where-Object { $_.Contains('phase') -and $_.phase -ceq 'Work' }).Count -eq 0) 'interrupted Work not blindly repeated'
            Assert ($s.status -ceq 'Complete') 'recovered to verified completion'
            $homePath=Join-Path $r '.autoframe'; $record=Save-Record $homePath @{fixture='regenerate'}
            [IO.File]::WriteAllText((Join-Path $homePath $record),'bad')
            $same=Save-Record $homePath @{fixture='regenerate'}
            Assert ($same -ceq $record -and (Read-Record $homePath $same).fixture -ceq 'regenerate') 'trusted regeneration restores hash-addressed content'
        } finally {
            if(-not $p.HasExited) { $p.Kill($true); $null=$p.WaitForExit(5000) }
            $p.Dispose(); $outStream.Dispose(); $errStream.Dispose()
        }
    }
    Test 'JSON timestamp-like strings retain their exact type and UTC representation' {
        $copy=Copy-Value ([Autoframe.Json]::Parse('{"date":"2026-09-14T08:40:56.1234567Z","A":1,"a":2}'))
        Assert ($copy.date -is [string] -and $copy.date -ceq '2026-09-14T08:40:56.1234567Z') 'no implicit date coercion'
        Assert ($copy['A'] -eq 1 -and $copy['a'] -eq 2) 'ordinal JSON keys'
    }
    Test 'Complete gate rejects a required generated artifact changed by final audit' {
        $r=Fixture 'generated artifact' 'audit-mutates-deliverable'; $p=(Read-Plan $r).data; $p.generated_scope=@('out/**'); Write-Plan $r $p
        $s=Run $r @() 6
        Assert ($s.message -like 'Completion evidence changed*') 'final gate checks outputs excluded from input signature'
    }
    Test 'PLAN change rejects stale Work and recovers current requirements' {
        $r=Fixture 'changed plan' 'plan-change'; $s=Run $r
        $history=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ })
        Assert (@($history | Where-Object { $_.Contains('kind') -and $_.kind -ceq 'plan-change' }).Count -eq 1) 'user edit recorded'
        Assert (@($history | Where-Object { $_.Contains('phase') -and $_.phase -ceq 'Work' }).Count -eq 0) 'stale Work not accepted'
        Assert ($s.status -ceq 'Complete') 'current inputs verified after recovery'
    }
    Test 'review: failed state publication cannot expose partial acceptance' {
        $r=Fixture 'failed publication'; $p=Read-Plan $r
        $settings=@{MaxRunMinutes=10;MaxPhaseAttempts=100;PhaseTimeoutMinutes=2;SaveReserveMinutes=1;StopTimeoutSeconds=5;MaxTasksPerWork=3;CompletionAuditInterval=3;CodexCommand=$fake;PhaseModels=@{}}
        $script:HomePath=Join-Path $r '.autoframe'; $script:StatePath=Join-Path $script:HomePath 'state.json'
        $script:State=New-State $r $p $settings $script:HomePath
        $script:StateHash=$null; $script:BaseElapsed=0.0; $script:RunClock=[Diagnostics.Stopwatch]::StartNew()
        Save-State; $before=Copy-Value $script:State; $candidate=Copy-Value $before
        $logical=Read-Record $script:HomePath $before.logical_ref 'logical'; $logical.tasks[0].status='implemented'
        $candidate.logical_ref=Save-Record $script:HomePath $logical; $candidate.base_plan_version++
        $candidate.accepted_attempts=@('fixture-acceptance'); $candidate.phase='Verify'; $candidate.pending_work=$true
        function Save-Json([string]$Path,$Value) { if($Value.Contains('accepted_attempts') -and $Value.accepted_attempts.Count) { throw 'Injected replacement failure' }; [Autoframe.Json]::Atomic($Path,(ConvertTo-Canonical $Value)) }
        Reject { Save-State $candidate }
        Assert ($script:State.logical_ref -ceq $before.logical_ref -and -not $script:State.accepted_attempts.Count) 'unpublished logical version is not installed'
        Save-State
        $saved=Read-Json $script:StatePath 'state'
        Assert ($saved.logical_ref -ceq $before.logical_ref -and $saved.base_plan_version -eq $before.base_plan_version -and -not $saved.pending_work) 'cleanup/heartbeat cannot publish rejected changes'
    }
    Test 'review: evidence publication is atomic and recovers corrupt destinations' {
        $r=Fixture 'evidence atomic'; $src=Join-Path $r 'proof.txt'; $dest=Join-Path $r 'saved.evidence'
        [IO.File]::WriteAllText($src,'authentic proof'); $hash=[Autoframe.Json]::FileHash($src)
        [IO.File]::WriteAllText($dest,'interrupted copy')
        Reject { [Autoframe.Json]::StoreEvidence($src,$dest,('0'*64),$null) }
        Assert ([IO.File]::ReadAllText($dest) -ceq 'interrupted copy') 'mismatched source cannot replace destination'
        [Autoframe.Json]::StoreEvidence($src,$dest,$hash,$null)
        Assert ([Autoframe.Json]::FileHash($dest) -ceq $hash) 'verified copy restored'
        Assert (@(Get-ChildItem -LiteralPath $r -Filter '*.corrupt-*').Count -eq 1) 'corruption retained'
        Reject { [Autoframe.Json]::StoreEvidence($src,$dest,$hash,[Action]{ throw [OperationCanceledException]::new() }) }
        Assert ([Autoframe.Json]::FileHash($dest) -ceq $hash -and -not @(Get-ChildItem -LiteralPath $r -Filter '*.tmp').Count) 'cancellation leaves valid canonical evidence'
    }
    Test 'review: partial recovery cannot discard other interrupted Work targets' {
        $r=Fixture 'subset recovery' 'work-crash' 2; $s=Run $r @() 6
        Save-Json (Join-Path $r 'scenario.json') @{mode='recover-subset'}
        $s=Run $r @('-Resume')
        $history=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ })
        $verify=@($history | Where-Object { $_.Contains('phase') -and $_.phase -ceq 'Verify' })
        Assert ($verify[0].task_results.Count -eq 2 -and -not $s.uncertain.Count) 'all old targets explicitly verified'
    }
    Test 'review: pending accepted Work survives a partial recovery plan' {
        $r=Fixture 'pending subset' 'success' 2; $s=Run $r @('-MaxPhaseAttempts','4') 2
        Save-Json (Join-Path $r 'scenario.json') @{mode='recover-subset'}
        $s=Run $r @('-Resume','-MaxPhaseAttempts','20')
        $history=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ })
        $verify=@($history | Where-Object { $_.Contains('phase') -and $_.phase -ceq 'Verify' })
        Assert ($verify[0].task_results.Count -eq 2 -and -not $s.pending_work) 'last batch verification is complete'
    }
    Test 'review: protected overrides and Windows aliases are rejected' {
        Assert (Test-Protected $fixtureRoot 'src/AGENTS.override.md') 'override instruction protected'
        foreach($path in @('PLAN.md.','dir /x','nul.txt','COM1','out/a|b','out/a"b')) { Reject { Assert-Relative $path } }
    }
    Test 'review: transient enumeration failure retries but cancellation propagates' {
        $script:TreeCalls=0
        function Get-Tree { $script:TreeCalls++; if($script:TreeCalls -eq 1) { throw [IO.FileNotFoundException]::new('concurrent deletion') }; return ,@(@{path='a';kind='file';hash=('0'*64)}) }
        Assert ((Get-Snapshot $fixtureRoot).Count -eq 1 -and $script:TreeCalls -eq 3) 'stable pair after retry'
        function Get-Tree { throw [OperationCanceledException]::new() }
        Reject { Get-Snapshot $fixtureRoot }
    }
    Test 'review: repeated proofs and optional resolutions do not reset stall counters' {
        $r=Fixture 'progress identity'; $l=New-Logical (Read-Plan $r).data
        $e=@{id='old';kind='task';phase='Verify';target_ids=@('T1');hash=('0'*64);input_signature=('1'*64);artifact_manifest=('2'*64)}
        $l.evidence=@($e); $l.tasks[0].evidence_refs=@('old')
        $result=@{task_results=@(@{id='T1';status='verified';evidence_ids=@('new')});evidence=@((Copy-Value $e));progress=@();changes=@()}; $result.evidence[0].id='new'
        Assert (-not (Test-MeaningfulProgress $l $result)) 'same proof after pending does not count'
        $result.evidence[0].input_signature='3'*64; Assert (Test-MeaningfulProgress $l $result) 'current input revalidation counts'
        $l.findings=@(@{id='F1';required=$false;status='open'}); $result.task_results=@(); $result.evidence=@(); $result.changes=@(@{kind='finding';id='F1';set=@{status='resolved'}})
        Assert (-not (Test-MeaningfulProgress $l $result)) 'optional resolution does not reset'
        $l.findings[0].required=$true; Assert (Test-MeaningfulProgress $l $result) 'new required resolution counts'
        $l.findings[0].status='resolved'; Assert (-not (Test-MeaningfulProgress $l $result)) 'repeated resolution does not reset'
    }
    Test 'review: audit deduplication uses proof contents rather than new IDs' {
        $r=Fixture 'audit identity'; $l=New-Logical (Read-Plan $r).data
        $l.evidence=@(@{id='old';kind='task';phase='Verify';target_ids=@('T1');hash=('0'*64);input_signature=('1'*64);artifact_manifest=('2'*64)})
        $l.tasks[0].evidence_refs=@('old'); $a=Get-AuditKey 'plan' $l 'signature'
        $l.evidence[0].id='new'; $l.tasks[0].evidence_refs=@('new')
        Assert ((Get-AuditKey 'plan' $l 'signature') -ceq $a) 'renamed proof does not relaunch audit'
        $l.evidence[0].hash='3'*64; Assert ((Get-AuditKey 'plan' $l 'signature') -cne $a) 'changed evidence content remains significant'
    }
    Test 'review: Work requires intact accepted Audit evidence' {
        $r=Fixture 'audit corruption'; $s=Run $r @('-MaxPhaseAttempts','3') 2
        $homePath=Join-Path $r '.autoframe'; $l=Read-Record $homePath $s.logical_ref 'logical'
        $e=@($l.evidence | Where-Object { $_.phase -ceq 'Audit' })[0]
        $context=@{input_plan_hash=$s.plan_hash;signature=$e.input_signature}
        Assert-ApprovalProofs $s $l $context $homePath
        [IO.File]::WriteAllText((Join-Path $homePath $e.record_path),'corrupt')
        Reject { Assert-ApprovalProofs $s $l $context $homePath }
    }
    Test 'review: stale finding resolutions reopen and cancelled validation stops' {
        $r=Fixture 'finding stale'; $homePath=Join-Path $r '.autoframe'; $l=New-Logical (Read-Plan $r).data
        $snapshot=Get-Snapshot $r; $manifest=Save-Record $homePath (New-Manifest $snapshot @() -FilesOnly) 'manifests'
        $path=Join-Path $r 'proof'; [IO.File]::WriteAllText($path,'finding verified'); $hash=[Autoframe.Json]::FileHash($path)
        [Autoframe.Json]::StoreEvidence($path,(Join-Path $homePath "records/$hash.evidence"),$hash,$null)
        $l.findings=@(@{id='F1';kind='product';required=$true;content='issue';resolution='check';task_ids=@('T1');status='resolved';evidence_refs=@('E1')})
        $l.evidence=@(@{id='E1';kind='finding';phase='Verify';target_ids=@('F1');hash=$hash;input_signature='signature';artifact_manifest=[IO.Path]::GetFileNameWithoutExtension($manifest);record_path="records/$hash.evidence"})
        Assert (-not (Update-StaleEvidence $l 'signature' $homePath $snapshot)) 'current resolution remains valid'
        Assert (Update-StaleEvidence $l 'changed-signature' $homePath $snapshot) 'changed inputs invalidate resolution'
        Assert ($l.findings[0].status -ceq 'open' -and -not $l.findings[0].evidence_refs.Count) 'stale required finding returns to review'
        $l.findings[0].status='resolved'; $l.findings[0].evidence_refs=@('E1')
        # Cancellation is not evidence corruption and must not be swallowed by revalidation.
        function New-Manifest { throw [OperationCanceledException]::new() }
        Reject { Update-StaleEvidence $l 'signature' $homePath $snapshot }
    }
    Test 'review: remaining work cannot be accepted as verified' {
        $r=Fixture 'unfinished verified' 'verified-remaining'; $s=Run $r @() 6
        $l=Read-Record (Join-Path $r '.autoframe') $s.logical_ref 'logical'
        Assert ($l.tasks[0].status -ceq 'implemented' -and $s.pending_work -and $s.accepted_attempts.Count -eq 4) 'invalid Verify has no partial state adoption'
    }
    Test 'review: Plan cannot bypass verification of accepted Work' {
        $r=Fixture 'skip verify'; $s=Run $r @('-MaxPhaseAttempts','4') 2
        Save-Json (Join-Path $r 'scenario.json') @{mode='skip-pending-verify'}
        $s=Run $r @('-Resume','-MaxPhaseAttempts','20') 6
        Assert ($s.pending_work -and $s.message -like 'Pending Work must pass Verify*') 'recovery cannot launch new Work early'
    }
    Test 'review: exhausted Resume performs no CLI preflight launches' {
        $r=Fixture 'budget preflight'; $s=Run $r @('-MaxPhaseAttempts','1') 2
        $s.elapsed_seconds=$s.settings.MaxRunMinutes*60
        Save-Json (Join-Path $r '.autoframe/state.json') $s
        $count=@(Get-ChildItem -LiteralPath (Join-Path $r '.autoframe/runs') -Directory -Filter 'preflight-*').Count
        $s=Run $r @('-Resume') 2
        Assert (@(Get-ChildItem -LiteralPath (Join-Path $r '.autoframe/runs') -Directory -Filter 'preflight-*').Count -eq $count) 'budget also applies before Worker launch'
    }
    Test 'review: NewRun archives lone corrupt state without a records directory' {
        $r=Fixture 'lone corrupt state'; $homePath=Join-Path $r '.autoframe'; $null=[IO.Directory]::CreateDirectory($homePath)
        [IO.File]::WriteAllText((Join-Path $homePath 'state.json'),'{corrupt')
        $s=Run $r @('-NewRun')
        Assert (@(Get-ChildItem -LiteralPath (Join-Path $homePath 'records') -Filter 'previous-*.json').Count -eq 1 -and $s.status -ceq 'Complete') 'corruption preserved and recovered'
    }
    Test 'review: missing job does not prove a matching launcher has exited' {
        $r=Fixture 'missing job'; $homePath=Join-Path $r '.autoframe'; $dir=Join-Path $homePath 'runs/fixture'
        $null=[IO.Directory]::CreateDirectory($dir)
        $p=Start-Process -FilePath $pwsh -WindowStyle Hidden -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 30') -PassThru
        try {
            $receipt=@{pid=$p.Id;start_utc=$p.StartTime.ToUniversalTime().ToString('O');job=('Local\autoframe-'+[guid]::NewGuid().ToString('N'));stopped=$false}
            Save-Json (Join-Path $dir 'process.json') $receipt
            Reject { Stop-OldChildren $homePath 1 }
            Assert (-not (Read-Json (Join-Path $dir 'process.json')).stopped -and -not $p.HasExited) 'unconfirmed live process is neither accepted nor blindly killed'
            $receipt.start_utc=[datetime]::UtcNow.AddDays(-1).ToString('O'); Save-Json (Join-Path $dir 'process.json') $receipt
            Stop-OldChildren $homePath 1
            Assert (-not $p.HasExited) 'reused PID is left alone'
        } finally { if(-not $p.HasExited) { $p.Kill(); $null=$p.WaitForExit(5000) }; $p.Dispose() }
    }
    Test 'review: narrow input scopes still track inherited instructions' {
        $r=Fixture 'instruction identity'; $p=(Read-Plan $r).data; $p.input_scope=@('protected.txt'); $p.generated_scope=@('cache/**')
        $dir=Join-Path $r 'cache'; $null=[IO.Directory]::CreateDirectory($dir); $path=Join-Path $dir 'AGENTS.override.md'
        [IO.File]::WriteAllText($path,'original instruction'); $a=Get-ObjectHash (Get-InputManifest $r $p (Get-Snapshot $r))
        [IO.File]::WriteAllText($path,'changed instruction')
        Assert ((Get-ObjectHash (Get-InputManifest $r $p (Get-Snapshot $r))) -cne $a) 'generated/narrow scopes cannot hide applicable instructions'
        $child=Join-Path $r 'child'; $null=[IO.Directory]::CreateDirectory($child); $path=Join-Path $r 'AGENTS.md'
        [IO.File]::WriteAllText($path,'ancestor instruction'); $a=Get-ObjectHash (Get-AncestorInstructions $child)
        [IO.File]::WriteAllText($path,'changed ancestor')
        Assert ((Get-ObjectHash (Get-AncestorInstructions $child)) -cne $a) 'parent instructions are part of environment identity'
    }
    Test 'review: Audit proof must cover every selected Work target' {
        $r=Fixture 'audit uncovered' 'audit-uncovered'; $s=Run $r @() 6
        Assert ($s.attempts -eq 3 -and $s.accepted_attempts.Count -eq 2 -and -not (Test-Path -LiteralPath (Join-Path $r 'out/T1.txt'))) 'unrelated audit proof cannot authorize Work'
    }
    Test 'spec: user dependency and criterion mappings cannot be removed' {
        $r=Fixture 'user requirements' -Count 2 -ReadOnly; $p=(Read-Plan $r).data
        $p.tasks[1].depends_on=@('T1')
        $l=New-Logical $p; $l.tasks[1].depends_on=@()
        Reject { Assert-Logical $l $p }
        $l=New-Logical $p; $l.tasks[1].criterion_ids=@()
        Reject { Assert-Logical $l $p }
        # Refinement may preserve the dependency through a new intermediate task.
        $l=New-Logical $p; $middle=Copy-Value $l.tasks[0]; $middle.id='I1'; $middle.source_ids=@(); $middle.depends_on=@('T1')
        $l.tasks+=@($middle); $l.tasks[1].depends_on=@('I1')
        Assert-Logical $l $p
    }
    Test 'spec: replacements preserve ordering and milestone membership' {
        $r=Fixture 'replacement requirements' -Count 2 -ReadOnly; $p=(Read-Plan $r).data
        $p.tasks[1].depends_on=@('T1'); $p.milestones=@(@{id='M1';description='milestone';task_ids=@('T2');acceptance='T2 done'})
        $l=New-Logical $p; $replacement=Copy-Value $l.tasks[1]; $replacement.id='R2'
        $l.tasks+=@($replacement); $l.tasks[1].status='superseded'; $l.tasks[1].replacements=@('R2')
        $l.milestones[0].task_ids=@('R2'); Assert-Logical $l $p
        Assert ($p.milestones[0].task_ids[0] -ceq 'T2') 'internal milestone changes cannot mutate PLAN data'
        $l.milestones[0].task_ids=@(); Reject { Assert-Logical $l $p }
        $l.milestones[0].task_ids=@('R2'); $replacement.depends_on=@(); Reject { Assert-Logical $l $p }
    }
    Test 'spec: recovery-only partial progress does not clear planning stalls' {
        $r=Fixture 'recovery progress' -ReadOnly; $l=New-Logical (Read-Plan $r).data
        $result=@{phase='Verify';decision='revise';task_results=@(@{id='T1';status='pending';evidence_ids=@('E1')});changes=@();progress=@(@{task_id='T1';evidence_ids=@('E1')});evidence=@(@{id='E1';hash=('0'*64)})}
        Assert (Test-MeaningfulProgress $l $result) 'cause identification can count after Work'
        Assert (-not (Test-MeaningfulProgress $l $result -CompletionOnly)) 'recovery requires a newly verified task or required resolution'
        $state=@{status='Running';phase='Verify';pending_work=$false;pending_route=$null;counters=@{no_progress_works=0;workless_plan_returns=2;works_since_audit=0;work_since_plan=$false};settings=@{CompletionAuditInterval=3;MaxRunMinutes=10;MaxPhaseAttempts=100};elapsed_seconds=0;attempts=5}
        $next=Get-Transition $state $result $l $true 'audit' $false
        Assert ($next.counters.workless_plan_returns -eq 2) 'partial recovery does not reset the counter'
        $next=Get-Transition $state $result $l $true 'audit' $true
        Assert ($next.counters.workless_plan_returns -eq 0) 'new recovery completion clears planning stalls'
    }
    Test 'spec: adapter preserves native and script exit codes' {
        $dir=Join-Path $fixtureRoot 'native exit'
        $result=Invoke-Child $pwsh @('-NoProfile','-Command','exit 19') $fixtureRoot $dir '' 10 5
        Assert ($result.exit_code -eq 19) 'native exit is not collapsed to 1'
        $scriptPath=Join-Path $fixtureRoot 'exit script.ps1'; [IO.File]::WriteAllText($scriptPath,'exit 23')
        $result=Invoke-Child $scriptPath @() $fixtureRoot (Join-Path $fixtureRoot 'script exit') '' 10 5
        Assert ($result.exit_code -eq 23) 'PowerShell launcher preserves script exit'
        $cmdPath=Join-Path $fixtureRoot 'exit launcher.cmd'; [IO.File]::WriteAllText($cmdPath,"@echo off`r`nexit /b 27`r`n")
        $result=Invoke-Child $cmdPath @() $fixtureRoot (Join-Path $fixtureRoot 'cmd exit') '' 10 5
        Assert ($result.exit_code -eq 27) 'cmd launcher preserves exit'
    }
    Test 'spec: missing completion evidence reroutes Resume to a fresh audit' {
        $r=Fixture 'missing completion proof' -ReadOnly; $s=Run $r
        $homePath=Join-Path $r '.autoframe'; $l=Read-Record $homePath $s.logical_ref 'logical'
        $e=@($l.evidence | Where-Object { $_.kind -ceq 'criterion' })[0]
        [IO.File]::Delete((Join-Path $homePath $e.record_path))
        $s=Run $r @('-Resume')
        $history=@($s.history | ForEach-Object { Read-Record $homePath $_ } | Where-Object { $_.Contains('phase') })
        Assert (@($history | Where-Object { $_.phase -ceq 'CompletionAudit' }).Count -eq 2) 'completion proof regenerated by a new accepted audit'
        Assert (@($history | Where-Object { $_.phase -ceq 'Work' }).Count -eq 1) 'completed work is not repeated'
    }
    Test 'spec: corrupt completion evidence at launch cap pauses and can recover' {
        $r=Fixture 'corrupt completion proof' -ReadOnly; $s=Run $r @('-MaxPhaseAttempts','6')
        $homePath=Join-Path $r '.autoframe'; $l=Read-Record $homePath $s.logical_ref 'logical'
        $e=@($l.evidence | Where-Object { $_.kind -ceq 'criterion' })[0]
        [IO.File]::WriteAllText((Join-Path $homePath $e.record_path),'corrupt')
        $s=Run $r @('-Resume','-MaxPhaseAttempts','6') 2
        Assert ($null -eq $s.complete_signature -and $null -eq $s.last_audit_key -and $s.attempts -eq 6) 'expired completion cannot bypass the cap or suppress the next audit'
        $s=Run $r @('-Resume','-MaxPhaseAttempts','20')
        Assert ($s.status -ceq 'Complete') 'fresh verification recovers corrupt proof'
    }
    Test 'spec: NewRun recovers accepted but unverified Work without replay' {
        $r=Fixture 'new run pending work'; $old=Run $r @('-MaxPhaseAttempts','4') 2
        $s=Run $r @('-NewRun','-MaxPhaseAttempts','20')
        $history=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ } | Where-Object { $_.Contains('phase') })
        Assert ($s.run_id -cne $old.run_id -and $s.status -ceq 'Complete') 'new identity with verified artifacts'
        Assert (@($history | Where-Object { $_.phase -ceq 'Work' }).Count -eq 0 -and @($history | Where-Object { $_.phase -ceq 'Verify' }).Count -eq 1) 'previous effects inspected before repeating Work'
    }
    Test 'spec: milestone uses all replacement proofs and deduplicates unchanged evidence' {
        $r=Fixture 'replacement milestone' -ReadOnly; $p=(Read-Plan $r).data
        $p.milestones=@(@{id='M1';description='milestone';task_ids=@('T1');acceptance='reviewed'})
        $l=New-Logical $p; $replacement=Copy-Value $l.tasks[0]; $replacement.id='R1'; $replacement.status='verified'; $replacement.evidence_refs=@('E1')
        $l.tasks+=@($replacement); $l.tasks[0].status='superseded'; $l.tasks[0].replacements=@('R1')
        $l.evidence=@(@{id='E1';kind='task';phase='Verify';hash=('0'*64);target_ids=@('R1');input_signature=('1'*64);artifact_manifest=('2'*64)})
        $keys=@(Get-NewMilestoneKeys $l 'signature' @())
        Assert ($keys.Count -eq 1) 'verified replacement reaches the original milestone'
        Assert (@(Get-NewMilestoneKeys $l 'signature' $keys).Count -eq 0) 'same proof does not retrigger the milestone'
        $second=Copy-Value $replacement; $second.id='R2'; $second.status='pending'; $second.evidence_refs=@(); $l.tasks+=@($second); $l.tasks[0].replacements+=@('R2')
        Assert (@(Get-NewMilestoneKeys $l 'signature' @()).Count -eq 0) 'every replacement must be verified'
    }
    Test 'spec: milestone triggers CompletionAudit before the next Work' {
        $r=Fixture 'milestone integration' 'milestone' 2; $p=(Read-Plan $r).data
        $p.tasks[1].depends_on=@('T1'); $p.milestones=@(@{id='M1';description='first task done';task_ids=@('T1');acceptance='T1 verified'}); Write-Plan $r $p
        $s=Run $r
        $history=@($s.history | ForEach-Object { Read-Record (Join-Path $r '.autoframe') $_ } | Where-Object { $_.Contains('phase') })
        $firstAudit=-1; $secondWork=-1; $works=0
        for($i=0;$i -lt $history.Count;$i++) {
            if($history[$i].phase -ceq 'CompletionAudit' -and $firstAudit -lt 0) { $firstAudit=$i }
            if($history[$i].phase -ceq 'Work') { $works++; if($works -eq 2) { $secondWork=$i } }
        }
        Assert ($firstAudit -gt 0 -and $firstAudit -lt $secondWork) 'milestone audit is independent of the Work interval and whole-project completion'
        Assert ($s.status -ceq 'Complete') 'incomplete milestone audit returns to Plan and finishes remaining work'
    }
    Test 'milestone: template is valid and covers every required task' {
        $r=Fixture 'milestone template'
        Copy-Item -LiteralPath (Join-Path $script:FrameRoot 'templates/PLAN.template.md') -Destination (Join-Path $r 'PLAN.md') -Force
        $p=(Read-Plan $r).data
        Assert ($p.tasks.Count -gt 0 -and $p.milestones.Count -gt 0) 'initial tasks and milestones exist'
        $covered=@($p.milestones | ForEach-Object { Assert ($_.task_ids.Count -gt 0) 'nonempty milestone'; $_.task_ids })
        foreach($task in $p.tasks | Where-Object { $_.required }) { Assert ($task.id -cin $covered) 'required task covered by a milestone' }
    }
    Test 'milestone: progress counts replacements and loses readiness after invalidation' {
        $r=Fixture 'milestone progress' 'success' 2; $p=(Read-Plan $r).data
        $p.milestones=@(@{id='M1';description='checkpoint';task_ids=@('T1','T2');acceptance='verified'},@{id='M0';description='legacy empty';task_ids=@();acceptance='legacy'})
        $l=New-Logical $p; $l.tasks[0].status='verified'
        $m=@(Get-MilestoneProgress $l)[0]
        Assert ($m.verified_count -eq 1 -and $m.total_tasks -eq 2 -and -not $m.ready_for_audit) 'partial progress'
        $l.tasks[1].status='superseded'; $l.tasks[1].replacements=@('R1','R2')
        foreach($id in @('R1','R2')) { $t=Copy-Value $l.tasks[0]; $t.id=$id; $l.tasks+=@($t) }
        $m=@(Get-MilestoneProgress $l)[0]
        Assert ($m.total_tasks -eq 3 -and $m.verified_count -eq 3 -and $m.ready_for_audit) 'all replacement tasks counted'
        $l.tasks[-1].status='pending'
        $progress=@(Get-MilestoneProgress $l)
        Assert ($progress[0].verified_count -eq 2 -and -not $progress[0].ready_for_audit) 'invalidated status removes readiness'
        Assert ($progress[1].total_tasks -eq 0 -and -not $progress[1].ready_for_audit) 'empty legacy milestone is not ready'
        Assert (@(Get-NewMilestoneKeys $l 'signature' @()).Count -eq 0) 'unready milestones do not trigger audits'
    }
    Test 'milestone: generated checkpoints reach audit with progress and unchanged PLAN' {
        $r=Fixture 'automatic milestones' 'auto-milestone' 2 -ReadOnly; $p=(Read-Plan $r).data
        $p.tasks[1].depends_on=@('T1'); Write-Plan $r $p
        $before=(Read-Plan $r).hash; $s=Run $r
        $l=Read-Record (Join-Path $r '.autoframe') $s.logical_ref 'logical'
        Assert ($s.status -ceq 'Complete' -and $l.milestones.Count -eq 2) 'generated milestones retained'
        Assert ((Read-Plan $r).hash -ceq $before) 'PLAN remains read-only'
        $inputs=@(Get-ChildItem -LiteralPath (Join-Path $r '.autoframe/runs') -Filter input.json -Recurse | ForEach-Object { Read-Json $_.FullName })
        $audit=@($inputs | Where-Object { $_.phase -ceq 'CompletionAudit' -and $_.milestone_progress[0].ready_for_audit -and -not $_.milestone_progress[1].ready_for_audit })
        Assert ($audit.Count -eq 1) 'first milestone audited while the second remains unfinished'
        Assert ($audit[0].milestone_progress[0].verified_count -eq 1 -and $audit[0].milestone_progress[1].verified_count -eq 0) 'Worker receives exact progress'
        $logs=@(Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.txt' | Where-Object { [IO.File]::ReadAllText($_.FullName).Contains('Milestone M-T1: 1/1 verified (audit candidate)') })
        Assert ($logs.Count -gt 0) 'verified milestone progress is visible in runner output'
    }
    $succeeded=$true
    Write-Host "$script:Passed tests passed. Fixtures: $fixtureRoot"
} finally {
    # Preserve failed fixtures for diagnosis. Successful fixtures are kept only on request.
    if(-not $KeepFixtures -and (Get-Variable succeeded -ErrorAction SilentlyContinue) -and $succeeded) {
        $resolved=[IO.Path]::GetFullPath($fixtureRoot)
        $allowed=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if($resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('autoframe-tests-')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
