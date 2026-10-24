# Native verification evidence: read the runner's success line or the coverage deferred to the pending gate.
# Inputs are the native log and, for pending verification, its result path and selected fixture count.
# Outputs are the existing completion predicate and detail text; this owner neither runs tools nor judges pending failures.
function Get-KimiNativeLogResult([string] $Log) {
    $line = Select-String -LiteralPath $Log -Pattern 'Passed [1-9]\d* native' | Select-Object -Last 1
    [pscustomobject]@{
        hasSuccessLine = $null -ne $line
        detail = "$(if ($line) { $line.Line.Trim() } else { 'see log' }); $Log"
    }
}

function Get-KimiNativePendingResult([string] $ResultsPath, [int] $SelectedCount, [string] $Log) {
    $results = @(if (Test-Path -LiteralPath $ResultsPath) { Get-Content -LiteralPath $ResultsPath -Raw | ConvertFrom-Json })
    $count = $results.Count
    $incomplete = @($results | Where-Object { $_.status -cnotin @('passed', 'failed') }).Count
    [pscustomobject]@{
        passed = $count -gt 0 -and $count -eq $SelectedCount -and $incomplete -eq 0
        detail = "results for $count of $SelectedCount fixtures, $incomplete incomplete; completed results judged by codegen pending; $Log"
    }
}
