# LSP review and hot-path measurements

The 2026-09-28 review covers `Kimi/Lsp`. Required behavior remains SPEC Chapter 23;
no language rule or protocol capability changed.

## Correctness

- Frame limits now cover the entire header section. Truncated, missing, duplicate
  and malformed content lengths are rejected. Invalid JSON-RPC objects are
  distinguished from malformed JSON, following the separate error categories in
  [JSON-RPC 2.0](https://www.jsonrpc.org/specification#error_object). Null change records cannot crash the session,
  and lifecycle requests and notifications preserve their message roles.
- Output closure releases queued messages and flush waiters. Every exit path,
  including external cancellation, cancels the worker and returns document buffers.
  Watch registration runs once. Invalid selected paths are logged.
- Reversed edit ranges are rejected before character clamping. Desynchronized
  overlays remain unestablished across unrelated checks and disk changes until a
  full-text event resynchronizes them.
- Dependency expansion visits already-loaded candidates and uses an explicit
  stack. Unreadable inputs and pending listings do not establish absent membership.
  Input exceptions translated by compiler boundaries still prevent adoption.
- A discarded product result cannot retire a test unit. Registration copies
  first-read records instead of modifying arrays still visible to the worker.
- Overlay listings deduplicate by platform file identity, preserving disk spelling
  on Windows. Shared diagnostics retain sorting, deduplication and retirement behavior.

The verified implementation commits are `6e93719b`, `91aa2a5b` and `40d0e51a`.

## Allocation measurements

Windows x64, .NET 10.0.12. The Debug comparison uses the listing/diagnostic code at
`91aa2a5b` and the final code at `40d0e51a`, with the same workloads and default JIT
settings. The measurement harness was added during this review. Results are medians
of five samples after 2,000 warmup operations; each sample performs 20,000 edit or
diagnostic operations, or 5,000 listing operations.

| Operation | Before, B/op | After, B/op |
| --- | ---: | ---: |
| Deduplicate 200 diagnostics into 100 | 1,680 | 824 |
| Merge 1,000 disk names with one existing overlay | 64,528 | 0 |
| Merge 1,000 disk names with one new overlay | 64,584 | 8,192 |

The diagnostic path compacts a span and allocates only the final array. Publication
reuses a single contributor's immutable array and a session-local scratch list for
multiple contributors. Listing merges reuse retained arrays when possible and copy
only when new names are added; directory grouping avoids scanning unrelated open
documents. First-read storage is worker-owned and needs no concurrent dictionary.

The Debug listing samples changed from 44.8 to 0.157 microseconds for the existing
overlay and from 45.7 to 11.4 microseconds for the new overlay. Diagnostic timing in
Debug did not improve consistently, so the allocation reduction is the supported
claim for that configuration. Raw results:
[before](Results/Lsp/2026-09-28-before-debug.json),
[after](Results/Lsp/2026-09-28-after-debug.json).

## Release observations

The final Release binary was measured separately with tiered compilation disabled.
The diagnostic benchmark includes the former implementation as a paired reference.

| Operation | ns/op | B/op |
| --- | ---: | ---: |
| Same-length character replacement, 105 KB document | 32.8 | 0 |
| Unchanged replacement, 105 KB document | 7.5 | 0 |
| Deduplicate 200 diagnostics, former implementation | 1,416.7 | 1,680 |
| Deduplicate 200 diagnostics, current implementation | 1,316.7 | 824 |
| Merge 1,000 names with an existing overlay | 139.2 | 0 |
| Merge 1,000 names with a new overlay | 8,180.1 | 8,192 |

[Raw Release results](Results/Lsp/2026-09-28-after-release.json).

These are operation costs, not whole-server response times. The listing samples
exclude disk I/O and snapshot construction; the edit samples exclude JSON parsing
and text snapshot creation. Insertions and deletions can still move the document
tail and following line offsets. New Windows paths may require a case-insensitive
scan of retained disk names. Timing varies by machine and workload.

To reproduce after a verified Release build:

```powershell
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    dotnet Benchmark/bin/Release/net10.0/Benchmark.dll --lsp
}
finally { $env:DOTNET_TieredCompilation = $previousTiering }
```

## Verification

The final focused run passed 82 tests, including 38 added regression cases:
`bin/verify/20260927-235300-085-unit-lsp-review-final`.

Session evidence is in `bin/verify/20260927-235430-539-session-lsp-review`, at clean
implementation HEAD `40d0e51a`. Both non-incremental Debug/Release builds passed with
warnings treated as errors. Each full suite ran 13,269 tests: 13,265 passed and the
four `TestProcessRecoveryTest` cases failed before child startup under the sandbox.
Those same built test assemblies passed all four cases outside the sandbox in both
configurations (`recovery-Debug.log`, `recovery-Release.log` and
`recovery-summary.json`). The original session failure records are preserved.

No NativeAOT run, compiler generation change, draft edit or milestone change was made.
