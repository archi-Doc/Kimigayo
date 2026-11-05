# Native verification evidence: read the runner's success line.
# Input is the native log. Output is the existing completion predicate and detail text; this owner runs no tools.
function Get-KimiNativeLogResult([string] $Log) {
    $line = Select-String -LiteralPath $Log -Pattern 'Passed [1-9]\d* native' | Select-Object -Last 1
    [pscustomobject]@{
        hasSuccessLine = $null -ne $line
        detail = "$(if ($line) { $line.Line.Trim() } else { 'see log' }); $Log"
    }
}
