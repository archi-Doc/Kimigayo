# Obtain a fresh, complete code-rules report before changing any baseline entry.
[CmdletBinding(DefaultParameterSetName = 'Report')]
param(
    [Parameter(ParameterSetName = 'Report')] [switch] $Report,
    [Parameter(Mandatory, ParameterSetName = 'Prune')] [switch] $Prune,
    [Parameter(Mandatory, ParameterSetName = 'Accept')] [switch] $Accept,
    [Parameter(Mandatory, ParameterSetName = 'Initialize')] [switch] $Initialize,
    [Parameter(Mandatory, ParameterSetName = 'Accept')]
    [Parameter(Mandatory, ParameterSetName = 'Initialize')] [string[]] $Rule = @(),
    [Parameter(Mandatory, ParameterSetName = 'Accept')] [string] $Path
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification-inputs.ps1')
. (Join-Path $PSScriptRoot 'code-rules-report.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$mode = $PSCmdlet.ParameterSetName
if ($Rule.Where({ $_ -cnotmatch '^KR[123][0-9]{3}$' }).Count) { throw 'Rule IDs must identify KR1-KR3 rules.' }
if ($mode -eq 'Accept' -and ($Path -cnotmatch '^src/Kimi/' -or $Path.Contains('\') -or $Path.Split('/') -ccontains '..')) { throw 'Path must be normalized and repository-relative.' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$evidence = Join-Path $repo "artifacts/verify/$stamp-code-rules-$($mode.ToLowerInvariant())"
$null = New-Item -ItemType Directory -Path $evidence
$sarif = Join-Path $evidence 'code-rules.sarif'
$before = Get-KimiVerificationInputs $repo
$before | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'inputs-before.json')
# Keep Report's stale-entry warning available to Prune; MSBuild otherwise promotes it after csc has emitted SARIF.
& dotnet build (Join-Path $repo 'src/Kimi/Kimi.csproj') --no-restore --no-incremental -c Release --disable-build-servers -m:1 -warnaserror -warnnotaserror:KR9002 -p:EmitCompilerGeneratedFiles=false -p:KimiCodeRulesMode=Report "-p:KimiCodeRulesLog=$sarif" *> (Join-Path $evidence 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Report build failed; see $evidence" }
$after = Get-KimiVerificationInputs $repo
$after | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'inputs-after.json')
Assert-KimiVerificationInputsStable $before $after
$results = Read-KimiCodeRulesSarif $sarif $repo -RequireComplete
$observed = Get-KimiCodeRulesObservations $results
$observed.Values | Select-Object rule, scope, anchor, value, path, allowed, kind, reason, limit, stage | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'observations.json')
$results | Where-Object ruleId -CEQ 'KR9001' | Group-Object { $_.properties.customProperties.rule + ' ' + ($_.properties.customProperties.path -replace '^src/Kimi/([^/]+/[^/]+).*', '$1') } |
    Select-Object Name, Count | Format-Table | Out-String | Set-Content -LiteralPath (Join-Path $evidence 'summary.txt')
if ($mode -cne 'Report') {
    $baselinePath = Join-Path $repo 'src/Kimi/CodeRules.baseline.tsv'
    $baseline = Get-KimiCodeRulesBaseline $baselinePath
    $updated = Update-KimiCodeRulesBaseline $baseline $observed $mode $Rule $Path
    # No write is permitted after source/configuration changes, even during report processing.
    Assert-KimiVerificationInputsStable $before (Get-KimiVerificationInputs $repo)
    Write-KimiCodeRulesBaseline $baselinePath $updated
}
Write-Output "$mode complete: $($observed.Count) observed keys; evidence $evidence"
