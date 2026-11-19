# Runs TataraExecutorTest against a private toolchain root (Tatara design A2 §4.5; temporary until unit A4 builds tatara.dll for every
# lane): the root is a copy of the shared toolchain plus the given tatara.dll, set for this process only (DECISIONS T-M3); a DLL whose
# build identity is not this checkout's (identity.ps1) is refused. Without -Fixtures the class runs its fixed rows; with it, the corpus
# is those fixture .ll files (tool control comparison at O0 and O2) and a pair per milestone and level (executor comparison). The fatal
# row runs in a second test process. Writes <Output>/report.md with the hashes, the test results and the corpus counts.
param(
    [Parameter(Mandatory)] [string] $Dll,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Fixtures,
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$guard = 'C:\Users\bwff1\repos\archi-Doc\Kimigayo\artifacts\board\tools\quiet_guard.py'
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null

# The DLL's identity, read in a child process so this process maps no tatara.dll.
. (Join-Path $PSScriptRoot 'identity.ps1')
$expected = Get-TataraBuildIdentity
$read = @'
param($path)
Add-Type -TypeDefinition 'public delegate void TataraIdentity(System.IntPtr identity);'
$library = [Runtime.InteropServices.NativeLibrary]::Load($path)
$function = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer([Runtime.InteropServices.NativeLibrary]::GetExport($library, 'tatara_identity'), [TataraIdentity])
$buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal(16)
$function.Invoke($buffer)
[Runtime.InteropServices.Marshal]::PtrToStringUTF8([Runtime.InteropServices.Marshal]::ReadIntPtr($buffer, 0))
'@
$build = (& pwsh -NoProfile -Command "& { $read } '$Dll'" | Select-Object -Last 1)
if ($build -cne $expected) { throw "Stale tatara.dll: $Dll has build $build; this checkout's is $expected. Rebuild it with src/Tatara/build.ps1." }

# The private root: the shared toolchain's files (copied once, then refreshed by hash) and the DLL.
$shared = Join-Path $repository 'toolchain'
$root = Join-Path $Output 'toolchain'
foreach ($file in Get-ChildItem -LiteralPath $shared -Recurse -File) {
    $copy = Join-Path $root ([IO.Path]::GetRelativePath($shared, $file.FullName))
    if (-not (Test-Path -LiteralPath $copy) -or (Get-Item -LiteralPath $copy).Length -ne $file.Length) {
        New-Item -ItemType Directory -Force -Path (Split-Path $copy) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $copy -Force
    }
}
Copy-Item -LiteralPath $Dll -Destination (Join-Path $root 'tatara.dll') -Force
$hashes = @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Name -in 'tatara.dll', 'opt.exe', 'llc.exe', 'lld-link.exe', 'clang.exe', 'llvm-lib.exe' } |
    ForEach-Object { "| $($_.Name) | $((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()) |" })

$tests = Join-Path $repository "tests\xUnitTest\bin\$Configuration\net10.0\xUnitTest.dll"
$env:KIMI_TOOLCHAIN_ROOT = $root
$env:KIMI_TATARA_CORPUS = $null
$corpusLines = @()
if ($Fixtures) {
    # The corpus: the fixtures (read in place through a junction) and one generated pair per milestone and level.
    $corpus = Join-Path $Output 'corpus'
    Remove-Item -LiteralPath $corpus -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path (Join-Path $corpus 'pairs') | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $corpus 'fixtures') -Target ([IO.Path]::GetFullPath($Fixtures)) | Out-Null
    $kimi = Join-Path $repository "tests\xUnitTest\bin\$Configuration\net10.0\Kimi.dll"
    $skipped = @()
    foreach ($source in Get-ChildItem -LiteralPath (Join-Path $repository 'tests\milestones') -Filter '*.kimi') {
        foreach ($level in 'O0', 'O2') {
            $pair = Join-Path $corpus "pairs\$($source.BaseName)-$level"
            New-Item -ItemType Directory -Path $pair | Out-Null
            Copy-Item -LiteralPath $source.FullName -Destination $pair
            "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$level`"" | Set-Content -LiteralPath (Join-Path $pair "$($source.BaseName).kimiproj") -Encoding utf8
            $emitted = & dotnet $kimi emit (Join-Path $pair "$($source.BaseName).kimiproj") 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { $skipped += "$($source.BaseName)-$level"; Remove-Item -LiteralPath $pair -Recurse -Force; $emitted | Set-Content (Join-Path $Output "skipped-$($source.BaseName)-$level.txt") }
        }
    }
    $env:KIMI_TATARA_CORPUS = $corpus
    $corpusLines += "Fixtures: $Fixtures ($((Get-ChildItem -LiteralPath $Fixtures -Filter '*.ll').Count) .ll files)", "Milestone pairs: $((Get-ChildItem (Join-Path $corpus 'pairs') -Directory).Count); not generated: $($skipped -join ', ')"
}

# The class, then the fatal row in its own process.
$results = @()
foreach ($class in 'XunitTest.TataraExecutorTest', 'XunitTest.TataraExecutorTest+Fatal') {
    $xml = Join-Path $Output "$class.xml"
    $log = & python -I -X utf8 $guard run t-a2 -- dotnet $tests -class $class -explicit only -xml $xml 2>&1 | Out-String
    $results += "## $class (exit $LASTEXITCODE)", '', '```', $log.Trim(), '```', ''
}

$report = @('# Tatara corpus run', '', "Checkout: $(git -C $repository rev-parse HEAD); build identity $expected", '', '| File | SHA-256 |', '|---|---|') + $hashes + '' + $corpusLines + '' + $results
if ($env:KIMI_TATARA_CORPUS) {
    $rows = @(Get-Content -LiteralPath (Join-Path $env:KIMI_TATARA_CORPUS 'results.tsv') -ErrorAction SilentlyContinue)
    $report += '## Corpus rows', '', '| Outcome | Rows |', '|---|---|'
    $report += $rows | ForEach-Object { ($_ -split "`t")[1] } | Group-Object | ForEach-Object { "| $($_.Name) | $($_.Count) |" }
    $report += '', '## Rows that differ or failed', ''
    $report += $rows | Where-Object { ($_ -split "`t")[1] -notlike 'equal*' } | ForEach-Object { "- $_" }
}
$report | Set-Content -LiteralPath (Join-Path $Output 'report.md') -Encoding utf8
