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
  structurally with reference-pair DAG memoization, including documentation facts and physical placement.
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
No responsiveness claim follows from these constants: initial/maximal inputs, continuous requests and queued edit latency
must be measured before the session implementation is complete. Later tuning must retain the limit regression cases.

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

Pending: client navigation, throughput/latency measurements for requests,
and final whole-solution Session verification. Record measurement
conditions and evidence paths here when these units are verified.
