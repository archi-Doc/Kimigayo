# Approval regressions use real Git trailers/blobs and canonical Report rows; builds are covered by the final commit audit.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/verification-inputs.ps1')
. (Join-Path $PSScriptRoot '../../scripts/code-rules-report.ps1')
. (Join-Path $PSScriptRoot '../../scripts/code-rules-approval.ps1')
$cases = 0
function Equal($Actual, $Expected) { if ($Actual -cne $Expected) { throw "Expected '$Expected', got '$Actual'." }; $script:cases++ }
function Reject([scriptblock] $Action, [string] $Message) {
    try { $null = & $Action }
    catch { if ($_.Exception.Message -notlike "*$Message*") { throw }; $script:cases++; return }
    throw "Expected rejection containing '$Message'."
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work = Join-Path $repo ('temp/code-rules-approval-test/' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($work)
function Put([string] $Path, [string] $Text) {
    $full = Join-Path $work $Path
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))
    [IO.File]::WriteAllText($full, $Text)
}
function Commit([string] $Message) {
    $body = $work + '-message.txt'
    [IO.File]::WriteAllText($body, $Message)
    $null = Invoke-KimiVerificationGit $work @('add', '--all')
    $null = Invoke-KimiVerificationGit $work @('commit', '--quiet', '--allow-empty', '--file', $body)
    return (Invoke-KimiVerificationGit $work @('rev-parse', 'HEAD')).Trim()
}
function Observation([string] $Kind, [int] $Bound, [string] $Scope = 'T:C', [string] $Path = 'src/Kimi/C.cs') {
    $p = @{ rule = 'KR2006'; scope = $Scope; anchor = "member:M:$($Scope.Substring(2)).M"; value = '0'; path = $Path; baselineAllowed = 'false'; kind = $Kind; reason = 'owned operation' }
    if ($Kind -ceq 'exception') { $p.limit = "$Bound" }
    else { $p.rule = 'KR1007'; $p.stage = @{ 0 = 'R0'; 6 = 'R5'; 7 = 'R6'; 8 = 'R7' }[$Bound]; $p.value = "$Bound" }
    return ,(Get-KimiCodeRulesObservations @(@{ ruleId = 'KR9001'; properties = @{ customProperties = $p } }))
}
$empty = Get-KimiCodeRulesObservations @()
$exception = Observation exception 70
Assert-KimiCodeRulesAnnotationApproval $exception (Observation exception 69); $cases++
Assert-KimiCodeRulesAnnotationApproval $exception $empty; $cases++
Assert-KimiCodeRulesAnnotationApproval $exception (Observation exception 70 'T:C' 'src/Kimi/OtherPartial.cs'); $cases++
Reject { Assert-KimiCodeRulesAnnotationApproval $empty $exception } 'annotation added or relaxed'
Reject { Assert-KimiCodeRulesAnnotationApproval $exception (Observation exception 71) } 'annotation added or relaxed'
Reject { Assert-KimiCodeRulesAnnotationApproval $exception (Observation exception 70 'T:D') } 'annotation added or relaxed'
$changedReason = Observation exception 70
@($changedReason.Values)[0].reason = 'different rationale'
Assert-KimiCodeRulesAnnotationApproval $exception $changedReason; $cases++
$transition = Observation transitional 7
Assert-KimiCodeRulesAnnotationApproval $empty $transition; $cases++
Assert-KimiCodeRulesAnnotationApproval $transition (Observation transitional 6); $cases++
Assert-KimiCodeRulesAnnotationApproval $transition (Observation transitional 7 'T:C' 'src/Kimi/Partial.cs'); $cases++
Reject { Assert-KimiCodeRulesAnnotationApproval $transition (Observation transitional 8) } 'annotation added or relaxed'
Assert-KimiCodeRulesAnnotationApproval (Observation transitional 0) (Observation transitional 0); $cases++

$null = Invoke-KimiVerificationGit $work @('init', '--quiet')
$null = Invoke-KimiVerificationGit $work @('config', 'user.name', 'Code rules regression')
$null = Invoke-KimiVerificationGit $work @('config', 'user.email', 'code-rules@example.invalid')
$null = Invoke-KimiVerificationGit $work @('config', 'commit.gpgsign', 'false')
Put '.gitignore' "artifacts/`n"
Put 'src/Kimi/CodeRules.globalconfig' "is_global = true`n"
$baselineText = "KR2006`tT:C`tmember:M:C.M`t61`tsrc/Kimi/C.cs`n"
Put 'src/Kimi/CodeRules.baseline.tsv' $baselineText
$first = Commit 'Initial inputs'
Equal (Get-KimiCodeRulesApprovalTrailer $work $first).Count 0
$valid = Commit "Approved`n`nCode-Rules-Approved: owner 2026-10-12: adopted rule`n"
Equal (Get-KimiCodeRulesApprovalTrailer $work $valid).Count 1
foreach ($trailer in @('owner 2026-02-30: invalid date', 'owner 2026-10-12 missing colon', 'owner 2026-10-12: ', '')) {
    $bad = Commit ("Malformed`n`nCode-Rules-Approved: " + $trailer + "`n")
    Reject { Get-KimiCodeRulesApprovalTrailer $work $bad } 'Malformed'
}
$bodyOnly = Commit "Message`n`nCode-Rules-Approved: owner 2026-10-12: looks like approval`n`nThis is ordinary body text.`n"
Equal (Get-KimiCodeRulesApprovalTrailer $work $bodyOnly).Count 0

$beforeBaseline = Get-KimiCodeRulesBaseline (Join-Path $work 'src/Kimi/CodeRules.baseline.tsv')
Put 'src/Kimi/CodeRules.baseline.tsv' ($baselineText.Replace("`t61`t", "`t62`t"))
$grown = Get-KimiCodeRulesBaseline (Join-Path $work 'src/Kimi/CodeRules.baseline.tsv')
Reject { Assert-KimiCodeRulesBaselineApproval $beforeBaseline $grown } 'baseline added or increased'
Reject { Assert-KimiCodeRulesBaselineApproval $empty $beforeBaseline } 'baseline added or increased'
Assert-KimiCodeRulesBaselineApproval $grown $beforeBaseline; $cases++
Assert-KimiCodeRulesBaselineApproval $beforeBaseline $empty; $cases++
Put 'src/Kimi/CodeRules.baseline.tsv' ($baselineText.Replace('src/Kimi/C.cs', 'src/Kimi/Partial.cs'))
Assert-KimiCodeRulesBaselineApproval $beforeBaseline (Get-KimiCodeRulesBaseline (Join-Path $work 'src/Kimi/CodeRules.baseline.tsv')); $cases++
Put 'src/Kimi/CodeRules.baseline.tsv' $baselineText
$base = Commit 'Before comparison'
$head = Commit 'No relaxation'
$calls = [Collections.Generic.List[string]]::new()
$beforeRows = $exception
$afterRows = Observation exception 69
$reportFailure = $false
# Replace the expensive production boundary, leaving policy, Git parsing and canonical SARIF row validation real.
function Get-KimiCodeRulesCommitReport([string] $Root, [string] $Commit, [string] $Evidence) {
    if ($Root -cne $work) { throw 'Unexpected snapshot repository.' }
    $calls.Add($Commit)
    if ($reportFailure) { throw 'Incomplete report fixture' }
    if ($Commit -ceq $base) { return ,$beforeRows }
    if ($Commit -cne $head) { throw 'Wrong compared commit.' }
    return ,$afterRows
}
$audit = Join-Path $work 'artifacts'
Equal (Assert-KimiCodeRulesCommitApproval $work $head $audit).result 'not-required'
Equal ($calls -join ',') "$base,$head"
$calls.Clear()
$afterRows = Observation exception 71
Reject { Assert-KimiCodeRulesCommitApproval $work $head $audit } 'annotation added or relaxed'
$reportFailure = $true
Reject { Assert-KimiCodeRulesCommitApproval $work $head $audit } 'Incomplete report fixture'
$reportFailure = $false
$calls.Clear()
Put 'src/Kimi/CodeRules.globalconfig' "is_global = true`n# even a comment requires approval`n"
$configChange = Commit 'Changed configuration'
Reject { Assert-KimiCodeRulesCommitApproval $work $configChange $audit } 'CodeRules.globalconfig changed'
Equal $calls.Count 0
Put 'src/Kimi/CodeRules.baseline.tsv' ($baselineText.Replace("`t61`t", "`t62`t"))
$baselineChange = Commit 'Increased baseline'
Reject { Assert-KimiCodeRulesCommitApproval $work $baselineChange $audit } 'baseline added or increased'
Equal $calls.Count 0
$approved = Commit "Authorized`n`nCode-Rules-Approved: owner 2026-10-12: accepted increase`n"
Equal (Assert-KimiCodeRulesCommitApproval $work $approved $audit).result 'approved'
Equal $calls.Count 0
Write-Output "Passed $cases code-rules approval checks. Isolated repository retained: $work"
