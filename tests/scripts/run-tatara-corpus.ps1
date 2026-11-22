# Runs TataraExecutorTest and TataraCommandNativeTest (Tatara designs A2 §4.5 and A4 §2.6) on the tatara.dll they load, the copy the
# build of Kimi puts beside the xUnit output's Kimi.dll (Tatara B6b1: from the checkout's toolchain; a DLL of a unit's own reaches it
# through a toolchain directory of the worktree's own, then a build), and the checkout's toolchain's tools; a DLL whose build identity
# is not this checkout's (src/Tatara/identity.ps1) is refused. Without -Fixtures the tool control comparison runs its fixed rows; with it, its
# corpus is those fixture .ll files at O0 and O2 (an LLVM update's gate, plan §6). Each fatal row runs in a child test process of its
# own, which its class's parent row starts. Writes <Output>/report.md with the run's identity (the checkout, its state and the
# binaries' hashes, read at the start), the test results, the corpus counts and the rows the comparison failed. The caller wraps the
# run in its own guard.
param(
    [Parameter(Mandatory)] [string] $Output,
    [string] $Fixtures,
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$tests = Join-Path $repository "tests\xUnitTest\bin\$Configuration\net10.0\xUnitTest.dll"
$kimi = Join-Path $repository "tests\xUnitTest\bin\$Configuration\net10.0\Kimi.dll"
$tatara = Join-Path $repository "tests\xUnitTest\bin\$Configuration\net10.0\tatara.dll"
$checkout = @("Checkout at the start: $(git -C $repository rev-parse HEAD)", '', '``` git status --porcelain') + @(git -C $repository status --porcelain) + '```'

# The DLL's identity, read in a child process so this process maps no tatara.dll; the build copies it only from a checkout that has it.
if (-not (Test-Path -LiteralPath $tatara -PathType Leaf)) { throw "Missing tatara.dll: $tatara. Build it with src/Tatara/build.ps1 into a toolchain directory of this checkout's own, then build Kimi." }
. (Join-Path $repository 'src\Tatara\identity.ps1')
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
$build = (& pwsh -NoProfile -Command "& { $read } '$tatara'" | Select-Object -Last 1)
if ($build -cne $expected) { throw "Stale tatara.dll: $tatara has build $build; this checkout's is $expected. Rebuild it with src/Tatara/build.ps1 into a toolchain directory of this checkout's own, then build Kimi." }

# The files the run tests: the DLL, the tools the tests run (the checkout's toolchain), the compiler and the tests.
$tools = Join-Path $repository 'toolchain'
$hashed = @(Get-Item -LiteralPath $tatara) + @(Get-ChildItem -LiteralPath $tools -File | Where-Object { $_.Name -in 'opt.exe', 'llc.exe', 'lld-link.exe', 'llvm-nm.exe', 'llvm-readobj.exe', 'clang.exe', 'llvm-lib.exe' }) +
    @(Get-Item -LiteralPath $kimi, $tests)
$hashes = @($hashed | ForEach-Object { "| $($_.FullName) | $((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()) |" })

$env:KIMI_TATARA_CORPUS = $null
$corpusLines = @()
if ($Fixtures) {
    # The corpus: the fixtures, read in place through a junction.
    $corpus = Join-Path $Output 'corpus'
    Remove-Item -LiteralPath $corpus -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $corpus | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $corpus 'fixtures') -Target ([IO.Path]::GetFullPath($Fixtures)) | Out-Null
    $env:KIMI_TATARA_CORPUS = $corpus
    $corpusLines += "Fixtures: $Fixtures ($((Get-ChildItem -LiteralPath $Fixtures -Filter '*.ll').Count) .ll files)"
}

# Each class; its fatal rows' parent rows start their child processes.
$results = @()
foreach ($class in 'XunitTest.TataraExecutorTest', 'XunitTest.TataraCommandNativeTest') {
    $xml = Join-Path $Output "$class.xml"
    $log = & dotnet $tests -class $class -xml $xml 2>&1 | Out-String
    $results += "## $class (exit $LASTEXITCODE)", '', '```', $log.Trim(), '```', ''
}

$report = @('# Tatara corpus run', '') + $checkout + @('', "Build identity $expected", '', '| File | SHA-256 |', '|---|---|') + $hashes + '' + $corpusLines + '' + $results
if ($env:KIMI_TATARA_CORPUS) {
    $rows = @(Get-Content -LiteralPath (Join-Path $env:KIMI_TATARA_CORPUS 'results.tsv') -ErrorAction SilentlyContinue)
    $report += '## Corpus rows', '', '| Outcome | Rows |', '|---|---|'
    # Each line is "<row>`t<Run's decision, pass or fail>`t<outcome>" (TataraExecutorTest's Run, which owns the acceptance).
    $report += $rows | ForEach-Object { ($_ -split "`t")[2] } | Group-Object | ForEach-Object { "| $($_.Name) | $($_.Count) |" }
    $report += '', '## Rows Run failed', ''
    $report += $rows | Where-Object { ($_ -split "`t")[1] -ceq 'fail' } | ForEach-Object { "- $_" }
}
$report | Set-Content -LiteralPath (Join-Path $Output 'report.md') -Encoding utf8
