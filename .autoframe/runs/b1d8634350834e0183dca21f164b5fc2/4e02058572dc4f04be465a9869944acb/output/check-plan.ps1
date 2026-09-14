$ErrorActionPreference = 'Stop'
$root = 'C:/Users/bwff1/repos/archi-Doc/Kimigayo'
$inputPath = Join-Path $root '.autoframe/runs/b1d8634350834e0183dca21f164b5fc2/4e02058572dc4f04be465a9869944acb/input.json'
$inputData = Get-Content -Raw -LiteralPath $inputPath | ConvertFrom-Json
$out = $inputData.output_directory
$plan = Get-Content -Raw -LiteralPath $inputData.plan_record | ConvertFrom-Json
$logical = Get-Content -Raw -LiteralPath $inputData.logical_path | ConvertFrom-Json
function Save-Json($name, $data) { $data | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $out $name) -Encoding utf8NoBOM }
function Sha($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Assert-Plan($condition, $message) { if (-not $condition) { throw $message } }
function Canon($data) {
    if ($null -eq $data) { return $null }
    if ($data -is [Collections.IDictionary]) {
        $result=[ordered]@{}
        foreach ($key in @($data.Keys | Sort-Object -CaseSensitive)) { $result[$key]=Canon $data[$key] }
        return $result
    }
    if ($data -is [pscustomobject]) {
        $result=[ordered]@{}
        foreach ($key in @($data.PSObject.Properties.Name | Sort-Object -CaseSensitive)) { $result[$key]=Canon $data.$key }
        return $result
    }
    if ($data -is [array]) { $result=@(foreach ($item in $data) { ,(Canon $item) }); return ,$result }
    return $data
}
function Json($data) { ConvertTo-Json -InputObject (Canon $data) -Depth 100 -Compress }

$initialStatus = @(git -C $root status --short)
$trackedPaths = @(git -C $root ls-files -- Kimi xUnitTest backend/windows-x64 examples Benchmark SPEC.md STATUS.md README.md Directory.Build.props global.json Kimigayo.slnx .gitignore PLAN.md AGENTS.md autoimpl/schemas/result.schema.json)
$before = @{}
foreach ($path in $trackedPaths) { $before[$path] = Sha (Join-Path $root $path) }
$embedded = [regex]::Match((Get-Content -Raw -LiteralPath $inputData.plan_path), '(?s)<!-- autoframe:begin -->\s*```json\s*(.*?)\s*```')
Assert-Plan $embedded.Success 'PLAN.md JSON block missing'
$sourcePlan = $embedded.Groups[1].Value | ConvertFrom-Json
foreach ($property in $plan.PSObject.Properties.Name) {
    Assert-Plan ((Json $sourcePlan.$property) -ceq (Json $plan.$property)) "PLAN.md and accepted plan differ: $property"
}
foreach ($task in $logical.tasks) {
    $sourceTask = $plan.tasks | Where-Object id -CEQ $task.id
    Assert-Plan ($null -ne $sourceTask) "Unknown current task: $($task.id)"
    foreach ($key in @('id','description','required','depends_on','criterion_ids','acceptance','verification')) {
        Assert-Plan ((Json $sourceTask.$key) -ceq (Json $task.$key)) "Current task differs from source: $($task.id).$key"
    }
}
Assert-Plan ($logical.findings.Count -eq 0 -and $logical.evidence.Count -eq 0) 'Unexpected current findings/evidence'
Assert-Plan (-not $inputData.pending_work -and $inputData.uncertain.Count -eq 0 -and $inputData.verify_targets.Count -eq 0) 'Recovery state changed'

$deliverables = @{
    T01 = @('STATUS.md')
    T02 = @('Kimi/Compiler/Lexing/**','Kimi/Compiler/Parsing/**','Kimi/Compiler/Core/**','Kimi/Compiler/Helper/**','xUnitTest/Tests/**','STATUS.md')
    T03 = @('Kimi/Compiler/Core/**','Kimi/Compiler/Parsing/**','Kimi/SolutionAndProject/**','xUnitTest/Tests/**','STATUS.md')
    T04 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Parsing/Koto/Types/**','xUnitTest/Tests/**','STATUS.md')
    T05 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Core/**','xUnitTest/Tests/**','STATUS.md')
    T06 = @('Kimi/Compiler/Binding/**','xUnitTest/Tests/**','STATUS.md')
    T07 = @('Kimi/Compiler/Binding/**','xUnitTest/Tests/**','STATUS.md')
    T08 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','STATUS.md')
    T09 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','STATUS.md')
    T10 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Helper/**','Kimi/Compiler/FloatingTypes.cs','Kimi/Compiler/ScalarTypes.cs','xUnitTest/Tests/**','STATUS.md')
    T11 = @('Kimi/Compiler/Analysis/**','Kimi/Compiler/Binding/**','xUnitTest/Tests/**','STATUS.md')
    T12 = @('Kimi/Compiler/Analysis/**','Kimi/Compiler/Binding/**','Kimi/Compiler/ReferenceTypes.cs','xUnitTest/Tests/**','STATUS.md')
    T13 = @('Kimi/Compiler/Analysis/**','Kimi/Compiler/Binding/**','xUnitTest/Tests/**','STATUS.md')
    T14 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','STATUS.md')
    T15 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','STATUS.md')
    T16 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','STATUS.md')
    T17 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','SPEC.md','STATUS.md')
    T18 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','STATUS.md')
    T19 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','STATUS.md')
    T20 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Core/**','xUnitTest/Tests/**','STATUS.md')
    T21 = @('Kimi/Compiler/Core/**','Kimi/Compiler/Binding/**','Kimi/SolutionAndProject/**','xUnitTest/Tests/**','SPEC.md','STATUS.md')
    T22 = @('Kimi/Compiler/Core/**','Kimi/SolutionAndProject/**','xUnitTest/Tests/**','bin/mod-tests/**','SPEC.md','STATUS.md')
    T23 = @('Kimi/Compiler/Emission/**','xUnitTest/Tests/**','backend/windows-x64/tests/**','bin/emission-fixtures/**','bin/emission-native/**','STATUS.md')
    T24 = @('Kimi/Compiler/Emission/**','Kimi/Compiler/Analysis/**','xUnitTest/Tests/**','bin/emission-fixtures/**','STATUS.md')
    T25 = @('Kimi/Compiler/Emission/**','xUnitTest/Tests/**','backend/windows-x64/test-emission.ps1','bin/emission-fixtures/**','bin/emission-native/**','SPEC.md','STATUS.md')
    T26 = @('Kimi/Compiler/Emission/**','xUnitTest/Tests/**','backend/windows-x64/tests/**','backend/windows-x64/test-emission.ps1','bin/emission-fixtures/**','bin/emission-native/**','STATUS.md')
    T27 = @('Kimi/Compiler/Emission/**','xUnitTest/Tests/**','backend/windows-x64/test-emission.ps1','bin/emission-fixtures/**','bin/emission-native/**','STATUS.md')
    T28 = @('Kimi/Compiler/Emission/**','xUnitTest/Tests/**','backend/windows-x64/test-scalars.ps1','bin/scalar-fixtures/**','bin/scalar-native/**','bin/emission-fixtures/**','bin/emission-native/**','STATUS.md')
    T29 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Emission/**','Kimi/SolutionAndProject/**','xUnitTest/Tests/**','backend/windows-x64/tests/**','bin/emission-fixtures/**','bin/emission-native/**','STATUS.md')
    T30 = @('Kimi/Compiler/Binding/**','Kimi/Compiler/Analysis/**','Kimi/Compiler/Emission/**','xUnitTest/Tests/**','backend/windows-x64/tests/**','examples/Hello/**','bin/emission-fixtures/**','bin/emission-native/**','STATUS.md')
    T31 = @('Kimi/Compiler/Emission/**','Kimi/SolutionAndProject/**','Kimi/Unit/Command/**','xUnitTest/Tests/**','backend/windows-x64/test-cli.ps1','bin/cli-tests/**','examples/**/bin/**','STATUS.md')
    T32 = @('backend/windows-x64/**','Kimi/Compiler/Emission/**','Directory.Build.props','backend/windows-x64/bin/**','toolchain/windows_x64/**','STATUS.md')
    T33 = @('xUnitTest/Tests/**','backend/windows-x64/**','examples/**','Kimi/bin/**','xUnitTest/bin/**','bin/**','TestResults/**','xUnitTest/TestResults/**','STATUS.md')
    T34 = @('Kimi/Compiler/**','xUnitTest/Tests/**','Benchmark/**','BenchmarkDotNet.Artifacts/**','SPEC.md','STATUS.md')
    T35 = @('SPEC.md','STATUS.md','README.md','examples/**')
}
$changes = [Collections.Generic.List[object]]::new()
foreach ($task in $logical.tasks) {
    $set = [ordered]@{ description=$null; required=$null; depends_on=$null; criterion_ids=$null; acceptance=$null; verification=$null; source_ids=$null; deliverables=$deliverables[$task.id]; replacements=$null }
    if ($task.id -ceq 'T02') { $set.depends_on = @($task.depends_on) + 'T36' }
    if ($task.id -ceq 'T34') { $set.depends_on = @($task.depends_on) + 'T36' }
    $changes.Add([ordered]@{kind='task';id=$task.id;set=$set})
}
$baseline = [ordered]@{
    description='T34の既存比較要件の基準側を先行実施する。T01後、T02以降の実装変更前に既存managed workloadとallocation保証の基準測定を保存する。'
    required=$true
    depends_on=@('T01')
    criterion_ids=@('C15')
    acceptance='変更前のソース・SDK/runtime・OS/CPU・構成・workload/引数・件数に結び付く基準測定とallocation結果が保存され、T34で同条件の変更後結果と比較できる。未実行・0件・失敗を基準成功にしない。既存要件に性能改善率や全経路zero-allocationを追加しない。'
    verification='Releaseを直列にbuildし、既存allocation/保持参照テストを件数付きで検証する。T34指定の通常managed benchmarkコマンドに --artifacts BenchmarkDotNet.Artifacts/baseline を付けてBindingBenchmark・OwnershipAnalysisBenchmark・FrontEndBenchmarkの基準結果を保存する。ログ・source/workload hash・環境・コマンド・成功/失敗/未実行を当該試行のoutput_directoryへ保存し、集計と生データをT34へ引き継ぐ。分割実行は全ケースの台帳で欠落を確認する。NativeAOTは実行しない。'
    source_ids=@('T34')
    deliverables=@('BenchmarkDotNet.Artifacts/baseline/**','Benchmark/bin/Release/**','Kimi/bin/Release/**','xUnitTest/bin/Release/**')
    replacements=@()
}
$changes.Add([ordered]@{kind='task';id='T36';set=$baseline})
$m11 = $logical.milestones | Where-Object id -CEQ 'M11'
$changes.Add([ordered]@{kind='milestone';id='M11';set=[ordered]@{description=$null;task_ids=@($m11.task_ids)+'T36';acceptance=$null}})
Save-Json 'proposed-changes.json' @($changes)

$updated = Json $logical | ConvertFrom-Json
foreach ($change in $changes) {
    $collection = if ($change.kind -ceq 'task') { 'tasks' } else { 'milestones' }
    $target = $updated.$collection | Where-Object id -CEQ $change.id
    if ($null -eq $target) {
        $target = [pscustomobject]@{id=$change.id}
        $updated.$collection = @($updated.$collection) + $target
    }
    foreach ($key in $change.set.Keys) {
        if ($null -ne $change.set[$key]) { $target | Add-Member -Force -NotePropertyName $key -NotePropertyValue $change.set[$key] }
    }
}
$taskMap=@{}
foreach ($task in $updated.tasks) {
    Assert-Plan (-not $taskMap.ContainsKey($task.id)) "Duplicate task $($task.id)"
    $taskMap[$task.id]=$task
    Assert-Plan ($task.required -eq $true) "Required task weakened: $($task.id)"
    Assert-Plan ($task.deliverables.Count -gt 0) "Missing deliverables: $($task.id)"
    foreach ($path in $task.deliverables) {
        $inScope=$false
        foreach ($scope in @($plan.work_scope)+@($plan.generated_scope)) {
            if ($scope.EndsWith('/**')) { $prefix=$scope.Substring(0,$scope.Length-3); if ($path -eq $prefix -or $path.StartsWith($prefix+'/')) {$inScope=$true} }
            elseif ($scope -ceq $path) { $inScope=$true }
        }
        Assert-Plan $inScope "Deliverable outside permitted scope: $path"
    }
}
$done=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$order=[Collections.Generic.List[string]]::new()
while ($done.Count -lt $taskMap.Count) {
    $available=@($updated.tasks | Where-Object { -not $done.Contains($_.id) -and @($_.depends_on | Where-Object { -not $done.Contains($_) }).Count -eq 0 })
    Assert-Plan ($available.Count -gt 0) 'Dependency cycle or unknown dependency'
    foreach ($task in $available) { $null=$done.Add($task.id); $order.Add($task.id) }
}
$coverage=@()
foreach ($criterion in $plan.completion_criteria) {
    $owners=@($updated.tasks | Where-Object { $_.required -and $criterion.id -cin $_.criterion_ids } | ForEach-Object id)
    Assert-Plan ($owners.Count -gt 0) "Unmapped criterion: $($criterion.id)"
    $coverage += [ordered]@{criterion_id=$criterion.id;required_task_ids=$owners}
}
foreach ($task in $updated.tasks) {
    foreach ($criterionId in $task.criterion_ids) { Assert-Plan ($criterionId -cin $plan.completion_criteria.id) "Unknown criterion: $criterionId" }
    Assert-Plan (@($updated.milestones | Where-Object { $task.id -cin $_.task_ids }).Count -gt 0) "Task missing milestone: $($task.id)"
}
foreach ($milestone in $updated.milestones) {
    Assert-Plan ($milestone.task_ids.Count -gt 0 -and $milestone.acceptance.Length -gt 0) "Incomplete milestone: $($milestone.id)"
    foreach ($id in $milestone.task_ids) { Assert-Plan $taskMap.ContainsKey($id) "Unknown milestone task: $id" }
}
foreach ($original in $logical.tasks) {
    $current=$taskMap[$original.id]
    foreach ($key in @('description','required','acceptance','verification','criterion_ids','source_ids','replacements')) {
        Assert-Plan ((Json $current.$key) -ceq (Json $original.$key)) "Protected task definition changed: $($original.id).$key"
    }
}
foreach ($original in $logical.milestones) {
    $current=$updated.milestones | Where-Object id -CEQ $original.id
    foreach ($key in @('description','acceptance')) { Assert-Plan ($original.$key -ceq $current.$key) "Milestone condition changed: $($original.id).$key" }
}
$profile = Get-Content -Raw (Join-Path $root 'backend/windows-x64/profile.json') | ConvertFrom-Json
$identities=@(
    [ordered]@{path='toolchain/windows_x64/kimi_backend_windows_x64_v1.lib';expected=$profile.artifactSha256},
    [ordered]@{path='toolchain/llvm-dlltool.exe';expected=$profile.kernel32.dlltoolSha256},
    [ordered]@{path='backend/windows-x64/kernel32.def';expected=$profile.kernel32.definitionSha256}
)
foreach ($identity in $identities) { $identity.actual=Sha (Join-Path $root $identity.path); Assert-Plan ($identity.actual -ceq $identity.expected) "Profile identity mismatch: $($identity.path)" }
$missingReferences=@($plan.references | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_.path)) } | ForEach-Object path)
Assert-Plan ($missingReferences.Count -eq 0) 'Missing required references'
$fileChanges=@()
foreach ($path in $trackedPaths) { if ((Sha (Join-Path $root $path)) -cne $before[$path]) { $fileChanges += $path } }
Assert-Plan ($fileChanges.Count -eq 0) 'Protected/product content changed during static check'
$finalStatus=@(git -C $root status --short)
Assert-Plan ((Json $initialStatus) -ceq (Json $finalStatus)) 'Git status changed during static check'
Save-Json 'plan-check.json' ([ordered]@{
    procedure='check-plan.ps1: compare PLAN.md accepted fields; check unchanged requirements, DAG, C01-C16 ownership, milestone coverage, deliverable scope, required references, three pinned hashes, and tracked-file stability.'
    expected='All structural checks pass; no product verification is inferred.'
    actual='passed'
    input_signature=$inputData.signature
    plan_record=$inputData.plan_record
    logical_record=$inputData.logical_path
    records_sha256=@{plan_record=Sha $inputData.plan_record;logical_record=Sha $inputData.logical_path;plan_md=Sha $inputData.plan_path;input=Sha $inputPath}
    original_task_count=$logical.tasks.Count
    updated_task_count=$updated.tasks.Count
    milestones=$updated.milestones | Select-Object id,task_ids
    topological_order=@($order)
    criterion_coverage=$coverage
    preserved_fields=@('existing task IDs','required','description','acceptance','verification','criterion_ids','source_ids','replacements','milestone description/acceptance')
    current_evidence_count=$logical.evidence.Count
    current_findings_count=$logical.findings.Count
    pending_work=$inputData.pending_work
    uncertain=$inputData.uncertain
    milestone_progress=$inputData.milestone_progress
    missing_references=$missingReferences
    pinned_identities=$identities
    git_status_before=$initialStatus
    git_status_after=$finalStatus
    tracked_files_checked=$trackedPaths.Count
    tracked_file_hashes=$before
    tracked_file_changes=$fileChanges
    proposed_changes_sha256=Sha (Join-Path $out 'proposed-changes.json')
    check_script_sha256=Sha (Join-Path $out 'check-plan.ps1')
    unperformed=@('Full normative clause inventory T01','Build','Managed tests','LLVM/native O0/O2 tests','Benchmarks','NativeAOT (excluded)')
    next='T01 clause inventory; T36 baseline gate; T02 and existing dependent tasks. Only the runner selects later workers.'
})
Write-Output "Static plan checks passed: $($updated.tasks.Count) tasks, $($coverage.Count) criteria, $($updated.milestones.Count) milestones, $($trackedPaths.Count) unchanged tracked files."
