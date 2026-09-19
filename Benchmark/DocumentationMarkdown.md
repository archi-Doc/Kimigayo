# Documentation Markdown measurements — 2026-09-20

The product now uses the independent parser and renderer; see
[DM5 product output](#dm5-product-output-and-switch-2026-09-20) and the
[product API guide](../Kimi/Compiler/Documentation/README.md). The DM3/DM4 sections
below preserve measurements taken before that switch.

DM4 compares the independent parser with Markdig and measures improvements against
the DM3 implementation at commit `4c53331`. The existing Markdig-backed product
entry point remains unchanged. These measurements cover syntax parsing and the
implemented query APIs; they do not measure complete documentation generation.

## Second tuning round (2026-09-20, after DM4)

A review of `Kimi/Compiler/Documentation` after DM4 (baseline commit `cde175c`)
corrected one CommonMark deviation and reduced parse time and allocations further.
The baseline for the same-process pair below is a Release build of `cde175c`
(`Kimi.dll` SHA-256 `3DA9F59F1D0D5BE01AB72F01C0F46E1C3EF813CFEE9118B250BE0BB22F4F7987`);
the tuned build is `8F42F7EA41D599DF737D9D91CC29355D176E6FC39D321FFAAEAF232515D76EF4`.

| Parse workload | UTF-16 characters | DM4 ns/op | Tuned ns/op | DM4 B/op | Tuned B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Plain summary | 64 | 127.6 | 133.8 | 216 | 192 |
| Formatted summary | 74 | 1,253.0 | 872.7 | 920 | 536 |
| 12 parameter descriptions | 392 | 3,851.8 | 1,927.9 | 2,608 | 1,952 |
| 256-line fenced code example | 5,653 | 7,227.1 | 4,245.3 | 608 | 264 |
| Nested lists/quotes | 95 | 1,732.6 | 1,129.9 | 1,336 | 912 |
| 32 lines of links/entities | 1,632 | 14,991.8 | 9,603.9 | 15,664 | 11,568 |

The geometric-mean tuned/DM4 ratio is **0.669 for time** and **0.663 for allocated
bytes** (33.1% and 33.7% reductions). The plain summary is within noise (+6 ns, its
fast path now also accepts multi-line plain paragraphs) and allocates 24 B less.
Against Markdig in the same run, the six ratios have geometric means of **0.395
(time)** and **0.170 (bytes)**; the plain-summary first call in a fresh process
takes 3.0 ms versus 39.3 ms.

Correctness change: a bare link destination may contain `<` after its first
character (`[x](a<b)`, `[x](a(b<c)d "t<u")`), as in CommonMark 0.31.2, cmark and
Markdig; the parser previously stopped the destination at `<` and produced text.
Regression cases were added to the parser tests and the Markdig comparison
generator (`"a<b"`, `"a(b<c)d"`, `"<a<b>"`). No other syntax result changed:
the 372 retained conformance examples, the Markdig comparisons and all
differences still pass.

Measured implementation changes, in the order they were profiled with a
SuspendThread-based sampler (Markdig-independent, single-threaded parse loop):

1. `SearchValues` needles containing NUL select a slower vectorized searcher.
   The inline marker set no longer contains NUL; a NUL-inclusive set is used only
   when the document contains NUL (found by the same scan that rejects CR).
   Per-node NUL scans were removed: plain runs stop at NUL by construction, so only
   code spans are scanned. This was the largest time reduction.
2. The parser is a stack-only `ref struct` with caller-supplied `stackalloc`
   scratch (64 nodes, 16 lines, 16 delimiters, …). No parser object and, for
   typical comments, no `ArrayPool` traffic. Retained nodes shrink from 44 to 36
   bytes: sibling/tail links and depth information live in a parallel parser-only
   buffer, and freezing is a single copy.
3. Adjacent plain text runs extend the previous text node in place (delimiter and
   bracket nodes stay separate). Unequal backtick runs at 33,408 characters now
   allocate 192 B instead of 39,000 B; failed links/titles allocate 59% less.
4. Link destinations and titles without escapes or references are source slices
   materialized on first `Destination`/`Title` use; title-less links add no empty
   table entry. Multi-line paragraphs whose lines are contiguous in the source are
   parsed in place; others are joined once and positions are mapped per line
   rather than per character.
5. The exact tree depth is tracked while parsing (container depth, inline wrapper
   heights), replacing the final full-tree walk. Parenthesis matching for link
   destinations jumps between the few relevant characters and the destination
   stop is found lazily; fenced-code lines that cannot close the fence skip the
   indentation scan; `ConsumeWhitespace`, `ReadListMarker` and the fence-language
   word avoid repeated scans and intermediate strings.
6. `ClassifyItems` counts matches by scanning up to 16 parameters instead of
   building a dictionary; standard item names are shared literals;
   `DocumentationComment.GetText` and `Format` fill exact arrays and strings once.

Other final-run observations: cached summary/candidate queries and 1,024-range
mapping still allocate 0 B; `Parse + extract` at 8/128/2,048 items costs
1.72/20.5/441 µs and 1,976/27,896/442,616 B (DM4: 3.91/51.2/827 µs); a depth
interruption costs 10.1 µs / 928 B. Largest-two-size growth is 2.98×–4.05× for the
prose, block, failed-link, failed-title, delimiter and bracket families, 3.73× for
quote depth and 2.76× for unequal backtick runs; all remain below the 6× gate.
Dedicated concurrency diagnostics: 2,048-item initial extraction with 1/4/16
simultaneous requests allocates 147,608/147,632/164,968 B (one extraction plus
coordination). Raw data: [tuned run](Results/DocumentationMarkdown/2026-09-20-tuned.json),
[same-process pair](Results/DocumentationMarkdown/2026-09-20-tuned-paired.json),
[diagnostics](Results/DocumentationMarkdown/2026-09-20-tuned-diagnostics.json).
Timing noise on this host is roughly ±10% between runs; the paired figures are
the comparable ones. The sections below record the DM4 state they were written for.

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

<a id="dm5-product-output-and-switch-2026-09-20"></a>

## DM5 product output and switch — 2026-09-20

The product facade now parses, classifies and renders independent syntax. Markdig
1.3.2 is a private comparison dependency of `xUnitTest` and `Benchmark` only.
Compiler assembly references, restored assets, dependency manifests and a fresh
managed publish contain no Markdig; the published compiler's `--help` exits 0.

Output measurements use the DM4 harness conditions: Release .NET 10.0.12, Windows
x64/i7-1280P, `DOTNET_TieredCompilation=0`, process affinity to one logical CPU,
50 ms warmup, calibrated batches targeting 20 ms, and seven alternating samples.
The six familiar inputs replace relative link destinations with root-relative
ones so both engines generate identical HTML. Heading offset is 0; raw HTML is
disabled; Markdig retains precise source locations. Setup verifies output equality
apart from the documented optional `<li>` formatting newline. Input creation and
cached-tree parsing are outside `Render`; `Parse+Render` includes both operations.
Markdig's renderer does not perform the independent renderer's URL validation and
cancellation checks; callbacks and deployment-specific mapping are not timed.

| Workload | Parse + render independent / Markdig ns | Independent / Markdig B | Render only independent / Markdig ns |
| --- | ---: | ---: | ---: |
| Summary | 187.7 / 645.9 | 360 / 736 | 133.3 / 106.3 |
| Rich summary | 1,747.3 / 2,219.4 | 880 / 2,128 | 534.3 / 389.1 |
| Parameters | 4,935.9 / 9,787.4 | 3,224 / 8,352 | 2,144.2 / 1,493.1 |
| Code example | 7,524.0 / 27,109.4 | 11,680 / 32,456 | 896.8 / 4,402.3 |
| Nested | 2,527.0 / 5,590.6 | 1,328 / 4,360 | 898.8 / 712.9 |
| Links/entities | 29,474.9 / 36,812.3 | 18,584 / 42,792 | 13,992.5 / 9,701.7 |

Medians are per operation. Equal-weight geometric means for parse + render are
**0.476 of Markdig time and 0.394 of its allocated bytes** (52.4% and 60.6% lower).
Render-only allocation equals the returned HTML string size in all six cases:
168 / 272 / 1,272 / 11,416 / 416 / 4,712 B respectively, matching Markdig. Rendering
alone is slower in five cases; this is not a claim of universal renderer speedup.
The large contiguous code slice benefits from vectorized escaping.

The initial output implementation allocated new StringBuilders and repeated URL
validation. Measurement led to a bounded per-thread builder cache, vectorized
escaping, skipping percent-decoding work when no percent escapes exist, and final
revalidation only for rewritten/mapped output. URL input checks and rejection
behavior remain covered by regression tests. Reentrant callbacks temporarily
remove the cached builder from the thread slot; cancellation returns or releases
scratch without publishing partial HTML.

Repeated list/link rendering at 256 / 1,024 / 4,096 / 16,384 lines costs 0.096 /
0.433 / 2.013 / 6.600 ms, allocating 33,840 / 280,320 / 1,087,624 / 4,348,984 B.
Successive 4× input steps cost 4.51× / 4.65× / 3.28× time, satisfying the <6×
investigative gate. Outputs larger than the 65,536-character builder retention
limit allocate fresh backing storage; Markdig allocates less on these stress cases
because its retention strategy differs. This bounds retained per-thread scratch
instead of retaining the largest document ever rendered. These finite inputs do
not prove complexity for every document. The traversal itself is iterative and
visits each node a bounded number of times; source-path normalization scans
segments once, without repeated whole-document searches.

[Raw samples](Results/DocumentationMarkdown/2026-09-20-product-output.json) record
runtime, time/allocation samples and product hash:

```text
E1973176A1A2B05E405F9B29242C62B01BD8DEE69D02549C53B7725F6321C29E
```

Reproduce after a Release build:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet Benchmark/bin/Release/net10.0/Benchmark.dll --documentation-markdown output bin/documentation-markdown/dm5-output.json
```

The switch retains all prior conformance/difference tests, adds 320 exact official
non-link product-output cases and explicit structured-URL/facade/concurrency tests,
and migrates obsolete product expectations to the adopted limited profile.
[API changes and placement rules](../Kimi/Compiler/Documentation/README.md) are
explicit: no Markdig public types, lowercase standard items, current Binding roles,
root-relative default source placement, and configurable structured mapping.
NativeAOT and native fixture execution were not run in DM5; managed integration
checks collection-on/off emitted IR after invoking the product output path.
