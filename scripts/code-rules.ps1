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
$result = Invoke-KimiCodeRulesReport $repo $evidence
$before = $result.inputs
$observed = $result.observations
if ($mode -cne 'Report') {
    $baselinePath = Join-Path $repo 'src/Kimi/CodeRules.baseline.tsv'
    $baseline = Get-KimiCodeRulesBaseline $baselinePath
    $updated = Update-KimiCodeRulesBaseline $baseline $observed $mode $Rule $Path
    # No write is permitted after source/configuration changes, even during report processing.
    Assert-KimiVerificationInputsStable $before (Get-KimiVerificationInputs $repo)
    Write-KimiCodeRulesBaseline $baselinePath $updated
}
Write-Output "$mode complete: $($observed.Count) observed keys; evidence $evidence"
