$ErrorActionPreference='Stop'
$inputData=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../input.json') | ConvertFrom-Json
$changes=@(Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'proposed-changes.json') | ConvertFrom-Json)
$check=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'plan-check.json') | ConvertFrom-Json
$prefix=$inputData.attempt_id
$checkId=$prefix+'_plan'
$reviewId=$prefix+'_review'
$taskIds=@($changes | Where-Object kind -CEQ 'task' | ForEach-Object id)
$taskResults=@(foreach($id in $taskIds) {
    $remaining=switch($id) {
        'T01' {'全規範条項の棚卸しと現物・検証への対応表は未実施。C01の達成証拠なし。'}
        'T36' {'実装変更前の基準測定とallocation検証は未実施。測定証拠なし。'}
        default {'受入・検証条件の実施が残る。現行の受理済み製品証拠なし。'}
    }
    [ordered]@{id=$id;status='pending';evidence_ids=@($checkId);remaining=@($remaining);blocker=$null}
})
$evidence=@(
    [ordered]@{id=$checkId;kind='plan';path='plan-check.json';hash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'plan-check.json') -Algorithm SHA256).Hash.ToLowerInvariant();target_ids=@($taskIds)+@($check.criterion_coverage.criterion_id)+@($check.milestones.id);input_signature=$inputData.signature},
    [ordered]@{id=$reviewId;kind='plan';path='plan-review.md';hash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'plan-review.md') -Algorithm SHA256).Hash.ToLowerInvariant();target_ids=@('T01','T02','T20','T31','T34','T36','C15');input_signature=$inputData.signature}
)
$result=[ordered]@{
    schema_version=1
    run_id=$inputData.run_id
    attempt_id=$inputData.attempt_id
    phase=$inputData.phase
    input_plan_hash=$inputData.input_plan_hash
    base_plan_version=$inputData.base_plan_version
    execution_plan_hash=$inputData.execution_plan_hash
    execution_plan=$null
    decision='ready'
    route_hint=$null
    summary="成果物を全35既存項目へ補完し、変更前の性能基準測定T36を追加。36必須項目・16完成条件・11マイルストーンの対応と非循環依存を静的確認。既存条件は維持。製品変更・build/test/benchmarkは未実施、全項目pending。"
    task_results=$taskResults
    changes=$changes
    findings=@()
    evidence=$evidence
    progress=@(
        [ordered]@{task_id='T01';description='現行計画とPLAN.mdの一致、C01–C16の必須task対応、全milestone割当、成果物範囲を機械照合した。規範条項の全件棚卸しは残る。';evidence_ids=@($checkId)},
        [ordered]@{task_id='T34';description='比較用の変更前測定をT36としてT02の前提に配置し、既存T34の比較要件を実行可能な順序にした。測定は未実施。';evidence_ids=@($reviewId,$checkId)}
    )
}
$resultPath=Join-Path $PSScriptRoot 'result.json'
$result | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
$valid=Test-Json -Json (Get-Content -Raw -LiteralPath $resultPath) -SchemaFile $inputData.result_schema
if (-not $valid) { throw 'Result schema validation failed' }
foreach($item in $evidence) {
    $actual=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $item.path) -Algorithm SHA256).Hash.ToLowerInvariant()
    if($actual -cne $item.hash) { throw "Evidence hash mismatch: $($item.id)" }
}
@{result_schema='passed';evidence_hashes='passed';task_results=$taskResults.Count;task_changes=$taskIds.Count;milestone_changes=1;decision=$result.decision;saved_at_utc=[DateTime]::UtcNow.ToString('o');save_from_utc=$inputData.save_from_utc;deadline_utc=$inputData.deadline_utc} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result-validation.json') -Encoding utf8NoBOM
$evidence | Select-Object id,path,hash | ConvertTo-Json
Write-Output 'result.schema.json validation passed; evidence hashes matched; 36 pending task results saved.'
