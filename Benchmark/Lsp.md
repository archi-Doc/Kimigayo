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
and text snapshot creation. Insertions and deletions still moved the document tail
and following line offsets; the follow-up review below removes that cost. New Windows
paths may require a case-insensitive scan of retained disk names. Timing varies by
machine and workload.

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

## Follow-up review

A second pass over `Kimi/Lsp` on the same day. Required behavior remains SPEC Chapter 23;
no language rule or advertised capability changed.

### Correctness

- Document text holding an escaped lone surrogate was rejected as invalid params, so
  the server dropped a change that the client had applied. Such text is now read, and
  the check reports `InvalidSourceEncoding_Kd` as §23.3.4 requires.
- A string request ID holding an escaped lone surrogate made the sender throw while
  echoing it; the pump stopped and no later frame was written. Such an ID is now an
  invalid request (-32600), and a value that cannot be serialized drops only its frame.
- An unreadable change entry (null, or without text) dropped the whole notification
  and left the server text silently stale. The entries before it still apply, and the
  document is desynchronized (§23.4.2) until a full-text event.
- An unexpected input failure left the session waiting forever; the receive loop now
  always ends the input.

The implementation commits are `01b24e96`, `4c35980c`, `f5071b2f` and `9606df50`; the
harness additions are `04a27cb3`.

### Measurements

Release, tiered compilation disabled, medians of five samples as above. Before is
`aba90c68` with the same harness adapted to its API; after is `9606df50`.

| Operation | Before, ns/op | After, ns/op | Before, B/op | After, B/op |
| --- | ---: | ---: | ---: | ---: |
| One-character `didChange` near the top of a 105 KB document, parse and state owner | 45,785 | 2,422 | 2,820 | 1,468 |
| Insertion or deletion near the top of the 105 KB document | 20,940 | 19 | 0 | 0 |
| Repeated `didOpen` of the 105 KB document | 891,280 | 449,782 | 336,951 | 221,376 |
| One diagnostics frame through the sender, all threads | 2,467 | 2,579 | 176 | 40 |

- Open documents are gap buffers. An edit costs its size plus its distance from the
  previous edit, and the line starts after the gap are kept as distances from the end,
  so typing at one place moves neither characters nor line offsets.
- The receive loop reads each body once and deserializes a known method's parameters
  straight from the frame; the former path copied them into a `JsonElement` and parsed
  them again. An edit finds its document by the URI it was opened with, without
  parsing the URI. The reopen still allocates the 210 KB text string it retains.
- Output messages are queued as values, each frame is one write, and the output is
  flushed when the queue runs empty. The remaining 40 B per frame is the harness's own
  parameter object.
- Each check copies the committed snapshot into a dictionary instead of persistent
  immutable collections, and skips hashing inputs while nothing changed after the base.
  These per-check costs were not measured separately.

Raw results: [before](Results/Lsp/2026-09-28-followup-before-release.json),
[after](Results/Lsp/2026-09-28-followup-after-release.json). The same exclusions apply:
these are operation costs, not whole-editor latency.

### Follow-up verification

Each unit was verified with the focused LSP classes; the regression tests for the four
defects fail on the code before the fix. The 93 LSP tests include 12 added in this
review. Session evidence is in `bin/verify/20260928-031855-454-session-lsp-followup`
at `04a27cb3`: non-incremental Debug and Release builds with warnings treated as
errors, and 12,908 tests passing in each configuration. No NativeAOT run, compiler
generation change, draft edit or milestone change was made.
