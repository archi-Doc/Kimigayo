# S3 option 1 — Consolidate existing Binding owners

Date: 2026-10-20. Baseline: `f373cd678ea3b8f6e4065c23b08029d17dea06e8`.
Status: independently authored, reviewed and unselected alternative; not implementation authorization. No correction required.
Scope: S3 means closure-internal Origin, alias and inferred-result publication for 2a0-D.
It does **not** mean the older S3 bounds clarification in SPEC 15.3.6 / R4-M74 OQ12.

## Recommendation

Do not choose an exclusively owner-local fix as S3's complete solution.
The strongest version is to consolidate Binding's existing contextual queries and reuse its
obligation and premise mechanisms, without adding an aggregate body-contract representation.
That can improve a real part of the implementation, but it cannot supply the missing result
contract and receiver facts to MIR without crossing this option's stated boundary.
Keep those possible consolidations inside the selected complete design; do not ship a separate
closure-specific fixedness patch and label S3 complete.

This conclusion is about missing contracts, not a demonstrated new wrong-code failure.
The investigation read source and existing regression cases; it ran no compiler or tests.

## Required meaning and root cause

SPEC 15.6.5 fixes capture Origins, the call receiver and inferred whole-result Origins **in
that closure body**. An outer inferred region may therefore be fixed in the inner body without
becoming fixed in the enclosing body. Origin identity and this contextual classification differ.
SPEC 15.8.2 gives each inferred result Origin an internal per-call contract and requires every
result source to outlive it; covariant positions meet sources, invariant positions require equality.
SPEC 14.9 requires unreachable result sources to be checked too.

Meaning → representation gap → owner → deletion:

1. A body's assumptions differ from its enclosing body's solvable facts → kind-only fixedness
   cannot express the boundary → Binding must decide contextual classification → consumers
   must not infer capture/body membership from syntax.
2. Declaration aliases and initializer contributions differ from facts a body may assume →
   private resolver chains mix normalization with the source of a relation → OriginDeclarations
   and the premise catalog must publish the correct distinction → no duplicated alias recovery.
3. An inferred result is a contract target, not just the joined source Type → the current
   result signature stores the inferred source Type and a separate special rejection supplies
   a display-only result end → closure binding must establish the result contract → replace
   `CallLocalOrigin` only when every result source has an equivalent checked relation.

A global `BoundOrigin.IsFixed` or a new arm accepting every closure Projection cannot satisfy
(1). Extending the existing escape scanner cannot satisfy (3). These would be symptom fixes.

## Code evidence and same-cause sites

Paths below are relative to the repository; line references refer to the baseline above.

| Site | Observed fact and implication |
| --- | --- |
| `Binding/Binding.OriginProof.cs:29`, `FixedOrigin` | Static/Parameter/Input and all-fixed intersections only; its signature has no body context. This is insufficient as the full SPEC 15.6.5 classifier. |
| `Binding/Binding.OriginProof.cs:100`, `JudgeOriginRelation`; `:138`, `JudgeOriginAtoms` | Normalize at the use, then use kind-based fixedness to distinguish Unknown from region constraints. Context must be established before this choice. |
| `Binding/Binding.OriginProof.cs:182`, `OriginAtUse` | Walks enclosing declaration and initializer replacements. A published closure view cannot ask MIR to repeat this syntax walk. |
| `Binding/Binding.OriginDeclarations.cs:446`, `ResolveOrigin`; `:472`, `CompleteOriginDeclaration` | Existing substitution/equality owner; aliases have declaration lifetime and completed state, not free-standing inference edges. |
| `Binding/Binding.OriginDeclarations.cs:734`, `ResolveInitializerOrigins` | Emits `RecordRegionBound` from resolved replacement to original slot before rewriting the Type. These source-to-slot edges must survive publication. |
| `Binding/Binding.LocalRegions.cs:280`, `ResolveLocalResultOrigins`; `:372`, `RecordRegionBound`; `:434`, `JudgeLocalRegion` | Current result normalization and finite-to-fixed chain handling; these are shared with non-closure local inference. |
| `Binding/Binding.Closures.cs:516`, `BindConcreteClosure` | Completes input/declaration contracts, captures and body, then publishes `plan.Signature` using `symbol.Type`. It does not publish independent inferred-result formal slots plus source relations. |
| `Binding/Binding.Closures.cs:107`, `CallLocalOrigin`; `:640` | Rejects the first call-local Origin found in the completed inferred result. This is a compatibility guard, not a general per-source contract. |
| `Binding/Binding.ArgumentOperations.cs:112`, `CallResultSlot` | Explicitly documented as a display-only Projection slot. Reusing it as arbitrary result semantics is a contract change. |
| `Binding/Binding.ControlFlow.cs:241`, `FinishResult`; `:291`, `DecideResult`; `:350`, `ResultContext` | Already owns result sources and their nodes. Use these while binding; do not introduce a second return-source syntax visitor. |
| `Binding/BoundClosure.cs` | Existing plan owns captures, signature, environment Type and a single minimum receiver. It has no per-case receiver/result-Origin publication. |
| `Binding/Binding.OriginPremises.cs:144`, `ExtractOriginPremises` | One catalog of assumptions, with inputs and declaration contracts. Anonymous inferred results are not ordinary named-result premises. |
| `Binding/Binding.FunctionTypes.cs:290`, `ResultFixedForCall` | Decides call independence using result/capture Copy and carrier membership. This is **not** the same question as fixedness in a body. |
| `Binding/Binding.ValueCalls.cs:108`, `FixedInBody`; `:250`, `FixedInBodyOrigin`; `:541` | Call-result admissibility remains separate; receiver-dependent results are currently Unsupported. A lifetime name alone gives no capture authority. |
| `Binding/TyCtxt.Retention.cs`, `Retains(BoundType, BoundType, BindingSymbol?)` | Computes value-call retention from signature inputs/result and premises; no receiver input is supplied today. C2 must supply it. |
| `Binding/Binding.Retention.cs`, `RetentionOf` | Reuses the premise catalog. Changing its assumptions changes Loan retention and must be checked beside Origin judgments. |
| `Mir/Build/MirBuilder.cs:215` | Closure receiver Type is built from `BoundClosure.EnvironmentType` and minimum receiver. There is no published contextual Origin contract to consume. |
| `Mir/Build/MirBuilder.Values.cs:268`, `Closure` | Capture creation is acquired once into an aggregate; closure bodies are separate. Outer capture-entry borrows must stay finite in the outer body. |
| `Mir/MirTypes.cs`, entry contract | May consume published Types, not rediscover Binding decisions or source syntax. It is not the owner of S3 fixedness. |

All `Binding/` and `Mir/` paths above are under `src/Kimi/Compiler/`.
Also search every caller of `FixedOrigin`, `RefutesOriginRelation`, `OriginAtUse` and
`RecordRegionBound`; the found shared callers include `Binding.CallRelations.cs:312`,
`Binding.OriginFits.cs:196–276`, `Binding.Diagnostics.cs:146` and `Binding.OriginProof.cs:434`.
Do not mechanically merge similarly named predicates with distinct contracts.

## Strongest owner-local design

The serious alternative is contextual **queries**, not ad hoc closure branches.
Binding retains its current Types, `BoundOrigin`s, declaration records and obligation list.
It computes body-relative Origin classification on request, and C1-F4's already planned
small `TyCtxt` view exposes the answer. No new all-purpose body-contract object is introduced.

The complete classification takes the definition and admitted Semantics case as semantic
inputs, rather than a boolean closure mode. Inside Binding it uses the declaration's input
Types and the existing capture/environment Types. `BoundOrigin` identity stays interned and
unchanged; classification belongs to the query's body, not to the atom globally.

For a capture Origin already carried by an environment Type, the closure query treats it as
an incoming fixed end. For the enclosing body the same Origin retains its outer classification.
A capture-entry borrow belongs to its creation body and is not upgraded there. Meets are
fixed only when every operand is fixed in the selected body/case. Nested closure queries
start at the immediate closure contract; they do not turn all enclosing local declarations
into premises. Projections of universally fixed inputs follow the existing contract rules.

The closure receiver is a separate incoming contract end. Environment-owned borrows depend
on a borrowed receiver; in a Consuming call their storage is finite. This determination must
use C2's eventual per-case receiver fact. Current `BoundClosure.Receiver` is only the strongest
minimum receiver and cannot be promoted to a correct per-case fact by this design.

### Alias treatment

Keep `OriginDeclarations` as the sole normalization owner and `LocalRegions` as the owner
of committed initializer source-to-slot contributions. Do not add an alias dictionary beside
them, and do not mark every replacement pair as an assumed equality.

A declaration's established equality may normalize equivalent ends. An initializer's inferred
source-to-slot contribution is an obligation edge in the enclosing body. It is not an inner
body premise simply because a capture carries that slot. At the closure boundary the inner
body uses its fixed incoming end; the enclosing body's graph remains responsible for proving
that the creation/capture flow satisfies it. A query-only design must define where that end
is represented and how the boundary relation is retained; current records do not supply it.

### Premises versus obligations

R1–R4 stay the premise catalog's responsibility; use its case-conditioned relations.
Return-source relations, initializer bounds and local Type clauses are obligations.
Never add a return edge to the premise graph used to prove the same return.
A completed inferred-result contract may become a callee guarantee only after its body checks;
this does not make the body's own source relation an assumption.
The existing `OriginDischarge.CallBorrows` distinction and exclusion of T1/Definition/Length
rows remain governed by 2a0-D, not reclassified for closures.

### Result publication: the blocking boundary

A complete S3 solution needs, for each result Origin position, a contract target distinct
from the source that supplies it, plus every source relation and its variance/case/location.
`ResultContext.Sources` and `SourceNodes` supply the existing binding-time evidence.
`BindingObligation` supplies equality, source Type, clause, well-formedness and case information.
Neither currently names a fresh inferred result slot as an internal call contract.

Example: existing `AResultOverCallLocalStorageIsRefuted` includes `func (x: i32) => x@ref`.
Its inferred Type carries the source Projection. If that same graph node is merely reclassified
as the fixed result, source-to-result becomes reflexive and loses the finite-to-fixed obligation.
If it stays finite, the result lacks the required fixed target. The special scanner catches
this today, but retaining it beside a new partial graph repeats the rule instead of solving it.

A single display-only `CallResultSlot` cannot describe multiple result positions with distinct
Origins and variance, nor instantiate independent call results. Making semantic result slots,
recording the internal/external mapping and publishing receiver retention is a shared contract
change. It belongs to a complete C1-F4/C2 design (options 2 or 3), even if physically stored in
`BoundClosure`. Calling this only an existing-owner cleanup would conceal that change.

Therefore this candidate stops here: it provides a possible small query implementation for
body classification, but **no safe standalone implementation sequence closes S3**.
Do not change production acceptance, delete `CallLocalOrigin`, or remove ValueCalls' gate on
this candidate's authority. Existing Unsupported boundaries remain until the complete design.

## Reproducers and verification matrix

Recorded regression source, not newly executed evidence:

| Existing owner/method | Meaning to retain |
| --- | --- |
| `ClosureEnvironmentOriginTest.AResultOverCallLocalStorageIsRefuted` | Parameter-local and consumed-environment result sources are Refuted at their returned value. |
| `ClosureEnvironmentOriginTest.AResultOverExternalOriginsOutlivesTheCall` | Captured shared references and values moved out obey distinct dependencies. |
| `ClosureResultOriginTest.ConcreteResultsKeepFixedExternalOrigins` | Direct, tuple, Option, array, borrowed receiver and fresh-argument cases retain external sources. |
| `ClosureResultOriginTest.AResultOverTheEnvironmentIsUnsupported` | Current boundary for receiver-dependent, exclusive, nested and Consuming results. |
| `ClosureResultOriginTest.AnErasedResultCannotBorrowTheCallReceiver` | Function erasure cannot hide the receiver dependency. |
| `OriginContextualClosureTest.AnOriginBearingSignatureTypesTheHeader` | A fixed expected signature is ordinary result checking, not inferred-result publication. |
| `OriginPremiseLifecycleTest.RemovingAndRestoringAClauseChangesOnlyItsJudgment` | Edit invalidation and no stale premises. |
| `OriginPremiseLifecycleTest.WarmProofAllocatesNothing` | Existing warm-proof allocation regression. |

These classes are in `tests/xUnitTest/Tests/`.
Additional proposed probes are unexecuted hypotheses, not claims of failures:

- Capture an outer local with an inferred Origin alias; assert the same identity is fixed only
  in the inner body and the enclosing source-to-slot obligation still exists.
- Two nested closures capture that value; alter the enclosing alias clause and rebind.
  No stale inner assumption may remain; independent sibling closures must not share obligations.
- Return two values at distinct covariant result positions, then an invariant carrier of two
  unequal Origins. Check each source and location, including an unreachable return.
- Repeat receiver-dependent capture behavior for every admitted owner/ref/uniq case.
  Do not invent a pair outer-Origin premise in owner cases (SETTLED's explicit prohibition).
- Distinguish Copy of a captured shared reference from borrowing its environment slot,
  exclusive reborrow through the receiver, and moving a reference out in a Consuming call.
- Close the same generic closure under two substitutions; verify shared Type/Origin identities
  only where the contract permits, and independent per-call result instantiation.
- Attempt to use the result obligation as its own premise. It must remain Refuted or Unknown,
  never become Proven merely because its edge was added to the obligation graph.

Use `OriginObligationVerdictTest`, `OriginPremiseCatalogTest`, `DeclaredOriginSlotTest`,
`LocalRegionInferenceTest`, `LocalRegionCompositionTest`, `RetentionTest`, `MirBuildTest`
and `MirBorrowsTest` for the lowest observable layer. Extend owning classes and shared helpers.
New public-view assertions belong with 2a0-D/C2 tests, not a second closure-analysis harness.

## Small units and stop conditions

These units describe the strongest safe work decomposition if the selected complete design
adopts the query approach. They are not permission to implement this incomplete alternative.

| Unit | Prerequisite and change | Required check / done condition |
| --- | --- | --- |
| L0: boundary evidence | Freeze source baseline; turn the probes above into source→Binding→MIR evidence under the eventual contract. First record current status and Unsupported gates. | Reproduce the actual missing fact before selecting the behavioral delta; no manually manufactured Origin graph is integration evidence. |
| L1: responsibility split | Only if the next change cannot fit or a hotspot requires it, move result-contract work out of oversized `Binding.Closures.cs` into an owner partial; keep `ResultContext` as the source owner. | Separate behavior-preserving commit, identical diagnostics, semantic HIR/MIR snapshots and affected generated fixtures per VERIFICATION; no behavior change hidden here. |
| L2: contextual query | Implement one body-relative classification at its Binding owner, replacing callers whose meaning is exactly SPEC 15.6.5. Leave retention/call-independence predicates distinct. | Atom classification for ordinary/closure/nested/case contexts; failing-before regression; unchanged non-closure verdicts and default-pipeline acceptance. |
| L3: alias obligations | Publish committed initializer contributions via the existing C1-F4 row owner; declaration equality stays normalized by OriginDeclarations. | Each applicable relation appears once; alias/direct spelling agree; local clauses never prove themselves; no cross-body alias traversal. |
| STOP: result contract | L2/L3 cannot represent distinct inferred result targets or receiver-per-case facts using current contracts. | Stop and choose a shared-contract design; do not add one-form branches, a one-feature side table or a second scanner. |
| L4: integration under selected design | Only after 2a0-D and C2 decide result identities/mappings, wire the single result source owner and remove redundant legacy logic at its recorded stage. | All source relations, result positions and call substitutions agree across public view, MIR and diagnostics; this is outside pure option 1. |

L2 and L3 are not independently useful to merge if they force repeated rewrites in L4.
Prefer including them as internal steps of the selected complete package.
Binding's legacy record twin remains until R6-check under R4-M74 OQ11(b).
The receiver-dependent-call gate follows OQ4 and 2c-5; removal is not part of a local cleanup.

## Size, speed and memory comparison

Estimates are hypotheses, not measurements or budgets.
A contextual query plus corrected callers is approximately +70–130 / −20–50 production lines;
responsibility moves are approximately net zero. Alias publication belongs to planned C1-F4
and must not be counted again as option-1 savings. The complete result contract has no honest
local-only estimate: assigning one would assume the missing shared design.
Possible tests for L2/L3: +100–180 lines in existing classes.

A query scans relevant contract Types/captures: roughly O(Q × C × T) in the naive form,
where Q is queried atoms, C captures and T the visited Type size. Query scratch can reuse
existing pools, avoiding retained tables; repeated nested queries may cost more than a
published body view. No speed advantage has been demonstrated. Memoizing per body/case
would require lifecycle and storage decisions and moves toward option 3.

Retained memory is potentially unchanged for pure queries; this does not account for the
required result contract, so do not compare it as a complete solution's memory saving.
Measure cold/warm compiler time, allocated bytes and retained Origin/Type references on
matched capture-count, nesting-depth, result-position and case-count workloads (1×/2×/4×).
Use `OriginProofScalingTest` and Benchmark timing-only workloads; retain failed evidence.
Native O2 runtime/allocation assertions stay unchanged and must not be weakened.

## Decisions, review and completion

R4-M33 supersedes historical R4-M32; do not treat M32 as authority to move work back into
Binding. R4-M51/R4-M52 govern retention and premise scope; R4-M74 governs 2a0-D, 2c-D,
D5 gates and legacy twins. R5-M36 governs closure substitution; R5-M63 requires kept rejected
closure bodies to retain independent result errors. CODEMAP identifies existing owners.
SETTLED's pair-case and exclusive-follow rules remain constraints, not simplifications.

Any fresh result-slot identity, new publication responsibility or retained structure requires
DECISIONS in the first implementing commit, shared-representation review, and the relevant
owner-approved 2a0-D/C2 package. No vocabulary or exception is authorized by this candidate.
Do not revise SPEC, existing frozen proposal text, or unsupported coverage to fit this option.

This documentation-only candidate requires source/diff/reference review; no build or tests.
A future implementation uses focused Unit verification, related allocation regressions and
Session verification, plus A5 census/ledger and explicitly selected native fixtures where its
behavior changes generated code. Behavior-preserving restructuring has its own evidence.
Commit association uses `verify-commit.ps1`; an unsuccessful required check blocks pushing.

Final assessment: reject option 1 as a standalone S3 resolution. Its useful contribution is
an exact ownership and deletion boundary: contextual classification may stay in Binding,
but result identities, alias boundary relations and receiver facts must have one published
contract before MIR can judge them. A selected alternative may reuse the local query without
adding redundant retained state; it must still solve those missing facts explicitly.
