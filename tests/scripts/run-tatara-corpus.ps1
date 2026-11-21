# Runs TataraExecutorTest and TataraCommandNativeTest against a toolchain root of this run's own (Tatara designs A2 §4.5 and A4 §2.6):
# the root is a copy of the shared toolchain plus the given tatara.dll, set for this process only; a DLL whose build identity is not
# this checkout's (src/Tatara/identity.ps1) is refused. Without -Fixtures the tool control comparison runs its fixed rows; with it, its
# corpus is those fixture .ll files at O0 and O2 (an LLVM update's gate, plan §6). Each fatal row runs in a child test process of its
# own, which its class's parent row starts. Writes <Output>/report.md with the run's identity (the checkout, its state and the
# binaries' hashes, read at the start), the test results and the corpus counts. The caller wraps the run in its own guard.
param(
    [Parameter(Mandatory)] [string] $Dll,
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
$checkout = @("Checkout at the start: $(git -C $repository rev-parse HEAD)", '', '``` git status --porcelain') + @(git -C $repository status --porcelain) + '```'

# The DLL's identity, read in a child process so this process maps no tatara.dll.
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
$hashed = @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Name -in 'tatara.dll', 'opt.exe', 'llc.exe', 'lld-link.exe', 'llvm-nm.exe', 'llvm-readobj.exe', 'clang.exe', 'llvm-lib.exe' }) +
    @(Get-Item -LiteralPath $kimi, $tests)
$hashes = @($hashed | ForEach-Object { "| $($_.Name) | $((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()) |" })

$env:KIMI_TOOLCHAIN_ROOT = $root
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
    $report += $rows | ForEach-Object { ($_ -split "`t")[1] } | Group-Object | ForEach-Object { "| $($_.Name) | $($_.Count) |" }
    $report += '', '## Rows that differ or failed', ''
    $report += $rows | Where-Object { ($_ -split "`t")[1] -notlike 'equal*' } | ForEach-Object { "- $_" }
}
$report | Set-Content -LiteralPath (Join-Path $Output 'report.md') -Encoding utf8
