# Documentation Markdown measurements — 2026-09-20

DM4 compares the independent parser with Markdig and measures improvements against
the DM3 implementation at commit `4c53331`. The existing Markdig-backed product
entry point remains unchanged. These measurements cover syntax parsing and the
implemented query APIs; they do not measure complete documentation generation.

## Results

All tables report medians. Time and allocation totals exclude input construction.
The six representative inputs are defined in
[DocumentationMarkdownMeasurements.cs](Benchmarks/DocumentationMarkdownMeasurements.cs).

| Parse workload | UTF-16 characters | Independent ns/op | Markdig ns/op | Independent B/op | Markdig B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Plain summary | 64 | 150.7 | 437.0 | 216 | 568 |
| Formatted summary | 74 | 1,353.3 | 1,513.2 | 920 | 1,856 |
| 12 parameter descriptions | 392 | 4,362.7 | 7,125.8 | 2,608 | 7,080 |
| 256-line fenced code example | 5,653 | 8,731.0 | 19,157.5 | 608 | 21,040 |
| Nested lists/quotes | 95 | 2,154.5 | 4,275.3 | 1,336 | 3,944 |
| 32 lines of links/entities | 1,632 | 16,725.2 | 22,140.1 | 15,664 | 37,824 |

The geometric mean of the six independent/Markdig ratios is **0.566 for time**
and **0.256 for allocated bytes** (43.4% and 74.4% reductions). Each workload has
equal weight; this is not a forecast of production input frequency or whole-compiler
performance. Markdig implements a broader language and uses a different tree model.

### Measured implementation changes

1. Construct the immutable arena directly for an empty input or a plain single-line
   paragraph. Conservatively defer possible block openers and inline syntax to the
   general parser. Preserve trimming, ranges, depth limits and cancellation.
2. Coalesce contiguous physical fenced-code lines into borrowed source slices.
   Removed prefixes, indentation, partial tabs and decoded NULs split slices;
   an absent physical final newline still produces the required synthetic LF.
3. Serialize only initial candidate extraction on the private node array, avoiding
   duplicate extraction and a separate lock allocation. Completed reads remain
   lock-free. Cancellation/failure publishes nothing and allows a later retry.
   A cancelled waiter checks its token after acquiring the monitor; monitor waiting
   itself is not cancellable.

The following comparison loads DM3 and the final implementation into the same
process and alternates 15 samples through matching delegate signatures. It avoids
attributing differences between separate benchmark sessions to these changes.

| Parse workload | DM3 ns/op | Final ns/op | DM3 B/op | Final B/op |
| --- | ---: | ---: | ---: | ---: |
| Plain summary | 390.3 | 140.5 | 456 | 216 |
| Formatted summary | 1,396.8 | 1,329.3 | 920 | 920 |
| 12 parameter descriptions | 4,484.3 | 4,291.8 | 2,608 | 2,608 |
| 256-line fenced code example | 39,456.0 | 8,854.8 | 23,088 | 608 |
| Nested lists/quotes | 2,201.9 | 2,128.4 | 1,336 | 1,336 |
| Links/entities | 17,468.2 | 16,924.9 | 15,664 | 15,664 |

The geometric mean final/DM3 ratio is **0.640 for time** and **0.482 for allocated
bytes** (36.0% and 51.8% reductions). The large gains are in plain text and fenced
code; small timing changes in the other cases are not treated as distinct
optimizations. No representative parse case increased allocations or regressed
more than 10% in time. The pre-tuning gate required at least a 10% geometric-mean
improvement in time or allocations when retaining changes, and is met.

## Other operations and resource behavior

### Queries and candidate processing

The Markdig item extractor/classifier in the harness is a **fixture-only adapter**
for the generated root parameter lists. Setup verifies candidate names,
description ranges and classification match counts. This is neither Markdig core
functionality nor a benchmark of the old product's complete publication API.

| Operation | Independent | Comparison | Allocated bytes, independent / comparison |
| --- | ---: | ---: | ---: |
| Parse + extract 8 items | 3.91 µs | 5.81 µs | 2,504 / 6,472 |
| Parse + extract 128 items | 51.24 µs | 79.02 µs | 32,264 / 93,560 |
| Parse + extract 2,048 items | 826.89 µs | 1,347.32 µs | 508,424 / 2,062,080 |
| Classify cached 2,048 items | 102.04 µs | 97.36 µs | 147,400 / 131,016 |

Classification is about 4.8% slower and allocates about 12.5% more than the adapter
at 2,048 items. The independent result carries a 40-byte item with a snapshot node
handle; the adapter carries a 32-byte fixture item. These are different result
contracts. This step retains that metadata and cancellation behavior.

Cached summary and candidate queries allocate **0 B/op**, including at 2,048
items. Their approximately 3–6 ns timings include the harness call and sink write;
the comparison is a Markdig root or a precomputed array, not an equivalent API.

Mapping 1,024 original-source ranges takes 39.77 / 98.64 / 130.54 µs at 64 / 1,024 /
16,384 lines, with **0 allocated bytes**. The comparison side is the same
`DocumentationText` binary-search mapper without the document wrapper (39.37 /
98.29 / 127.71 µs). It is labeled `Markdig` by the generic paired JSON schema but
does not call a Markdig source-mapping API. Setup checks every mapped result.

### Concurrent first extraction

Dedicated diagnostics release 1, 4 or 16 prepared workers simultaneously, after
two untimed batches, with seven recorded samples. Affinity is restored to the
host's original allowed CPUs. Parsing and task creation are outside the interval;
gate release, scheduling and completion overhead remain inside it. Allocations
are measured across all threads.

| 2,048-item initial extraction | Before synchronization µs / B | Final µs / B |
| --- | ---: | ---: |
| 1 request | 108.7 / 147,608 | 108.5 / 147,608 |
| 4 simultaneous requests | 338.9 / 590,072 | 151.0 / 147,632 |
| 16 simultaneous requests | 284.7 / 1,038,824 | 197.4 / 147,728 |

These are separate diagnostic runs and timings are sensitive to scheduling.
The allocation evidence confirms that initial extraction no longer scales with
the number of competing callers. Sixteen cached requests cost 103.2 µs and 248 B
including coordination; the warmed single-thread query itself allocates zero.
The normal run's single-observation `Concurrent-first-extract-*` rows are preliminary
probes with scheduler/JIT overhead; the dedicated diagnostics supersede them.

### Scaling and malformed inputs

Eight input families use four increasing sizes. The table compares the largest
two sizes using actual UTF-16 length, not the generator's size label.

| Family | Input growth | Independent time growth |
| --- | ---: | ---: |
| Plain prose | 4.00× | 2.83× |
| Repeated blocks | 4.00× | 4.43× |
| Failed links | 4.00× | 4.56× |
| Failed quoted titles | 4.00× | 4.38× |
| Emphasis delimiters | 4.00× | 4.02× |
| Unmatched brackets | 4.00× | 3.76× |
| Quote depth 512 → 2,048 | 3.97× | 4.21× |
| Unequal backtick runs 128 → 256 | 3.92× | 2.45× |

All meet the pre-tuning investigative threshold of less than 6× time for
approximately 4× input. No unexplained quadratic trend was observed in these
families; this finite corpus is not a proof for every input.

Markdig rejects `Delimiters-16384` and `Unmatched-brackets-16384` with its internal
depth-limit error even with the pipeline depth setting raised. JSON records the
error and empty samples, never a zero-time success or a speedup ratio. Precise
locations also expose near-quadratic timings in some successful adversarial
Markdig cases, such as failed links. These are excluded from the six-case headline.

The independent representation has a memory tradeoff for unequal unmatched
backticks: at 33,408 characters it allocates **39,000 B versus Markdig's 2,600 B**,
while taking 78.45 µs versus 2,729.21 µs. The extra source-sliced nodes preserve
the independent representation; this step does not claim minimum allocations
for every malformed input.

### Retained memory, first calls and interruption

Forced-GC retained-result estimates, rounded to bytes, are:

| Workload | Independent B/result | Markdig B/result |
| --- | ---: | ---: |
| Plain summary | 215 | 455 |
| Formatted summary | 668 | 1,312 |
| Parameter descriptions | 2,344 | 6,699 |
| Fenced code example | 358 | 12,494 |
| Nested lists/quotes | 1,072 | 3,144 |
| Links/entities | 15,375 | 32,839 |

These are heap-delta estimates across 8–256 retained roots with preallocated root
arrays, shared pre-existing input strings and warmed pools. They are not object
size proofs or retained-memory limits. Final whole-process peak working set was
298,905,600 B; the baseline run was 262,127,616 B. Both include the harness,
generated corpus, runtime and pools, so this metric does not establish a
per-parser peak-memory improvement.

Three fresh child processes per workload/engine measure the first parse call,
including parser JIT/static initialization and Markdig pipeline creation but
excluding process startup and input generation:

| Workload | Independent median ms | Markdig median ms |
| --- | ---: | ---: |
| Plain summary | 6.75 | 52.74 |
| Formatted summary | 55.93 | 76.14 |
| Parameter descriptions | 50.93 | 67.79 |
| Fenced code example | 46.81 | 63.30 |
| Nested lists/quotes | 47.19 | 70.70 |
| Links/entities | 55.64 | 95.07 |

Three launches are descriptive evidence, not a general startup guarantee. Raw
first-call allocations are retained in JSON and include runtime/pool initialization.

Pre-cancelled parsing costs 2.04 µs / 472 B and a depth interruption 13.26 µs /
1,168 B, including exception handling. Seven `CancelAfter(1)` requests on a
2.8-million-character input all interrupt; final median elapsed time is 14.11 ms
and allocation 1,104 B. Timing starts before scheduling cancellation, so a sample
can cancel before parser work begins; these figures do not isolate polling latency
or promise a 1 ms deadline. A subsequent successful parse of the same input costs
47.62 ms / 30,800,368 B. Cancellation timing and pool warmth explain differences
from the earlier diagnostic run; no cancellation-speedup claim is made.

## Method and reproduction

- Windows x64 build 10.0.26100; Intel Core i7-1280P, 20 logical processors;
  .NET SDK 10.0.401 and runtime 10.0.12; Release build.
- `DOTNET_TieredCompilation=0`; no tiered compilation or dynamic PGO. Normal and
  paired runs pin only the benchmark process to the lowest allowed logical CPU
  (mask 1 on this host). Builds/tests do not run concurrently with measurements.
- Seven alternating samples after at least 50 ms warm-up; adaptive batch
  calibration targets at least 20 ms. Fifteen samples and matching batch counts
  for the DM3/final pair. Raw samples expose variability; no confidence interval
  or universal performance guarantee is inferred from these small runs.
- `Stopwatch` time and `GC.GetAllocatedBytesForCurrentThread` per operation;
  diagnostic concurrency uses `GC.GetTotalAllocatedBytes(true)`. Inputs, delegates
  and query setup are outside warm intervals. Results are retained or consumed.
- Identical LF text; Markdig `DisableHtml()` and `UsePreciseSourceLocation()`.
  Markdig NuGet version 1.3.2 reports assembly version 1.3.0.0. Stress parsing raises
  the independent tree limit to 8,192 and Markdig's configured limit to 1,000,000;
  normal product defaults are unchanged. Shared-profile correctness is covered
  by DM3 tests; the profiles and internal node shapes are not identical.
- Results and input hashes:
  [DM3 baseline](Results/DocumentationMarkdown/2026-09-20-baseline.json),
  [final Markdig comparison](Results/DocumentationMarkdown/2026-09-20-optimized.json),
  [same-process DM3/final pair](Results/DocumentationMarkdown/2026-09-20-paired.json),
  [before synchronization diagnostics](Results/DocumentationMarkdown/2026-09-20-diagnostics.json),
  [final diagnostics](Results/DocumentationMarkdown/2026-09-20-diagnostics-final.json).

Kimi.dll SHA-256:

```text
DM3:   E92DA378655D83EE3606F353BDB3F8FACF29229DE8B93FBC16B3C07728CE662F
Final: 20531AE7763BB32713A2434CF1BF4540B083323FE85463B7C58866E075A365DA
```

Run from the repository root in PowerShell:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
$env:DOTNET_TieredCompilation = '0'
dotnet Benchmark/bin/Release/net10.0/Benchmark.dll --documentation-markdown bin/documentation-markdown/comparison.json
dotnet Benchmark/bin/Release/net10.0/Benchmark.dll --documentation-markdown diagnostics bin/documentation-markdown/diagnostics.json
dotnet Benchmark/bin/Release/net10.0/Benchmark.dll --documentation-markdown paired bin/documentation-markdown/dm4-baseline-binaries/Kimi.dll bin/documentation-markdown/paired.json
```

For the last command, preserve a Release build of commit `4c53331` **with its DLL
dependencies** in the indicated directory (or supply another absolute DLL path).
Build it in a separate checkout before comparing with the current harness. The
baseline directory is a local ignored artifact, not supplied by the result JSON.
The harness isolates baseline dependencies in an `AssemblyLoadContext`; sharing
Tinyhand between the two product assemblies causes duplicate registration.

## Verification and remaining scope

Eighteen added regression cases cover the plain fast path and fenced-code slice
boundaries. Final Debug/Release solution builds have zero warnings/errors. All
**10,175 managed tests per configuration** pass, including **965 documentation
tests** covering conformance, Markdig differences, ranges, cancellation,
concurrency and Kimigayo integration. The test inputs preserve collection-on/off
IR equivalence. Native execution was not repeated in DM4; NativeAOT was not run.

DM4's implemented-parser gates are satisfied. HTML rendering, structured URL
resolution, compiler publication integration and their output benchmarks remain
DM5 work before switching the product path. No specification or draft was changed.
