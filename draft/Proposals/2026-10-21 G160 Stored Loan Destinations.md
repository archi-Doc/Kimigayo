# G160: Stored Loan Destinations

Date: 2026-10-21. Scope: a design and implementation plan, not an implementation.
Source baseline: `e307e9f92414f9c57461f14efbdc05ad07058791`.

Status: plan B selected by the planning session; independent plan review passed on 2026-10-21. The proposed language
contract in §5 is not current SPEC. No specification, compiler, library or supported boundary changes
with this proposal. R4 remains paused; implementing this plan requires its resumption and the semantic
adoption gate in U1. G160 remains open until both public acceptance and MIR analysis pass the exit checks.

## 1. Required meaning and evidence

A value stored through a raw pointer retains its Origins and Loan requirements. The destination,
not the presence of a Loan in the pointer value, decides where those requirements survive.
SPEC §15.6 treats raw storage as storage the body does not own; values stored there may be used
after every point. Calls must account for every destination their instantiated public contract
permits, independently of the callee implementation.

The design chain is:

> SPEC §5.2.1 and §15.6–15.6.4 require complete storage retention, including raw access after a
> shared layer → retention currently loses destination structure, and `MirWrites` cannot express
> independent safe and raw routes → Binding publishes one storage contract through `TyCtxt`,
> preserving its destinations; both analyses map those destinations to their own holders →
> remove MIR's second decision about which storage is writable and the legacy raw-store omission.

Authoritative references:

- [Raw access and retained dependencies](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#521-access-conditions).
- [Loan markers](../../docs/spec/15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins).
- [Holders and storage](../../docs/spec/15-ownership-and-lifetime-analysis.md#156-borrow-checking),
  [shared access](../../docs/spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)
  and [call contracts](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls).
- [G160 and G148](../../docs/dev/PLAN.md), [R4-M73 and R4-M76](../../docs/dev/DECISIONS.md),
  [architecture and migration freeze](../../docs/dev/COMPILER_ARCHITECTURE.md).

### 1.1 Reproducer matrix

These are reduced shapes, not complete executable programs. U0 must supply initialization,
declarations, unsafe blocks and a safe observation of the saved reference, then record exact results.
`x` is the borrowed local. The conflict is the later `x = 5`.

| ID | Store shape | Existing evidence and required result |
| --- | --- | --- |
| A | `RawBox {a}` contains `p: raw/(ref/i32 during a)` and `r: ref/i32 during a`; `storeRaw(b@uniq, x@ref)` writes `b.p[0] = value` | PLAN records acceptance. Reject the later conflict even when `b` is dead. |
| A-direct | `q.p[0] = x@ref` | MIR direct-store control rejects; legacy acceptance is recorded. |
| A-raw | `putRaw(c@uniq, x@ref)`, with `c: raw/(ref/i32)` | MIR raw-only control rejects; keep this working. |
| B | `putRR(c@uniq, x@ref)`, with `c: raw/(raw/(ref/i32))`, writes through both raw layers | Historical review records the miss. Raw chain length must not decide acceptance. |
| C | `r = c@ref`; `putRS(r@uniq, x@ref)` reads the raw pointer through `r`, then writes through it | Historical review records the miss when `r` is dead. Shared access to the pointer does not remove its independent authority. |
| C-live | C followed by `inspect(r)` | Historical MIR control already rejects through the live holder. This is not another accepted case. |
| D | `Raw.initialize(p, x@ref); x = 2`, followed by reading the stored safe reference | Tracked MIR regression rejects; STATUS and historical review record legacy acceptance. |
| I | Iterate a local `Array<ref/Data>`, then write `a.value = 3` after the views and their items cease being used | PLAN records three false errors from indiscriminate raw-to-E widening. Must remain accepted. |

The historical final review is
`artifacts/handoff/2026-10-19-r4-pause/r4-notes/rv-g148f/review.md`, N2 and check 7,
covering `b335393b` and `0e96cf19`. Its referenced probe sources/output are absent from that archived
directory. Treat its results as historical reviewer reports, not fresh measurements or reproducible
evidence at this baseline. The tracked
[raw intrinsic regression](../../tests/xUnitTest/Tests/MirBorrowsTest.cs) and
[A5 prototype](../Sketches/A5%20Step%200/A5RulesPrototype.cs), `RawHidden`, provide additional
source evidence; the latter is a model, not the production compiler.

This planning unit reads code and reviews documentation only. It does not run builds or tests.

## 2. Current control flow and responsibility

| Owner and source | Inputs → output | Relevant invariant or loss |
| --- | --- | --- |
| [TyCtxt.Retention](../../src/Kimi/Compiler/Binding/TyCtxt.Retention.cs), `Retains` | Callee/signature, substitution and premise scope → cached `CallRetention` | Uses public contracts, never solved caller Origins or callee bodies. |
| [Binding.Retention](../../src/Kimi/Compiler/Binding/Binding.Retention.cs), `RetentionWalk` | Instantiated Types and Origin premises → atoms, then rows | A raw layer reopens writability after shared access. `RetentionRows` folds writable atoms into output argument `j`, losing destination distinctions. |
| [MirTypes.Holding](../../src/Kimi/Compiler/Mir/MirTypes.Holding.cs), `HoldsLoans` | A local's case Type → holder eligibility and one `MirWrites` value | `Raw` requires no Loan; the raw walk stops at another raw layer; shared layers stop descent. Early completion also hides independent raw facts. |
| [MirBorrows.Calls](../../src/Kimi/Compiler/Mir/Analysis/MirBorrows.Calls.cs), `Call` | Argument values and retention rows → carried Loan joins | Reads arguments, applies Moves, evaluates W once per output, joins stores, then stores result. |
| [MirBorrows.Values](../../src/Kimi/Compiler/Mir/Analysis/MirBorrows.Values.cs), `Writes`, `Write` | Argument Loan set and `MirWrites` → holder targets | W reconstructs destinations from a scalar guard; raw E is added only for an unnamed raw-only local. |
| [MirBorrows.Flow](../../src/Kimi/Compiler/Mir/Analysis/MirBorrows.Flow.cs), `Store` | Actual destination Place and value → holder/E join | Direct raw stores already join E. This explains the call/direct mismatch. |
| [MirBorrows.Solve](../../src/Kimi/Compiler/Mir/Analysis/MirBorrows.Solve.cs), `Run` | Validated MIR and a Semantics view → problems and work counters | Loans/holders → backward liveness → forward carrying fixed point → replay checks. |
| [OwnershipAnalysis.Pointers](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Pointers.cs), `StorePointer` | Prepared address and acquired value → `StorePointer` operation | The stored value is an operand, not a persistent external destination. |
| [OwnershipBody.Borrows](../../src/Kimi/Compiler/Analysis/OwnershipBody.Borrows.cs), `PrepareRetentions` | Operations/call inputs → borrow retentions | Direct pointer stores and writable call inputs require an outer `uniq`; raw destinations are omitted. |
| [OwnershipBody.Updates](../../src/Kimi/Compiler/Analysis/OwnershipBody.Updates.cs) and [BorrowLiveness](../../src/Kimi/Compiler/Analysis/BorrowLiveness.cs) | Updates and uses → content/liveness state | Raw stores have no corresponding safe update; using a source at the store does not keep it live afterwards. |

Under both pipeline flags, [Compilation.Pipeline](../../src/Kimi/Compiler/Core/Compilation.Pipeline.cs)
still runs legacy ownership analysis. MIR A5 problems are shadow results until R6-check.
A MIR-only repair does not repair current public acceptance.

Legacy ownership cannot simply be switched off: its `TemplateBody` serves Binding's effect-bound
and virtual-effect checks and legacy default emission; `AnalyzeInstance` serves concrete generic
generation. Legacy default declarations also run `VerifyPreparedDefault`. Their removal belongs to
R6-check/R6-gen, not this unit.

### 2.1 Root cause

The representation conflates three questions:

1. Does this value carry Loans?
2. Can safe access through it write further storage?
3. Can reading it reveal a raw pointer with independent storage authority?

None implies the others. More fundamentally, the published retention row says only which argument
is an output. It does not retain which storage within or beyond that argument is the output.
The consumer therefore repeats an incomplete storage analysis.

There is a second missing fact: `Loan<T>` expresses a dependency and grants no access. An ordinary
reference or a Loan marker next to a raw field does not prove any correspondence to that field.
Matching Types, Origins, field names or declaration order cannot supply one.

### 2.2 Scope and preserved invariants

The same cause reaches ordinary, value, erased Callable and intrinsic calls; raw stores and
`Raw.initialize`; closures/captures; and default-template retention. A5-F6/F7 and F9 must consume the
same destination contract. F9's temporary raw-input row must disappear when that contract covers it.

Keep per-argument `Value` versus `Content` retention and whole-holder Loan carrying. Do not infer
Loan identity from equal Origins, introduce per-field Loan liveness, recover a Loan from a raw
pointer, or discard a prior Loan after a partial write. E remains live after every point.
Checks record typed facts after convergence; diagnostic text remains the adapter's responsibility.
MIR reads no syntax/HIR and performs no second Origin proof.

## 3. Alternatives

Two agents independently investigated and wrote A and B before comparing their designs. A starts
inside the MIR owner; B changes the published representation. The planning session also examined
the direct patches and safe-view alternative.

| Option | Design and implementation outline | Assessment |
| --- | --- | --- |
| P: widen current predicates | Allow `Raw` alongside Loans, continue through raw chains/shared layers, add E at each matching holder. | Smallest diff; fails I, still duplicates retention semantics, and has no marker correspondence. Reject as a symptom fix. |
| H: raw-to-E with a hold | Use complete raw discovery; ambiguous abstractions report located `Unsupported_Kd`. | Sound containment only if every affected operation is covered. Breaks library availability; cannot close G160. Any emergency hold needs an explicit removal stage. |
| A: MIR per-part graph | Separate `HoldsLoans` from a `MirTypes` graph; evaluate safe/read/raw routes at W. Keep current retention triples. Introduce an explicit storage association before exempting any raw edge. | Coherent owner-local baseline, but Binding and MIR still decide storage reachability separately. Its public-contract prerequisite remains necessary. |
| **B: published storage destinations** | Binding owns a storage graph and explicit raw-storage correspondence; retention rows preserve graph destinations. Both analyses only map routes to holders/E. | Selected: one semantic decision, one contract, deletes reconstruction and covers both pipelines. Larger first unit; expected smaller long-term implementation. |
| C: normalize library storage to safe views | Replace raw-plus-marker fields with `Slice`/`UniqSlice`, then treat remaining bare raw as E. | Useful for a shared contiguous remainder, but not complete: `UniqSlice` itself uses raw-plus-Loan and its ordinary borrows depend on `self`; dictionary byte storage does not express typed key/value authority. Requires a separate splitting/representation redesign. |
| D: one storage capability value | Replace the raw-plus-marker pair with a new safe storage value carrying the pointer and dependency as one Type. | Strong encapsulation, but adds construction, projection and splitting APIs and migrates the entire storage boundary. Compare in U0; no evidence yet that it preserves iterator independence and ABI with fewer changes than B. |
| E: infer pointer provenance | Track addresses, casts, arithmetic, copies and aliases through bodies; export inferred effects. | Most state and proof obligations; does not replace the public contract for erased/foreign calls. Reject as the G160 solution. |

### 3.1 Independent plan A

A1 preserves before cases and controls. A2 adopts a public storage association. A3 separates
holder eligibility from writes in a behavior-preserving commit. A4 replaces `MirWrites` with a
per-part graph, reading shared parts to find independent raw storage but never granting safe writes
through them. A5 makes every call-store path use its evaluator. A6 adds a legacy adapter or a
temporary Unsupported boundary; A7 performs census, scaling and final review.

A deletes the scalar modes and raw-only branch, but keeps Binding's separate writable-atom walk.
Without A2 it is a containment plan, not a complete repair. No measured size or speed advantage
has been established.

### 3.2 Independent plan B and selection

B first defines the missing storage contract, then retains destination identity at publication.
It replaces both the scalar guard and the need for a second semantic walk. A consumer's remaining
work is mapping published storage paths to the body's existing holder sets.

B best fits One Concept, One Canonical Form and Local Reasoning: the declaration states the
storage invariant, the contract decides possible retention, and the consumer does not inspect the
callee body. Its unsafe promise is explicit, rather than guessed from a marker. Its checks and
measurements can be tied to exact source/configuration, as the Compiler Server Protocol requires.

Selection is based on ownership of decisions and expected deletions, not a claimed measured
speedup. If the bounded prototype cannot express the invariants below or requires pointer
provenance state, B returns to design review; it must not grow a second raw-specific fallback.

## 4. Published representation

Extend the existing Binding/TyCtxt retention component; do not create a second authority service.
Responsibility-specific files are `Binding.Storage.cs` and `TyCtxt.Storage.cs`, with entry contracts.
MIR's destination evaluator belongs beside `MirBorrows.Values`; it is not a new Origin solver.

Use a finite graph per declaration with symbolic Type-parameter/substitution edges, not an
enumeration of instantiated access paths. Intern its immutable query results by substitution
and Semantics case. Non-regular `N<(T,T)>` recursion must not expand a tree of concrete paths.
A node states an inline part, safe-borrow target, raw-storage boundary, or declared storage
correspondence. Edges preserve the part identity and read/write capability. These are proposed
vocabulary entries, requiring approval in U1; the proposal grants no architecture exception.

A retention output is either the result or `(input position, storage node)`. Preserve `Input` and
`RetentionKind` unchanged. The storage node determines whether a terminal is the current holder,
holders reached through a declared safe/Loan dependency, or E. Do not encode new output meanings
as undocumented negative integers. Canonicalize equal rows at production, sort by output, and
evaluate each distinct output once per call.

Binding's existing Origin proof determines whether an input can be retained at each destination.
Only then may rows merge by identical destination and input; a local-holder row and an E row
must not merge into a single argument number. `CallRetention.Limit` remains a whole-contract
failure, with no readable partial rows.

### 4.1 Mapping and transfer

| Published route | Consumer action |
| --- | --- |
| Inline value/part | Use its enclosing holder; parts do not acquire separate Loan sets. |
| Safe shared reference | Read its reached holders to discover contents; do not select the referent as a safe write target. |
| Safe exclusive reference | Select its reached holders for permitted stores and continue through their published parts. |
| Bare raw pointer | Select E for stores its pointee contract permits, independently of enclosing holder Loans. Continue discovery of nested raw/safe contents as the contract requires. |
| Declared shared storage | No write to that immediate storage; read its declared content to discover independent authority inside it. |
| Declared exclusive storage | Map to the holders reached by the declaring holder's carried Loans, using the declared dependency route. |
| Declared owned storage | Map to the owning holder under its access capability. |
| Direct exposed raw access or bare raw argument | E; do not recover the enclosing declaration's association from pointer bits or history. |

For a store row, join the retained value into every mapped destination, including E. A declared
storage capability is intersected with the capability by which its owner is accessed: reading a
wrapper through `ref` does not recover its stored marker's exclusive capability. Reading a raw
pointer from that content still starts its separate raw route.
Keep the published route node when mapping to another holder; do not rediscover permissions from
that holder's Type. Splitting destinations never splits the source argument's retained Loan set:
each `Value`/`Content` row still transfers that whole set, even if one Origin atom generated it.
Whole-holder carrying remains SPEC §15.6's union: selecting a declaration part does not select
only that part's Loan IDs. In particular, `via loan` must not filter IDs by an Origin or pretend
that the compiler has a separate Loan set for that field. Operationally the dependency step uses
`refs(σ(h))` and the route's required content closure, for the whole holder `h`. Extra targets and
Loans follow the current SPEC carrying rule; this proposal does not claim field-sensitive
precision. Multiple-marker controls and P13 decide whether the mapping implements that rule,
not whether a discarded field-specific correlation can be guessed.

A raw pointer loaded from declared storage has independent authority. A shared slot containing
`raw/(ref/T)` cannot be overwritten through its shared capability, but reading its pointer and
writing the pointer's pointee still reaches E. The association covers the named storage boundary,
not every raw pointer recursively reachable below it.

Cycles use a worklist of `(storage node, holder, capability)` states and reusable stamps.
Joins are monotone. Cache complete immutable contracts, never a partially explored result.
Regular recursion closes on node identity; growing instantiated Types retain the existing
storage-instantiation limit. The construction key must include any remaining bound that affects
its result, or solve the corresponding recursive component before caching: first-encounter order
must not decide success, Limit, or destination kind (G153). Limit yields located Unsupported,
not invented E routes or an over-conservative Language error.

## 5. Proposed explicit storage contract

This section is a proposed SPEC extension, not a reading that changes `Loan<T>` retroactively.
The selected declaration spelling is:

```text
unsafe storage p of T via loan
unsafe storage p of T via self
```

It is a declaration inside a nominal Type. `p` names one raw-pointer Field; `T` is the complete
Type of the values whose storage/dependencies the declaration describes; `loan` names a particular
`Loan<ref/U>`, `Loan<uniq/U>`, `Loan<objref/U>` or `Loan<objuniq/U>` Field that keeps its dependency.
Binding must prove a typed storage route from `U` to `T` in the published graph. Equality after
normalization is the zero-step route. Longer routes use actual Fields/base/payloads, owned element
storage, safe referents/views and explicitly declared storage domains, composing every capability.
Bare raw jumps, unused Type arguments and marker Fields alone supply no route. Preserve all
matching routes, never the first match or a match by Origin alone. An uninstantiable bounded query
is Unsupported; a complete graph with no matching route makes the declaration ill-formed.
The first form takes its shared/exclusive capability
from that borrow. `self` means the Type owns the described storage and carries its contents'
dependencies. A declaration may describe typed content behind `raw/u8` or `raw/()`.

The descriptor is part of the nominal Type's public semantic contract even when its Fields are
private. Importing modules can read it without inspecting implementation bodies. No declaration
means bare raw storage, not an inferred relationship. Multiple fields require separate, explicit
relationships; an unrelated bare field still reaches E.

For SPEC §5.6's existing `BufferView<T>`, the backing Type is `Loan<ref/Buffer<T>>`, not a
Loan of one element. Declare `Buffer.data of T via self` and `BufferView.data of T via loan`.
The producer's route is `Buffer<T>` → its owned `T` storage; the view keeps the Buffer's Loan
and has shared access. Empty buffers still have that typed storage domain, so no element borrow
is invented. The unsafe promise says the pointer designates the relevant part of that domain;
the compiler proves the Type/capability route, not address identity.

### 5.1 Safety and well-formedness

The declaration establishes a **persistent unsafe representation invariant**:

- The named storage and every alias used to access it obey the declared owner/dependency
  capability for their entire use, including later calls, Moves and destruction. Construction
  alone is insufficient. An allocation's original writability cannot override a later shared
  owner/dependency capability.
- All stored values' Origins and Loan requirements are retained by the stated owner or marker.
  The relation covers the declared content, not merely the pointer's bit representation.
- Bounds, alignment, initialization, provenance, non-overlap and correspondence to the actual
  borrowed/owned storage remain the unsafe implementer's obligations under §5.2.
- A violation, including an alias writing while only the declared shared authority is available,
  is undefined behavior under the proposed contract. The compiler checks declaration shape and
  use of unsafe construction; it does not prove arbitrary pointer aliases.

The raw and evidence fields are non-public `let` Fields, either private or internal to their
declaring module; this admits the existing internal Storage fields without adding access wrappers.
All module code accessing them remains subject to the invariant. Binding verifies unique field
resolution, exactly one relation per raw field, a same-value backing field, the complete set of
normalized typed routes and permitted borrow modes; it rejects competing or dangling relations.
No field-specific Loan IDs enter those declarations.

Establishment and transfer use existing initialization/completeness operations, with these
additional static obligations; no new runtime operation or transactional rollback is proposed:

1. Every initialization or restoration of a participating raw/evidence Field, and every partial
   Move out of one, must occur lexically inside an Unsafe Block. A public safe constructor can
   contain that block. Binding publishes the obligation; the owning initialization analysis
   checks it at the operation. Compiler-supplied constructors have the same published contract.
2. An association becomes available only when the value is complete: at successful constructor
   completion under §6.3, or restoration of all missing parts under §15.1.2. The unsafe writer
   promises that the invariant holds then. Unsafe does not relax ordinary assignment permission
   for a `let` Field.
3. A partial Move makes the source incomplete under the existing Move Path rules, so it cannot
   be passed whole or used through its association until legally restored. Remaining initialized
   raw Fields may still be read; their exposed pointer has only ordinary raw/E treatment. No
   descriptor may make an incomplete source look complete.
4. Whole-value Move transfers an established invariant. Whole-value Copy is permitted only
   when the evidence is Copy and no owned storage is duplicated; `via self` rules out Copy.
   Whole assignment takes the new complete value's invariant. Partial replacement follows
   existing write permissions and must re-establish the relation in unsafe context.
5. Constructors cannot fail normally; Abort performs no cleanup (§6.3.5). Successful construction
   performs the specified cleanup before exposing its result. Outside constructors, ordinary
   partial-value cleanup destroys only initialized parts; a user `drop` still requires complete
   `self`. There is no rollback and no duplicated destruction. Dependencies must survive every
   operation that may observe the raw contents, including cleanup of the old owner.

In particular, migrate `UniqSlice.init(state)` without adding a runtime field: inside one Unsafe
Block assign `self.storage = state.storage + state.position`, assign the length, then
`self.loan = state.loan@move`. The last operation makes `state` incomplete; no subsequent
association-based call on `state` is allowed. Successful constructor completion establishes
`self`'s relation. The source remainder has no user `drop`, so its remaining raw/scalar fields
clean up normally. A transfer from an owner with `drop` must restore completeness before any
ordinary exit, or move that complete owner; unsafe does not waive this rule.

For `via self`, declared content `T` contributes as owned stored content to variance, Loan
requirements, OwnedOrigins, Phantom classification, holder eligibility and destruction observation.
It is not merely a Type argument that the holder walk may ignore. The common storage descriptor
publishes this fact to those owning analyses. For `via loan`, the actual marker continues to
contribute its borrow dependency; the new relation alone creates no Loan. Existing physical-field
Type constraints still apply. Logical content does not add physical fields, automatic element
destruction or a second drop: the owning implementation's explicit destruction remains responsible
for its raw storage, and the analyzer accounts for what it may observe.

This is additional unsafe contractual responsibility, not a new runtime pointer representation.
It never grants a Loan or access merely because a marker exists. Exposing/copying a raw pointer
does not expose a safe proof: direct raw operations and raw argument contracts still use E.
The unsafe invariant continues to constrain aliases even after that static information is lost.

### 5.2 Library and abstraction boundary

Apply explicit shared/exclusive relations to `Storage.RefRemainder`, `UniqRemainder`,
dictionary remainders and `UniqSlice`; inspect owning remainders and formatting buffers too.
Typed dictionary content must be declared despite byte storage. Preserve each public iterator's
split-child Loan and independence contract; no caller may borrow the iterator to keep an already
returned independent item alive.

Built-in collection and Slice storage use their existing SPEC storage contracts to produce the
same nodes. Nominal wrappers compose through their actual Fields, not through Type arguments
treated as stored exclusive references. A Type argument still contributes dependencies according
to SPEC; it does not independently grant access.

An abstraction call can use its nominal storage contract. Extracting the raw Field and performing
the operation directly intentionally loses that static abstraction, just as raw borrowing does
not recover an owner today. No optimizer or source inliner may change the caller's checked
contract by rediscovering pointer provenance.

An undeclared typed store hidden behind a cast from bare `raw/u8` is not magically discoverable
from `u8`. Its unsafe implementation must still satisfy §5.2's retained-dependency contract.
This plan does not claim to verify arbitrary unsafe code or reconstruct hidden content Types.

## 6. Implementation units and gates

Every behavior-changing unit adds a regression that fails before and passes after. Units are
serialized within a worktree, verified and pushed separately. Behavior-preserving restructuring
precedes behavior changes and uses VERIFICATION's diagnostic, semantic and IR comparisons.

| Unit | Work and deletions | Completion condition |
| --- | --- | --- |
| U0: baseline and bounded prototype | Reconstruct §1.1 production probes; preserve exact source, pipeline flags and outputs. Prototype A/B destination mapping on identical inputs under `draft/Sketches`. Census all raw-plus-marker/owned storage and affected contract rows. | Three misses, I, controls and adversarial unrelated-marker cases accounted for; no historical result presented as a new run. Record sizes/hotspots and graph work. |
| U1: semantic adoption | Independently review §5, grammar, visibility, typed backing routes, initialization/completeness lifecycle, maintained alias invariant, stored-content Type properties and diagnostics. Obtain the owner's approval for authoritative SPEC changes and new vocabulary; record the decision. | Integrate affected definitions/examples together, including Buffer/BufferView and partial-transfer controls. If not adopted, implementation is held; neither a marker heuristic nor narrowing SPEC substitutes for it. |
| U2: restructure | Separate retention destination construction from Origin closure and holder eligibility from write classification, behavior unchanged. Split over-limit/responsibility files first. | Required equivalence evidence; no new accepted form or changed diagnostic. |
| U3: publish destinations | Implement declarations and the immutable storage contract in Binding/TyCtxt, including owned logical content for Type-property consumers. Preserve destination identity in retention rows; validate contracts once at their producer. Replace the old writable-atom-to-argument collapse. | `RetentionTest` and declaration tests cover exact rows, recursive/substituted/erased contracts and limits. No consumer reconstructs missing semantics. |
| U4: common MIR transfer and library relations | Replace W reconstruction with graph evaluation; consume existing declaration/intrinsic rows and validated descriptors. Add explicit library relationships and Unsafe Blocks around establishment/partial transfer, including UniqSlice.init; retain internal fields and physical layouts. Delete `MirWrites`, `writable`, `writingKind`, raw-only guards and redundant holding-walk write modes. | §7 matrix passes at MIR-problem layer; I and independent iterator items remain valid. Direct raw Store continues to target E. |
| U5: legacy public safety | Adapt current operations/call inputs to the same published destinations and add persistent external retention. Use existing Loan values/liveness; no second type/authority solver or disabling ownership. Isolate adapter in `OwnershipBody.Retention.cs`. | Public checks reject A–D under both flags. The adapter is removed with old Analysis at R6-check. If required changes exceed a mirrored miscompile fix, obtain an explicit freeze/ratchet exception before implementation. |
| U6: integrate shared consumers | Ordinary/value/erased calls, foreign/intrinsic contracts, defaults, captures and descendant stores use the same output domain. Delete F9's separate raw-store rule when covered. Coordinate the P13 per-part unit; remove its body hold only after its own completion conditions pass. | No raw-feature side table or fallback. Test omitted/explicit recursive-default twins and prepared-slot mutation separately. Recheck held-body old-only ledger rows. |
| U7: closure | Full independent final review; census, ledger and performance comparison; update affected contracts/navigation/support facts. | Both pipelines safe, no unexpected library/milestone regressions, required verification passed, all temporary structures have removal stages; then close G160 before R4 exit. |

U3–U6 form one behavior group: no half-published output contract may merge. All consumers of
a changed row are migrated in its first implementing unit, or use an explicitly recorded staged
adapter with a removal unit; no silent interpretation of old rows. The implementer stops and reports
if the selected representation fails a counterexample.

G148/P13 is related but not automatically closed by G160. Its per-part field guard, independent
Language errors previously held, and empty-value deciding test remain required. R4-M73 is
superseded only where the implementing decision actually replaces its W representation;
R4-M76's whole-holder carrying rule remains. No current DECISIONS text is rewritten.

## 7. Verification and cost

### 7.1 Required matrix

Use existing owning tests: `RetentionTest`, `MirTypesTest`, `MirBorrowsTest`, and the appropriate
public ownership/diagnostic class selected in U0. Library changes add `KimiLibraryShapeTest`.
Do not create duplicate native fixtures for analysis-only outcomes.

- A–D, their direct/raw-only/live-holder twins, raw chains of length 1/2/3, shared→raw→shared→raw.
- Bare pointer plus ordinary reference; unrelated marker; two raw fields with different relations;
  one declared and one bare; same-Origin values with different Loan identities.
- Shared associated storage containing a raw pointer: no overwrite of the slot, but the loaded
  pointer's own store still reaches E. Shared `via self` calls and alias violations are checked
  against the proposed unsafe invariant, not assumed safe from allocation provenance.
- Struct/Tuple/enum wrappers, generic Fields, abstract carriers, pair Semantics cases, regular
  and growing recursive Types; permuting Field/order must not change targets or Limit.
- SPEC's BufferView, including an empty Buffer, with a marker borrowing its container rather
  than an element; multiple matching backing routes with different capabilities, no first-match
  choice; a marker whose referent has no declared route is rejected at the declaration.
- Per-argument Value/Content, callable erasure, fixed Origins, foreign declarations, intrinsic
  extra operands, closures, default evaluation, omitted/explicit `dc-raw-hid` twins.
- Whole/partial replacement, Copy/Move, joins/loops, scope ends and observing destructors.
- UniqSlice's partial marker transfer establishes the destination only at completion; later
  whole-source use is rejected, raw-field reads stay E, and normal/Abort paths follow existing
  cleanup rules. A partial transfer from a Type with `drop` cannot exit incomplete.
- Non-generic `via self` owners whose only logical Loan-bearing content is behind a raw Field:
  the owner is a holder and retains that content; Phantom/OwnedOrigins/variance and destruction
  observation agree without adding runtime drop operations.
- Shared post-iteration writes; multiple live exclusive items across `next`, iterator Moves
  and destruction; owning iterators carrying borrowed elements; dictionary keys/values.
- P13 controls, independent errors in formerly held views, and the default-template distinction
  between copying a prepared pointer and mutating its prepared slot.

Assert Language conflicts at their accesses for specification violations, location-only
`Unsupported_Kd` for unrepresented valid contracts/limits, and `InternalInvariant_Kd` for broken
published internal contracts. Capture cause, location and witness once; do not rerun proof for text.

### 7.2 Performance and size

No performance measurement was run for this proposal. B is expected to cost more during contract
construction and less in repeated body analysis because destination semantics are cached once.
Expected deletions are the scalar classification and duplicated W reconstruction, plus temporary
F9 raw rows; legacy adaptation is temporary added code. Net compiler size remains to be measured.

Measure identical workloads for A, B and baseline: contract graph nodes/edges, destination rows,
per-call visited `(node, holder, capability)` states, Loan-set work/bytes, elapsed compilation time,
allocated bytes and retained memory. Report construction separately from repeated analysis.
Use 1×/2×/4× nested/raw-chain, branching, recursive, mixed-marker and many-call workloads.
Work is proportional to visited materialized graph states/edges and resulting set joins; do not
claim it is linear in source size or that dense retention has disappeared.

Reuse scratch arrays/stamps and interned rows; preserve warm allocation tests and native O2
runtime/allocation guarantees. Declarations erase, so no runtime allocation is intended. Library
layout/ABI and O2 output still need equivalence checks when annotations or representations land.
Timing-only code belongs in `src/Benchmark`; failed measurements stay in artifacts.

Completion uses related Unit checks and a Session as [VERIFICATION](../../docs/dev/VERIFICATION.md)
requires, with the current R4/R5 MIR-codegen checks where applicable. Explicitly select relevant
native fixtures/milestones; no NativeAOT run is authorized by this plan. Review the difference
ledger and the full library/milestone corpus before R4 closure.

## 8. Integration mapping and review record

| Proposal section | Authoritative destination at adoption/implementation |
| --- | --- |
| §5 grammar, invariant and lifecycle | SPEC §3.5.2, §5.2.1/§5.6, §6 declarations/construction, §15.1.2–§15.1.3, §15.3.5, §15.6/§15.6.4, §16.3, Appendix E/F; affected examples and spec tests together |
| §5.2 standard storage relationships | SPEC §22.1–§22.2 and relevant collection storage contracts; public declaration/guarantee changes in LIBRARY |
| §4 ownership, records and bounds | DECISIONS in the first implementing commit; COMPILER_ARCHITECTURE and CODEMAP only where actual contracts/navigation change |
| §6 stage/removal state and §7 verified boundaries | PLAN and STATUS when implementation changes their owned facts |

No section is integrated or frozen at intake. SPECJ, GUIDE and history synchronization are outside
this request. The proposal does not reopen SETTLED's rejection of runtime pointer-position state:
it adds no runtime provenance or position and uses no producer-name whitelist.

Independent design authors: `/root/design_a` (A), `/root/design_b` (B). The planning session
selects the final design. `/root/reviewer`, who authored neither, reviews both alternatives and
the final proposal. Review disposition and the document checks are recorded with this unit's
commit evidence; implementation still requires U1 and a final implementation review.

The initial candidate review identified two blocking omissions: construction did not establish
an invariant over later aliases, and a Field selector did not identify a Field-specific Loan set.
The selected contract was restated around a persistent unsafe invariant and whole-holder
mapping (§4.1, §5.1); no per-pointer or per-field Loan state was added. The review also required
finite declaration graphs, explicit SPEC adoption and a workable internal-field migration.
The first full-document review then rejected exact content/backing-Type equality (it excluded
SPEC's BufferView) and unspecified partial transfer (it excluded UniqSlice.init). §5 was restated
using published typed backing routes and the existing initialization/completeness lifecycle,
with explicit unsafe establishment and transfer obligations. Both examples are now exit checks.
Final independent document review: accepted, with no remaining blocking findings. This is approval
of the plan's coherence and documentation, not semantic adoption or measured implementation evidence.
