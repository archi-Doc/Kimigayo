# Hover implementation and verification

[SPEC §23.4.11](../spec/lsp-hover.md) defines observable behavior. These notes record implementation policy and evidence;
they do not change language guarantees or mark pending work as supported.

## Ownership and reuse

- Checks opt into collection before parsing. Embedded tokens and comment candidates are immutable; declaration association
  belongs to each compilation. CLI checks leave collection disabled.
- Optional collection failures are recorded against their source without changing parsing or diagnostics. Shared static
  delegates wrap documentation-only mutations; span-based association and lexical creation have the same failure filter.
  Pending-input/cancellation exceptions propagate. Embedded candidate failures never publish a completed empty cache.
- `HoverBuilder` (`src/Kimi/Checking/Hover`) builds detached own-source token indexes and reads Binding only through
  `SemanticQuery`. Headers, comment inputs and complete Type identities are shared within a projection. Structural
  identities form a DAG, avoiding expansion of repeated Type/Origin subgraphs.
- Copy reuses the existing judgment in the target's scope. Effect descriptions obtain provenance through existing
  availability checks; ordinary checks pass no collector and allocate no provenance list.
- `HoverBuilder.Variables` indexes established local and ordinary parameter bindings, sharing descriptions by binding,
  complete Type and contextual Copy result. Callable uses add variable facts to the existing call contract/effect facts.
  Complete generic Types and Semantics/target pairs retain their declarations and constraints without a guessed owner/Core.
  `HoverExplanations` owns common Semantics and operation wording; operation descriptions are shared by parsed form.
- Parser anchors distinguish the `@` token, operation name and explicit Type arguments, even when an operand fails Binding.
  Only established syntax selects a general operation description; it is independent of use-site legality.
- Virtual declarations retain their original public contract; overrides additionally display their own header and comments.
  Calls distinguish a dynamic slot from the selected direct base implementation. Detached identities retain the bound
  declaring Type, base lookup and implementation, including virtual Function Items. Receiver and erasure details read
  checked operations; projection never creates a generation context. Binding and ownership completion remain distinct.
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
- Containers use inline code and declarations use normal-size code blocks. Comment heading levels are preserved.
  Parameter items use the existing classified documentation path once, then move immediately after the variable header.
  Known physical source names are computed once per module source using its own project base, independently of comments;
  embedded/generated/unknown mappings invent no physical path. Stable attribution accompanies a reserved source footer,
  including after documentation truncation. Agreement includes these names and variable facts.

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

### Hover improvements verification boundary (2026-10-08)

`HoverImprovementsTest` adds declaration/reference, shadowing, renamed-parameter items, generic/nested/structural Types,
Callable effects, operation token/error/non-code cases, split sources, footer truncation, agreement and cached allocation
regressions. `LspHoverTest` adds real variable/operation responses in Markdown and plaintext. Runtime Contract Views remain
deferred language forms; their rejection does not justify inventing established variable facts or weakening the Hover spec.

Automated verification is complete: Session `20261007-183827-283-session-virtual-remaining-session-repaired` passed all 19,318 functional/allocation tests, including the repaired Hover regressions, with a warning-free whole-solution Release build and stable inputs. [PLAN_HISTORY](PLAN_HISTORY.md#hover-verification) retains the earlier Smart App Control failures, the user's execution waiver and the later superseding verification.

The user owns manual VS Code inspection of font size, wrapping, headings and source readability; agents must not automate or repeat it. Its result awaits the user's report and is not an outstanding agent task. No older client or automated test result certifies the new presentation.

## Responsiveness measurements

Run `dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --hover` on an otherwise idle local machine. Workloads shared
with regressions include short and long comments, the maximum admitted comment, 60,000 effect characters, a shared
identity DAG and a wide identity tree, each across four independent configurations. Fixed conditions: 32 warm-ups,
seven samples, 64 initial requests or 10,000 repeated requests per sample; history uses all 256 admitted edits.
Variable/parameter and operation workloads additionally use four independent compiler snapshots of `ValueProgram`, at
their actual token positions. Cached and queued requests use those same positions and immutable source text.
The full compilation comparison uses 16 iterations per sample with collection disabled/enabled, including ownership.

Review criteria on the measured host: warmed initial-target samples at most 100 ms/request, repeated lookup at most
1 ms/request with zero lookup allocations, and an edit queued behind 100 cached requests applied within 250 ms in every
sample. Report the before-warm-up response separately. These are investigation thresholds, not language guarantees or
timing assertions in the functional suite. A failed criterion requires profiling and repair or an explicit unresolved limit.

The queued-burst workload runs the actual state owner in FIFO order, including response construction/enqueue and the
following edit. Request parsing, transport and asynchronous serialization/drain are outside that latency; allocation counts
include the sender through its drain. Results do not measure editor text-entry latency or a remote client's display time.

### Results (2026-10-07)

Raw samples: `artifacts/benchmarks/hover-20261007/independent-inputs.json` (Release, .NET 10.0.12, Windows 10.0.26300, 32 reported logical processors). All predeclared thresholds passed. Largest initial sample average: 3.512 ms/request for a wide identity tree, with 8,457,698 B/op; repeated lookup and maximum-history mapping allocated zero. These are sample averages, not maximum individual latency. Initial wide-identity comparison storage remains a cost despite bounded caches.

Collection added about 9.1% allocation on the small compiler workload; timing ranges overlapped and established no speedup. CLI checks leave collection disabled. The [archived full table](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/HOVER.md#results-2026-10-07) preserves each workload, host conditions and before-warm-up observations.

### Client evidence

The original VS Code integration verified token ranges, kimi Markdown fences, Copy, untrusted Markdown, command-link removal and a mapped destination through vscode.open. Physical mouse-click behavior and heading scrolling were not asserted. [PLAN_HISTORY](PLAN_HISTORY.md#hover-verification) retains the exact suites, platform skips, corrected drive-letter assumption and log location. This evidence does not replace the user-owned display review of the later improvements.

### Measurements (2026-10-08)

Raw samples: `artifacts/benchmarks/hover-improvements-20261008.json`, following the final warning-free non-incremental whole-solution Release build, on .NET 10.0.12 / Windows 10.0.26300 with 8 reported logical processors and no concurrent build/test. The unchanged conditions above ran all nine workloads; parameter/local/operation initial allocations were 7,152/3,232/2,528 B/op, with zero current/history cached-lookup allocation.

Every sample met the investigation thresholds. The largest initial sample average was 6.81 ms/request (wide identity); the first process response took 30.10 ms. Host/JIT differences and fixed execution order establish no speedup over the earlier run. Wide initial identities still allocate a large temporary comparison set. [Archived results](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/HOVER.md#measurements-2026-10-08).
