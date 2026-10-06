# Hover implementation and verification

[SPEC §23.4.11](../spec/lsp-hover.md) defines observable behavior. These notes record implementation policy and evidence;
they do not change language guarantees or mark pending work as supported.

## Ownership and reuse

- Checks opt into collection before parsing. Embedded tokens and comment candidates are immutable; declaration association
  belongs to each compilation. CLI checks leave collection disabled.
- Optional collection failures are recorded against their source without changing parsing or diagnostics. Shared static
  delegates wrap documentation-only mutations; span-based association and lexical creation have the same failure filter.
  Pending-input/cancellation exceptions propagate. Embedded candidate failures never publish a completed empty cache.
- `Binding.Hover` builds detached own-source token indexes. Headers, comment inputs and complete Type identities are shared
  within a projection. Structural identities form a DAG, avoiding expansion of repeated Type/Origin subgraphs.
- Copy reuses the existing judgment in the target's scope. Effect descriptions obtain provenance through existing
  availability checks; ordinary checks pass no collector and allocate no provenance list.
- Function and Property syntax writers also write body-free headers. Verified accessor metadata supplements implicit
  operations. A missing named-function result means Unit; it is not inferred from the body.
- Documentation inputs retain immutable partial placement maps. Editor links reuse profile validation and logical
  resolution, then encode physical path data separately from URL query/fragment. External mappings establish no neighbor
  placement; conflicting mappings and embedded sources have no physical destination. Resolution performs no I/O.
- `LspSession.Hover` refreshes eligibility on state events, independently of diagnostic publication. Source selection
  includes unfinished required units and excludes dependency consumers and Product units for TestSources-only files.
- Each open document retains one `HoverState`: detached participant indexes and result IDs, bounded edit history and
  cached agreement/rendering. It retains no unit result or unrelated source index. `HoverAgreement` compares identities
  structurally with reference-pair DAG memoization, including hidden closure substitutions, documentation and placement.
  Session disposal releases input/result indexes even if the disposed session itself remains referenced.
- Repeated lookup maps positions and reads the cached answer; it does not recompare comments/effects or render again.
  Cache saturation clears old entries before retaining the requested answer. Historical body prefixes are cached separately
  from current ranges. Full replacement, lost synchronization and configuration/membership changes discard history.
- `HoverRenderer` serializes the documentation profile's tree to Markdown or plaintext. Completed blocks are staged
  before publication; contract fields remain indivisible. Unsupported syntax is escaped, and links use source placement.

## Deterministic limits

`HoverLimits` is the single policy location. Values count UTF-16 code units where applicable.

| Resource | Initial limit | Enforcement |
| --- | ---: | --- |
| Recursive projection / documentation depth | 64 | Implemented |
| Work per recursive formatting or request operation | 1,048,576 | Implemented |
| Total projection identity/index work | 4 × work limit | Implemented |
| Collected representative anchors | 1,048,576 | Overflow disables the optional index, preserving the check |
| One header/call input and rendered declaration | 65,536 | Implemented; executable bodies excluded from header traversal |
| Documentation input and response output | 65,536 | Implemented |
| Cached targets per document | 1,024 | Implemented; includes absent/different/limited targets |
| Cached UTF-16 characters per document | 1,048,576 | Implemented; includes previous prefixes and retained reasons |
| Accepted incremental edits retained | 256 | Implemented; overflow discards the set |

Limits are deterministic interruptions, never partial proofs. Projection failures use the optional CheckService boundary;
they preserve finalized diagnostics and outcomes. Documentation-only collection/projection failures retain other fields;
the ordinary documentation publisher keeps its original failure contract.
The measurements below characterize these limits on one host; the constants alone guarantee no response time.
Later tuning must retain the limit regression cases.

## Verification

Focused tests cover token positions, shared headers, associated Types and split declarations, contextual Copy, private and
embedded documentation inputs, generated constructors, effect premises, implicit accessor/result contracts, recursive
formatting limits, compiler-graph release and allocation-free binary lookup. Related effect functional and allocation
regressions must remain unchanged when the optional evidence collector is absent.
`HoverLinkTest` covers admitted schemes, unsupported destinations, root escape, one-time UTF-8 decoding, literal percent/hash
file names, URL suffixes, exact external placement and dependency-local documentation bases.

`HoverRenderingTest` checks independent CommonMark interpretation, plaintext, parameter-item selection, code/newline
preservation, nested structures, unavailable syntax, interrupted documentation and indivisible mandatory fields.

`LspHoverTest` checks real compiler-to-protocol output and shared quiet-period updates with empty diagnostics.
`LspHoverAdoptionTest` controls adoption/revalidation and unfinished units independently of diagnostic contributors.
`HoverStateTest` checks strict agreement, DAG sharing, continuity, fixed storage bounds and zero-allocation repeated lookup.

## Responsiveness measurements

Run `dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --hover` on an otherwise idle local machine. Workloads shared
with regressions include short and long comments, the maximum admitted comment, 60,000 effect characters, a shared
identity DAG and a wide identity tree, each across four independent configurations. Fixed conditions: 32 warm-ups,
seven samples, 64 initial requests or 10,000 repeated requests per sample; history uses all 256 admitted edits.
The full compilation comparison uses 16 iterations per sample with collection disabled/enabled, including ownership.

Review criteria on the measured host: warmed initial-target samples at most 100 ms/request, repeated lookup at most
1 ms/request with zero lookup allocations, and an edit queued behind 100 cached requests applied within 250 ms in every
sample. Report the before-warm-up response separately. These are investigation thresholds, not language guarantees or
timing assertions in the functional suite. A failed criterion requires profiling and repair or an explicit unresolved limit.

The queued-burst workload runs the actual state owner in FIFO order, including response construction/enqueue and the
following edit. Request parsing, transport and asynchronous serialization/drain are outside that latency; allocation counts
include the sender through its drain. Results do not measure editor text-entry latency or a remote client's display time.

### Results (2026-10-07)

Release, .NET 10.0.12, Windows 10.0.26300, 32 logical processors, default runtime/JIT settings; no concurrent build/test.
Raw samples: `artifacts/benchmarks/hover-20261007/independent-inputs.json`. Each configuration owns independent comment
storage. Values below are the largest per-operation averages among seven samples, not maximum individual latency.

| Workload | Initial agreement/render, ms | Initial allocation, B/op | Repeated lookup allocation, B/op |
| --- | ---: | ---: | ---: |
| Short comment | 0.013 | 4,488 | 0 |
| 600 Markdown paragraphs | 2.910 | 900,593 | 0 |
| Maximum comment (65,536 input units) | 0.204 | 280,080 | 0 |
| 60,000 effect characters | 0.084 | 380,339 | 0 |
| Shared identity DAG, depth 40 | 0.006 | 14,216 | 0 |
| 32,767-node identity tree per configuration | 3.512 | 8,457,698 | 0 |

Every repeated sample is below 0.00025 ms/request; mapping all 256 historical edits stays below 0.0051 ms/request,
also at zero lookup allocation. The maximum comment returns a completed-block truncation notice rather than partial text.
The first process response before warm-up takes 21.821 ms. An edit behind one initial Hover takes at most 3.515 ms;
behind 100 cached requests it takes at most 0.097 ms. All predeclared criteria pass. Initial allocation is intentionally
reported separately: very wide identities still need a large temporary comparison set; bounded caches avoid repeating it.

The small compiler workload's median allocation is 4,272,664 B/check without collection and 4,661,250 B/check with it
(about 9.1% additional allocation). Timing ranges overlap (12.113–25.731 and 11.252–17.329 ms/check respectively);
the fixed execution order and JIT variation do not establish a speedup. CLI checks continue to leave collection disabled.

### Client evidence

VS Code 1.140.0, the managed Release server, and isolated extension test profiles verify the returned token range,
`kimi` Markdown fence, Copy field, untrusted Markdown and removal of command links. A mapped `guide #%.md#intro`
destination keeps its fragment and opens the intended file through `vscode.open`; physical mouse-click behavior and
heading scrolling are not asserted. The LSP integration suite passes 13 tests, command/selection suites 21/14, and extension
unit tests 65 with two platform skips. The initial link assertion assumed an uppercase drive letter; the corrected assertion
uses VS Code's own file-URI normalization. The passing LSP log is `artifacts/verify/hover-client-20261007/lsp.log`.
Final whole-solution verification and commit association are recorded in PLAN_HISTORY and the Verify evidence.
