# Fixed O2 timing corpus and compiler pipeline series (src/Benchmark/O2Corpus.md); timing is opt-in, never a Verify assertion.
# The baseline (A) and candidate (B) checkouts, each defaulting to this script's checkout, need Release builds of Kimi and
# Benchmark. Every kernel is built at -Optimization (O2, or O0 for the O0 times) by both compilers and checked; the executables
# then run A, B, A, ..., A, and every run's output is checked. Benchmark --compiler-pipeline of each checkout then runs A, B, A,
# ..., A. With B equal to A, the series records the variation of matched runs (P4). -CandidateEnvironment NAME=value pairs are set only for B's builds and
# B's --compiler-pipeline runs; A runs the default pipeline and code generator.
[CmdletBinding()]
param(
    [string] $Baseline = '',
    [string] $Candidate = '',
    # pwsh -File binds A=x,B=y as one string; a second pair inside a value is rejected rather than silently dropped.
    [ValidatePattern('^[A-Za-z_]\w*=(?!.*,[A-Za-z_]\w*=)')] [string[]] $CandidateEnvironment = @(),
    [ValidateRange(1, 99)] [int] $Samples = 15,
    [ValidateRange(1, 9)] [int] $PipelineRuns = 3,
    [ValidateSet('O0', 'O2')] [string] $Optimization = 'O2',
    [string] $ToolchainRoot = '',
    [string] $Name = ''
)
$ErrorActionPreference = 'Stop'
# A's processes get only the inherited environment, so an inherited selector would leave A off the default.
$selectors = @(Get-ChildItem env: | Where-Object Name -In 'KIMI_PIPELINE' | ForEach-Object Name)
if ($selectors) { throw "The baseline (A) would inherit $($selectors -join ', '); unset it and pass B's selectors with -CandidateEnvironment." }
. (Join-Path $PSScriptRoot 'toolchain.ps1')
$ToolchainRoot = Resolve-KimiToolchainRoot $ToolchainRoot
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$roots = [ordered]@{
    A = $(if ($Baseline) { [IO.Path]::GetFullPath($Baseline) } else { $repo })
    B = $(if ($Candidate) { [IO.Path]::GetFullPath($Candidate) } else { $repo })
}
$expected = [ordered]@{
    Array = 'checksum 1917245123'; ArrayLiteral = 'checksum 996929325'; Dictionary = 'checksum 906592013'; ErasedCalls = 'checksum 1506170851'
    Interpolation = 'checksum 1676251827'; Iterators = 'checksum 2503774365'; SharedHandles = 'checksum 3194390882'
    VirtualDispatch = 'checksum 3854058029'
}
$kernels = Get-ChildItem -LiteralPath (Join-Path $repo 'src/Benchmark/Kimi/Corpus') -Filter '*.kimi' -File | Sort-Object Name
if ((@($kernels.BaseName) -join ',') -cne (@($expected.Keys | Sort-Object) -join ',')) { throw 'Every corpus kernel needs exactly one expected output.' }
$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$root = Join-Path $repo "artifacts/benchmarks/o2-corpus/$stamp$(if ($Name) { "-$Name" })"
$work = Join-Path $repo "temp/o2-corpus/$stamp"
New-Item -ItemType Directory -Path $root, $work -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$dotnet = (Get-Command dotnet).Source

# Builds, tests and native tools with their CPU seconds so far: on a quiet machine they do not change between start and end.
function Get-Busy {
    $pattern = '^(dotnet|MSBuild|VBCSCompiler|xUnitTest|testhost|clang|lld-link|opt|llc|Kimi)$'
    return @(Get-Process | Where-Object ProcessName -Match $pattern | ForEach-Object { "$($_.ProcessName) $($_.Id) $([Math]::Round($_.TotalProcessorTime.TotalSeconds, 1)) s" })
}

# Wall seconds and the child's CPU seconds: wall time well above CPU time shows that the run waited or was preempted.
function Invoke-Timed([string] $File, [string[]] $Arguments = @(), [string[]] $Environment = @()) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($pair in $Environment) { $variable, $value = $pair -split '=', 2; $start.Environment[$variable] = $value }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(600000)) { $process.Kill($true); throw "Timed out: $File" }
        $seconds = $watch.Elapsed.TotalSeconds
        return @{ seconds = $seconds; cpuSeconds = $process.TotalProcessorTime.TotalSeconds; stdout = $stdout.GetAwaiter().GetResult(); stderr = $stderr.GetAwaiter().GetResult(); exitCode = $process.ExitCode }
    }
    finally { $process.Dispose() }
}

function Get-Statistics([double[]] $Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    $mean = ($Values | Measure-Object -Average).Average
    $squares = ($Values | ForEach-Object { ($_ - $mean) * ($_ - $mean) } | Measure-Object -Sum).Sum
    $deviation = [Math]::Sqrt($squares / [Math]::Max(1, $Values.Count - 1))
    return [ordered]@{ count = $Values.Count; min = $sorted[0]; median = $median; max = $sorted[-1]; mean = $mean; stdDev = $deviation; cvPercent = 100 * $deviation / $mean }
}

# Each B value against the mean of the A values run just before and after it.
function Get-Ratios([double[]] $A, [double[]] $B) {
    return Get-Statistics ([double[]] (0..($B.Count - 1) | ForEach-Object { $B[$_] / (($A[$_] + $A[$_ + 1]) / 2) }))
}

# A checkout and the variables set for its builds and pipeline runs.
function Get-Checkout([string] $Path, [string[]] $Environment) {
    $compiler = Join-Path $Path 'src/Kimi/bin/Release/net10.0/Kimi.dll'
    $benchmark = Join-Path $Path 'src/Benchmark/bin/Release/net10.0/Benchmark.dll'
    $hash = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath (Join-Path (Split-Path $benchmark) 'Kimi.dll') -Algorithm SHA256).Hash -cne $hash) { throw "Benchmark's Kimi.dll is not this compiler: $Path" }
    return [ordered]@{
        root = $Path; commit = (git -C $Path rev-parse HEAD); status = @(git -C $Path status --porcelain=v1)
        compiler = $compiler; compilerSha256 = $hash; benchmark = $benchmark; benchmarkSha256 = (Get-FileHash -LiteralPath $benchmark -Algorithm SHA256).Hash
        environment = @($Environment)
    }
}

function Build-Kernel($Source, [string] $Variant, $Checkout) {
    $directory = Join-Path $work "$Variant/$($Source.BaseName)"
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item -LiteralPath $Source.FullName -Destination $directory
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $directory $Source.Name) -Algorithm SHA256).Hash # The source this build compiles.
    $project = Join-Path $directory "$($Source.BaseName).kimiproj"
    [IO.File]::WriteAllText($project, "Targets=`n  `"x86_64-pc-windows-msvc`"`nOutputKind=`"Application`"`nOptimization=`"$Optimization`"`n", $utf8)
    $build = Invoke-Timed $dotnet @($Checkout.compiler, 'build', $project, '--ToolchainRoot', $ToolchainRoot) $Checkout.environment
    if ($build.exitCode -ne 0) { throw "Build failed: $Variant $($Source.Name)`n$($build.stdout)$($build.stderr)" }
    $bin = Join-Path $directory 'bin/x86_64-pc-windows-msvc'
    $exe = Join-Path $bin "$($Source.BaseName).$Optimization.exe"
    $stream = [IO.File]::OpenRead($exe)
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try { $text = $pe.PEHeaders.SectionHeaders | Where-Object Name -CEQ '.text' }
    finally { $pe.Dispose(); $stream.Dispose() }
    # Instructions without addresses, displacements and immediates: equal sequences are the same code.
    $listing = & (Join-Path $ToolchainRoot 'llvm-objdump.exe') -d --no-show-raw-insn $exe
    $code = (($listing | Select-Object -Skip 2) -replace '^\s*[0-9a-f]+:\s*', '' -replace '#.*$', '' -replace '0x[0-9a-f]+', 'X') -join "`n"
    if ($LASTEXITCODE -ne 0 -or $null -eq $text -or $code.Length -eq 0) { throw "No code or .text for $exe" }
    return [ordered]@{
        exe = $exe; sourceSha256 = $sourceHash; buildSeconds = $build.seconds; irBytes = (Get-Item (Join-Path $bin "$($Source.BaseName).ll")).Length
        # An O0 build runs no opt, so it writes no optimized IR.
        optimizedIrBytes = $(if ($Optimization -eq 'O2') { (Get-Item (Join-Path $bin "$($Source.BaseName).O2.ll")).Length }); textVirtualBytes = $text.VirtualSize
        codeSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($code)))
    }
}

function Invoke-Checked([string] $Exe, [string] $Kernel) {
    $run = Invoke-Timed $Exe
    if ($run.exitCode -ne 0 -or $run.stderr -cne '' -or $run.stdout -cne "$($expected[$Kernel])`n") { throw "Wrong result: $Exe (exit $($run.exitCode)): $($run.stdout)$($run.stderr)" }
    return $run
}

# A report another one is compared with must measure the same stages of the same inputs.
function Assert-Comparable($Report, $Reference) {
    if ($Report.schemaVersion -ne $Reference.schemaVersion -or ($Report.stages -join ',') -cne ($Reference.stages -join ',') -or
        (@($Report.results | ForEach-Object { "$($_.shape) $($_.size) $($_.sourceSha256)" }) -join ',') -cne (@($Reference.results | ForEach-Object { "$($_.shape) $($_.size) $($_.sourceSha256)" }) -join ',')) {
        throw 'Pipeline reports differ in schema, stages or inputs.'
    }
}

$conditions = [ordered]@{
    started = (Get-Date).ToString('o'); machine = [Environment]::MachineName; processors = [Environment]::ProcessorCount
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; powerScheme = (powercfg /getactivescheme | Out-String).Trim()
    dotnet = (dotnet --version); toolchainRoot = $ToolchainRoot; optimization = $Optimization; samples = $Samples; pipelineRuns = $PipelineRuns
    environment = @(Get-ChildItem env: | Where-Object Name -Match '^(KIMI_|DOTNET_)' | ForEach-Object { "$($_.Name)=$($_.Value)" })
    order = 'A, B, A, ..., A per kernel and for the pipeline; each B against the mean of its neighboring A runs; process start included; no warm-up'
    script = [ordered]@{ root = $repo; commit = (git -C $repo rev-parse HEAD); status = @(git -C $repo status --porcelain=v1) }
    busyProcessesAtStart = Get-Busy
}
$results = [Collections.Generic.List[object]]::new()
$stages = [Collections.Generic.List[object]]::new()
$status = 'incomplete'
$failure = $null
try {
    $conditions.checkouts = [ordered]@{ A = Get-Checkout $roots.A @(); B = Get-Checkout $roots.B $CandidateEnvironment }
    # Every executable is built and checked before any timing, so no build runs during the series.
    $builds = [ordered]@{}
    foreach ($kernel in $kernels) {
        $builds[$kernel.BaseName] = [ordered]@{}
        foreach ($variant in 'A', 'B') {
            $build = Build-Kernel $kernel $variant $conditions.checkouts[$variant]
            $build.checkedRunSeconds = (Invoke-Checked $build.exe $kernel.BaseName).seconds
            $builds[$kernel.BaseName][$variant] = $build
        }

        if ($builds[$kernel.BaseName].A.sourceSha256 -cne $builds[$kernel.BaseName].B.sourceSha256) { throw "A and B built different sources: $($kernel.Name)" }
    }

    foreach ($kernel in $kernels) {
        $built = $builds[$kernel.BaseName]
        $runs = @{ A = [Collections.Generic.List[object]]::new(); B = [Collections.Generic.List[object]]::new() }
        for ($i = 0; $i -le $Samples; $i++) {
            $runs.A.Add((Invoke-Checked $built.A.exe $kernel.BaseName))
            if ($i -lt $Samples) { $runs.B.Add((Invoke-Checked $built.B.exe $kernel.BaseName)) }
        }

        $secondsA = [double[]] $runs.A.seconds
        $secondsB = [double[]] $runs.B.seconds
        $row = [ordered]@{
            kernel = $kernel.BaseName; sourceSha256 = $built.A.sourceSha256
            sameCode = $built.A.codeSha256 -ceq $built.B.codeSha256; builds = $built
            secondsA = $secondsA; secondsB = $secondsB; cpuSecondsA = [double[]] $runs.A.cpuSeconds; cpuSecondsB = [double[]] $runs.B.cpuSeconds
            statisticsA = Get-Statistics $secondsA; statisticsB = Get-Statistics $secondsB; ratioBA = Get-Ratios $secondsA $secondsB
        }
        $results.Add($row)
        Write-Output ("{0,-16} A median {1:N4} s (cv {2:N1}%)  B median {3:N4} s (cv {4:N1}%)  B/A median {5:N4} [{6:N4}, {7:N4}]  same code {8}" -f
            $row.kernel, $row.statisticsA.median, $row.statisticsA.cvPercent, $row.statisticsB.median, $row.statisticsB.cvPercent, $row.ratioBA.median,
            $row.ratioBA.min, $row.ratioBA.max, $row.sameCode)
    }

    $pipeline = @{ A = [Collections.Generic.List[object]]::new(); B = [Collections.Generic.List[object]]::new() }
    for ($i = 0; $i -le $PipelineRuns; $i++) {
        foreach ($variant in $(if ($i -lt $PipelineRuns) { 'A', 'B' } else { 'A' })) {
            $run = Invoke-Timed $dotnet @($conditions.checkouts[$variant].benchmark, '--compiler-pipeline') $conditions.checkouts[$variant].environment
            $path = Join-Path $root "pipeline-$variant-$($pipeline[$variant].Count + 1)"
            [IO.File]::WriteAllText("$path.json", $run.stdout, $utf8)
            [IO.File]::WriteAllText("$path.stderr.log", $run.stderr, $utf8)
            if ($run.exitCode -ne 0) { throw "Pipeline measurement failed: $variant" }
            $report = $run.stdout | ConvertFrom-Json
            if ($pipeline.A.Count -gt 0) { Assert-Comparable $report $pipeline.A[0] }
            $pipeline[$variant].Add($report)
        }
    }

    # Collect plus codegen is emission lowering and IR writing until R5 replaces them. Each run gives its sample median, so the
    # spread across runs is the variation of matched runs.
    $reference = $pipeline.A[0]
    $lowering = [Array]::IndexOf([string[]] $reference.stages, 'EmissionLowering')
    $writing = [Array]::IndexOf([string[]] $reference.stages, 'LlvmIrWriting')
    if ($lowering -lt 0 -or $writing -lt 0) { throw 'The pipeline report has no collect-plus-codegen stages.' }
    foreach ($case in $reference.results) {
        $per = @{}
        foreach ($variant in 'A', 'B') {
            $medians = foreach ($report in $pipeline[$variant]) {
                $match = $report.results | Where-Object { $_.size -eq $case.size -and $_.shape -ceq $case.shape }
                $scale = $report.iterationsPerSample * $report.stopwatchFrequency / 1000.0
                [pscustomobject]@{
                    collectCodegen = (Get-Statistics ([double[]] ($match.samples | ForEach-Object { ($_.stageTicks[$lowering] + $_.stageTicks[$writing]) / $scale }))).median
                    total = (Get-Statistics ([double[]] ($match.samples | ForEach-Object { $_.totalTicks / $scale }))).median
                    collectCodegenBytes = $match.stages[$lowering].meanAllocatedBytes + $match.stages[$writing].meanAllocatedBytes
                }
            }
            $per[$variant] = [ordered]@{
                collectCodegenMilliseconds = [double[]] $medians.collectCodegen; totalMilliseconds = [double[]] $medians.total
                collectCodegenAllocatedBytes = [double[]] $medians.collectCodegenBytes; collectCodegenStatistics = Get-Statistics ([double[]] $medians.collectCodegen)
            }
        }

        $row = [ordered]@{
            shape = $case.shape; size = $case.size; A = $per.A; B = $per.B
            collectCodegenRatioBA = Get-Ratios $per.A.collectCodegenMilliseconds $per.B.collectCodegenMilliseconds
            totalRatioBA = Get-Ratios $per.A.totalMilliseconds $per.B.totalMilliseconds
        }
        $stages.Add($row)
        Write-Output ("{0,-9} {1,4}: collect+codegen A median {2:N3} ms (cv {3:N1}% across runs)  B/A median {4:N4} [{5:N4}, {6:N4}]; {7:N0} bytes" -f
            $case.shape, $case.size, $per.A.collectCodegenStatistics.median, $per.A.collectCodegenStatistics.cvPercent, $row.collectCodegenRatioBA.median,
            $row.collectCodegenRatioBA.min, $row.collectCodegenRatioBA.max, $per.A.collectCodegenAllocatedBytes[0])
    }

    $status = 'passed'
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    $conditions.finished = (Get-Date).ToString('o')
    $conditions.busyProcessesAtEnd = Get-Busy
    [ordered]@{ status = $status; error = $failure; conditions = $conditions; kernels = $results; pipeline = $stages } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
    Write-Output "Summary: $root ($status)"
}
