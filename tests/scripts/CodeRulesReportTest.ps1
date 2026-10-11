# Shared SARIF/baseline workflow checks; no compiler build is required.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/code-rules-report.ps1')
$cases = 0
function Reject([scriptblock] $Action, [string] $Message) {
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        $script:cases++
        return
    }
    throw "Expected rejection containing '$Message'."
}
function Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "Expected '$Expected', got '$Actual'." }
    $script:cases++
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('kimi-code-rules-report-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $root 'src/Kimi')
$log = Join-Path $root 'report.sarif'
function Save($Results) {
    @{ version = '2.1.0'; runs = @(@{ results = @($Results) }) } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $log
}
try {
    $config = Join-Path $root 'src/Kimi/CodeRules.globalconfig'
    $baseline = Join-Path $root 'src/Kimi/CodeRules.baseline.tsv'
    [IO.File]::WriteAllText($config, "is_global = true`r`n", [Text.UTF8Encoding]::new($true))
    [IO.File]::WriteAllText($baseline, "# baseline`n", [Text.UTF8Encoding]::new($false))
    $properties = @{
        treeCount = '1'
        configAlgorithm = 'Sha256'; configChecksum = (Get-FileHash $config).Hash
        baselineAlgorithm = 'Sha256'; baselineChecksum = (Get-FileHash $baseline).Hash
    }
    $marker = @{ ruleId = 'KR9000'; properties = @{ customProperties = $properties } }
    Save @($marker)
    Equal @(Read-KimiCodeRulesSarif $log $root -RequireComplete).Count 1
    Save @()
    Reject { Read-KimiCodeRulesSarif $log $root -RequireComplete } 'exactly one'
    Save @($marker, $marker)
    Reject { Read-KimiCodeRulesSarif $log $root -RequireComplete } 'exactly one'
    Save @($marker, @{ ruleId = 'AD0001' })
    Reject { Read-KimiCodeRulesSarif $log $root -RequireComplete } 'Analyzer exception'
    foreach ($id in @('KR2001', 'KR9004', 'IDE0051', 'IDE0052', 'IDE0060')) {
        Save @(@{ ruleId = $id; suppressions = @(@{ kind = 'inSource' }) })
        Reject { Read-KimiCodeRulesSarif $log $root } 'Suppressed protected'
    }
    Save @(@{ ruleId = 'CA1000'; suppressions = @(@{ kind = 'inSource' }) })
    Equal @(Read-KimiCodeRulesSarif $log $root).Count 1
    $properties.treeCount = '0'
    Save @($marker)
    Reject { Read-KimiCodeRulesSarif $log $root -RequireComplete } 'no source trees'
    $properties.treeCount = '1'
    Save @($marker)
    [IO.File]::AppendAllText($config, "# changed`n")
    Reject { Read-KimiCodeRulesSarif $log $root -RequireComplete } 'checksum mismatch'
    $p = @{ rule = 'KR2006'; scope = 'T:C'; anchor = 'member:M:C.M'; value = '61'; path = 'src/Kimi/C.cs'; baselineAllowed = 'true' }
    $observation = @{ ruleId = 'KR9001'; properties = @{ customProperties = $p } }
    $observed = Get-KimiCodeRulesObservations @($observation, $observation)
    Equal $observed.Count 1
    Equal @($observed.Values)[0].value 61
    $inventory = $observed.Values | Select-Object rule,scope,anchor,value,path,allowed | ConvertTo-Json | ConvertFrom-Json
    Equal $inventory.rule 'KR2006'
    Equal $inventory.scope 'T:C'
    Equal $inventory.anchor 'member:M:C.M'
    Equal $inventory.value 61
    Equal ($inventory.value -is [long] -or $inventory.value -is [int]) $true
    Equal $inventory.path 'src/Kimi/C.cs'
    Equal $inventory.allowed $true
    $empty = Get-KimiCodeRulesBaseline $baseline
    $initial = Update-KimiCodeRulesBaseline $empty $observed Initialize @('KR2006') ''
    Write-KimiCodeRulesBaseline $baseline $initial
    $initial = Get-KimiCodeRulesBaseline $baseline
    Equal $initial.Count 1
    $p.value = '62'
    $grown = Get-KimiCodeRulesObservations @($observation)
    $pruned = Update-KimiCodeRulesBaseline $initial $grown Prune @() ''
    Equal @($pruned.Values)[0].value 61
    Reject { Update-KimiCodeRulesBaseline $initial $grown Initialize @('KR2006') '' } 'cannot replace'
    $accepted = Update-KimiCodeRulesBaseline $initial $grown Accept @('KR2006') 'src/Kimi/C.cs'
    Equal @($accepted.Values)[0].value 62
    $p.baselineAllowed = 'false'
    $forbidden = Get-KimiCodeRulesObservations @($observation)
    Equal ($forbidden.Values | Select-Object allowed | ConvertTo-Json | ConvertFrom-Json).allowed $false
    Reject { Update-KimiCodeRulesBaseline $initial $forbidden Accept @('KR2006') 'src/Kimi/C.cs' } 'forbidden'
    $none = Get-KimiCodeRulesObservations @()
    $deleted = Update-KimiCodeRulesBaseline $initial $none Prune @() ''
    Equal $deleted.Count 0
    # A malformed or inconsistent observation cannot be used to shrink a baseline.
    $bad = @{ ruleId = 'KR9001'; properties = @{ customProperties = @{ rule = 'KR2006' } } }
    Reject { Get-KimiCodeRulesObservations @($bad) } 'Malformed'
    $copy = $p.Clone()
    $copy.value = '63'
    Reject { Get-KimiCodeRulesObservations @($observation, @{ ruleId = 'KR9001'; properties = @{ customProperties = $copy } }) } 'Inconsistent'
    Write-Output "Passed $cases code-rules report checks."
}
finally {
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($root).StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($root) -notmatch '^kimi-code-rules-report-[0-9a-f]{32}$') { throw 'Unsafe temporary cleanup target.' }
    Remove-Item -LiteralPath $root -Recurse -Force
}
