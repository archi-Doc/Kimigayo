# Actual csc suppression/error-log regression. Run after building Kimi.Analyzers in Release.
[CmdletBinding()]
param([string] $Evidence = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/code-rules-report.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$analyzer = Join-Path $repo 'src/Kimi.Analyzers/bin/Release/netstandard2.0/Kimi.Analyzers.dll'
if (-not (Test-Path -LiteralPath $analyzer)) { throw 'Build Kimi.Analyzers in Release first.' }
$id = [Guid]::NewGuid().ToString('N')
$root = Join-Path $repo "temp/code-rules-csc-$id"
if (-not $Evidence) { $Evidence = Join-Path $repo "artifacts/verify/code-rules-csc-$id" }
$Evidence = [IO.Path]::GetFullPath($Evidence)
$null = New-Item -ItemType Directory -Path (Join-Path $root 'src/Kimi'), $Evidence
$project = Join-Path $root 'Fixture.csproj'
$source = Join-Path $root 'src/Kimi/Fixture.cs'
$configPath = Join-Path $root 'src/Kimi/CodeRules.globalconfig'
$baseline = Join-Path $root 'src/Kimi/CodeRules.baseline.tsv'
$config = [IO.File]::ReadAllText((Join-Path $repo 'src/Kimi/CodeRules.globalconfig'))
# This isolated suppression fixture does not declare the compiler's architecture types.
$config = [regex]::Replace($config, '(?m)^kimi_code_rules\.(?:layer\.|deny_|allow_|vocabularies)[^\r\n]*(?:\r?\n|$)', '')
[IO.File]::WriteAllText($baseline, "# baseline`n")
# The isolated fixture root is the analyzer's repository; the analyzer does not propagate to its consumers.
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KimiRepositoryRoot>$root</KimiRepositoryRoot>
    <KimiCodeRulesMode>Report</KimiCodeRulesMode>
    <ErrorLog>`$(KimiCodeRulesLog),version=2.1</ErrorLog>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <Analyzer Include="$analyzer" />
    <AdditionalFiles Include="src/Kimi/CodeRules.globalconfig;src/Kimi/CodeRules.baseline.tsv" />
    <GlobalAnalyzerConfigFiles Include="src/Kimi/CodeRules.globalconfig" />
    <CompilerVisibleProperty Include="KimiRepositoryRoot" />
    <CompilerVisibleProperty Include="KimiCodeRulesMode" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project
$attribute = '[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeRules", "KR2001")] class C { }'
$cases = @(
    @{ name = 'blanket-pragma'; source = "#pragma warning disable`nclass C { }"; suppressed = $false },
    @{ name = 'id-pragma'; source = "#pragma warning disable KR9004`nclass C { }"; suppressed = $false },
    @{ name = 'nowarn'; source = $attribute; extra = @('-p:NoWarn=KR9004'); suppressed = $false },
    @{ name = 'editorconfig'; source = $attribute; editor = "root = true`n[*.cs]`ndotnet_diagnostic.KR9004.severity = none"; suppressed = $false },
    @{ name = 'globalconfig'; source = $attribute; config = "`ndotnet_diagnostic.KR9004.severity = none"; suppressed = $false },
    @{ name = 'member-alias'; source = 'using S = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute; class C { [S("CodeRules", "KR9004")] public void M() { } }'; suppressed = $true },
    @{ name = 'type'; source = '[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeRules", "KR9004")] class C { }'; suppressed = $true },
    @{ name = 'assembly'; source = '[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("CodeRules", "KR9004", Scope = "type", Target = "~T:C")] [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeRules", "KR2001")] class C { }'; suppressed = $true },
    @{ name = 'unconditional'; source = '[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("CodeRules", "KR9004")] class C { }'; suppressed = $true },
    @{ name = 'builtin'; source = 'class C { [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0052")] private int value; public C(int input) { this.value = input; } }'; config = "`ndotnet_diagnostic.IDE0052.severity = error"; suppressed = $true; diagnostic = 'IDE0052' }
)
$results = [Collections.Generic.List[object]]::new()
foreach ($case in $cases) {
    [IO.File]::WriteAllText($source, $case.source)
    [IO.File]::WriteAllText($configPath, $config + $case.config)
    [IO.File]::WriteAllText((Join-Path $root '.editorconfig'), [string]$case.editor)
    $sarif = Join-Path $Evidence ($case.name + '.sarif')
    $log = Join-Path $Evidence ($case.name + '.log')
    $extra = @($case.extra)
    & dotnet build $project --no-incremental -c Release --disable-build-servers -m:1 "-p:KimiCodeRulesLog=$sarif" @extra *> $log
    $exitCode = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $sarif)) { throw "csc produced no error log: $log" }
    $records = @((Get-Content -LiteralPath $sarif -Raw | ConvertFrom-Json).runs[0].results)
    $id = if ($case.diagnostic) { $case.diagnostic } else { 'KR9004' }
    $matching = @($records | Where-Object ruleId -CEQ $id)
    if ($matching.Count -eq 0) { throw "$id disappeared in $($case.name); see $sarif" }
    if ($case.suppressed) {
        if (-not @($matching | Where-Object { @($_.suppressions).Where({ $null -ne $_ }).Count -gt 0 }).Count) { throw "Missing csc suppression metadata: $sarif" }
        $rejected = $false
        try { $null = Read-KimiCodeRulesSarif $sarif $root }
        catch { if ($_.Exception.Message -notlike 'Suppressed protected*') { throw }; $rejected = $true }
        if (-not $rejected) { throw "Verification accepted suppressed diagnostics: $sarif" }
    }
    elseif ($exitCode -eq 0) { throw "Suppression bypassed a required error: $log" }
    $results.Add(@{ case = $case.name; exitCode = $exitCode; diagnostic = $id; records = $matching.Count; suppressed = $case.suppressed })
}
# A Report must survive stale-entry warnings so Prune can remove them. Verify still rejects the same source.
foreach ($case in @(
    @{ name = 'stale-report'; mode = 'Report'; source = 'class C { }'; success = $true; level = 'warning' },
    @{ name = 'other-warning'; mode = 'Report'; source = "#warning unrelated`nclass C { }"; success = $false; level = 'warning' },
    @{ name = 'stale-verify'; mode = 'Verify'; source = 'class C { }'; success = $false; level = 'error' }
)) {
    [IO.File]::WriteAllText($source, $case.source)
    [IO.File]::WriteAllText($configPath, $config)
    [IO.File]::WriteAllText((Join-Path $root '.editorconfig'), '')
    [IO.File]::WriteAllText($baseline, "KR2001`tT:C`tmember:M:C.M`t7`tsrc/Kimi/Fixture.cs`n")
    $sarif = Join-Path $Evidence ($case.name + '.sarif')
    $log = Join-Path $Evidence ($case.name + '.log')
    & dotnet build $project --no-restore --no-incremental -c Release --disable-build-servers -m:1 -warnaserror -warnnotaserror:KR9002 "-p:KimiCodeRulesMode=$($case.mode)" "-p:KimiCodeRulesLog=$sarif" *> $log
    $exitCode = $LASTEXITCODE
    if (($exitCode -eq 0) -ne $case.success) { throw "Unexpected stale-entry build outcome: $log" }
    $records = Read-KimiCodeRulesSarif $sarif $root -RequireComplete:($case.mode -ceq 'Report')
    $stale = @($records | Where-Object ruleId -CEQ 'KR9002')
    if ($stale.Count -ne 1 -or $stale[0].level -cne $case.level) { throw "Stale-entry severity changed: $sarif" }
    if ($case.success) {
        $pruned = Update-KimiCodeRulesBaseline (Get-KimiCodeRulesBaseline $baseline) (Get-KimiCodeRulesObservations $records) Prune @() ''
        Write-KimiCodeRulesBaseline $baseline $pruned
        if ((Get-KimiCodeRulesBaseline $baseline).Count -ne 0) { throw 'Complete Report did not remove the stale baseline.' }
    }
    $results.Add(@{ case = $case.name; exitCode = $exitCode; diagnostic = 'KR9002'; records = 1; level = $case.level })
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Evidence 'results.json')
Write-Output "Passed $($results.Count) csc suppression and stale-report checks; evidence $Evidence"
