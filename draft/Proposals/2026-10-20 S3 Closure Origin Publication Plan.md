# S3 Closure Origin Publication Plan

Status: 一部取り込み (open); option 3, one body-relative Origin contract; adopted for implementation on 2026-10-20 (PLAN), first
unit S3-0 (gate R); adoption establishes no support. Inspected at `f373cd67`, revised at `338831f7` (§8); line numbers name both.
This S3 is the closure publication gap in 2a0-D/C2, not the SPEC 15.3.6 clarification also called S3 (OQ12).

## 1. Required meaning and root cause

SPEC §15.6.5 makes fixedness relative to the checked body. A captured outer Origin can be inferred or finite outside and fixed
inside; this changes neither its identity nor its outer bound, and merges no Loans. SPEC §15.8.2 makes each omitted result Origin
a per-call Origin of the internal call contract and judges every returned source against it, unreachable sources included.

Design chain:

1. **Body-relative atoms.** A body consumes a contract whose captured, input and receiver Origins are fixed; `BoundOrigin.Kind`
   cannot say so. Binding publishes one body-relative classification through the C1-F4 view; `MirOrigins` consumes it. The
   solver needs no closure test.
2. **Result contract.** A result inferred from its sources is an outward contract, not the sources' own local Origins. Today
   `BoundClosure.Signature` reuses the inferred result Type and its source Origins. C2 publishes result slots and their source
   relations; the first-local-origin rejection and the capture scans go once the common relation check and call consumers replace them.
3. **Aliases and bounds.** Aliases and inference bounds must survive publication without becoming assumptions. Private
   replacement maps and local bounds are no published proof contract. Binding owns resolution and publishes aliases and obligations
   apart from R1–R4 premises; consumers neither inspect syntax nor repeat Binding inference to explain a failure.

This is a missing shared contract, not evidence that a supported program is accepted unsoundly. `Unsupported_Kd` gates and the
old closure-local rejection hide parts of the gap.

## 2. Cause sites and affected owners

Paths are under `src/Kimi/Compiler/`.

| Source | Observed fact and consequence |
| --- | --- |
| `Binding/Binding.OriginProof.cs:29 FixedOrigin`, `:118 JudgeOriginAtoms` | Static, Parameter and Input atoms (and their meets) count as fixed; no checked-body parameter. `ValueCalls.cs:133` and `FunctionTypes.cs:334` have their own local `FixedOrigin`. |
| `Binding/Binding.Diagnostics.cs:146 RefutesOriginRelation` | `BodyLocalOrigin` + global `FixedOrigin`; it cannot become the body-relative classifier. |
| `Binding/Binding.OriginDeclarations.cs:446 ResolveOrigin`, `:472 CompleteOriginDeclaration` | Equalities rewrite the private `Replacements` (`:556`); established aliases differ from obligations being judged. |
| `Binding/Binding.OriginDeclarations.cs:735 ResolveInitializerOrigins` | Records replacement bounds (`:742`) before compacting inference obligations (`:745`); capture them here, not by reconstructing syntax. |
| `Binding/Binding.LocalRegions.cs:280 ResolveLocalResultOrigins` | Replaces local regions by meets of `LocalRegionSources` (`:79`) whatever the position's variance; not a C2 result-contract algorithm. `RecordLocalRegionBound` (`:343`) drops the condition and turns an equality into two unflagged edges; `RecordRegionBound` (`:372`) keeps only the first evidence per source. |
| `Binding/Binding.ControlFlow.cs:178 BeginResult`, `:241 FinishResult`, `:291 DecideResult`, `:350 ResultContext` | Result collection keeps source Types (`Sources`) and `SourceNodes`, including transfer operands (`Expressions.cs:731`). Reuse it; contexts are pooled per pass. |
| `Binding/Binding.Closures.cs:107 CallLocalOrigin`, `:479 CallResultOrigin`, `:516 BindConcreteClosure` | One recursively found local Origin of the call's own storage is rejected (`:640`), on `symbol.Type` after `ResolveLocalResultOrigins` rewrote it (`:609`); the first local found selects the reported Borrow: the primary span in a block body, the related span otherwise (`Diagnostics.cs:102`, `:428`, `:459`). `Signature` is formed (`:626`) before receiver classification (`:639`). `CallResultSlot` (`ArgumentOperations.cs:112`) is a diagnostic marker read only by `Diagnostics.cs:427`, `:591`; it is no per-position signature. |
| `Binding/Binding.ControlFlow.cs:50 SelectCommonType`, `:123 CommonBorrowResult`; `Binding/Binding.TypeRelations.cs:42 CommonOriginType` | The result join takes a candidate every source fits or meets one top-level borrow layer; joins inside results require identical Types at invariant positions. Neither forms proven Origin equalities (§4.2 step 3). |
| `Binding/BoundClosure.cs:19` | Captures, Signature, EnvironmentType, Receiver, exclusive-use flags, Storage; no body-relative Origin facts or result source rows. |
| `Binding/Binding.FunctionTypes.cs:291 ResultFixedForCall` | Scans every result Origin and capture (`:355`) for Copy or external dependence. |
| `Binding/Binding.ValueCalls.cs:248 FixedInBodyOrigin`, `:531` | A different, call-site fixedness question; receiver-dependent and environment results are Unsupported. Do not merge it with the body question. |
| `Binding/Binding.OriginPremises.cs:146 ExtractOriginPremises` | R1–R4 contextual catalog plus R5; R3 only for named non-constructor functions (`:170`). Preserve these boundaries. |
| `Binding/Binding.Origins.cs:31 InputOriginCount`, `:637`; `OriginDeclarations.cs:598`, `:613` | Aggregate Input atoms: parameter slots only for named functions and property accessors (`:637`); result-only slots (`InputIndex = -1`) only for clause-quantified results (`:613`). Closures get none today. |
| `Binding/Binding.ClosureInstances.cs:13 ClosureSignature`; `TyCtxt.Substitutions.cs:42 SubstOfClosure` | A closure's context is its canonical Subst (R5-M36); new views instantiate through the same path. |
| `Binding/TyCtxt.Retention.cs`, `Binding/Binding.Retention.cs:97 RetentionOf` | Retention reads declaration premises, never the call's solved Origins, and is per argument; C2 keeps this. |
| `Mir/Build/MirBuilder.cs:216` | The closure receiver Type (`:219`) comes from one `BoundClosure.Receiver`; a borrowed receiver has no explicit receiver Origin. |
| `Mir/Analysis/MirBorrows.Calls.cs:24 Call` | Temporary environment-to-result carrying until F7 reads C2 (unit 2c); no substitute for C2 retention. |
| `Mir/MirTypes.cs:85 Agree`, `Mir/MirValidator.Types.cs` | Type agreement ignores Origins; neither may infer missing contracts or become another Origin validator. |

Planned, not present at this base: `TyCtxt.Origins.cs` with `TyCtxt.OriginObligations` (2a0-2), `MirOrigins` (2a-1a),
`OriginAtom.Of`, `UniversalRelations.For` (2a0-D) and `TyCtxt.Closure` (2c-2). `TyCtxt.ResetViews` (`TyCtxt.cs:193`) exists.

## 3. Sources and observation status

Existing regressions (inspected, not run here; `tests/xUnitTest/Tests/`):

| Test | Source (abridged) | Current expectation |
| --- | --- | --- |
| `ClosureResultOriginTest.AResultOverTheEnvironmentIsUnsupported` | `let n = 7` / `let f = func [n] () => n@ref` / `let r = f()` | One `Unsupported_Kd` at `f()`. |
| `ClosureEnvironmentOriginTest.AResultOverCallLocalStorageIsRefuted` | in `main`: `let f = func (x: i32) => x@ref` / `let r = f(1)` | One `UnsatisfiedOriginRelation_Kd` at `x@ref`. |
| `ClosureResultOriginTest.ConcreteResultsKeepFixedExternalOrigins` | `let n = 7` / `let values: [1 of ref/i32] = [n@ref]` / `let f = func [values] () => values[0]` / `let first = f()` | Accepted; the result keeps the external Loan. |
| `ClosureResultOriginTest.TheReturnedViewProtectsTheOwnerAfterTheLastClosureUse` | the same with `var n`, then `n = 9` | Binding completes; a Loan conflict, no `Unsupported_Kd`. |
| `BlockClosureInferenceTest` `FixedBorrow`, `MeetBorrow`; `AnInferredResultCannotBorrowTheCallsOwnStorage` | block-bodied closures returning a captured reference, a join of two parameters, a local borrow | Accepted, accepted, `UnsatisfiedOriginRelation_Kd`. |

New probe extending `FixedBorrow`. The comments state SPEC verdicts; the independent reviewer's probe on a clean `338831f7` build
agreed (`f` accepted, a later write to a `var n` is a Loan conflict, `g` one `UnsatisfiedOriginRelation_Kd` at `m@ref`):

```kimi
public func main() -> ()
    let n = 7
    let p = n@ref
    let f = func [p] ()
        var r = p          // r is its own inference node; p's Origin is fixed in f, inferred in main (bounded by n's Loan).
        r = p              // Relation row p -> r.
        return r           // Row r -> result formal; the contract binds the formal to p's external Origin.
    let result = f()       // result keeps p's Loan on n.
    let g = func [p] ()
        let m = 8
        var r = p
        r = m@ref          // Refuted: m's finite Origin must outlive the fixed result Origin (chain m@ref -> r -> result).
        return r
```

Also required: a join of `p` and another captured reference meets at covariant positions; an invariant aggregate result needs a
proven equality, never the meet-only helper.

## 4. Chosen representation

Use the planned C1-F4 view, not another closure-analysis database. Its definition key includes the checked body; its instantiated
key adds the closure Subst and premise scope where they affect the contract.

### 4.1 Published columns

- **Atoms.** Canonical `BoundOrigin` identity plus the body-relative class from SPEC §15.6.5: Fixed, FiniteBody (body-local),
  FiniteStatic (a Borrow of mutable static storage) and Inferred (inferred regions, including raw and through-borrow Borrows); with
  bound and case mask. C2 publishes which capture atoms and receiver contract are imported; the common publisher classifies once.
  - Environment bindings are Fields of the call receiver: a borrow of one through a borrowed receiver is an inferred region
    contained in the receiver Origin (§15.6.3; §15.8.2 row 1); in a Consuming call it is FiniteBody. Classify per receiver case.
  - Meets keep interned operand identity. A captured atom is never relabelled globally or copied to mark it fixed.
- **Premises.** Immutable R1–R4 contract relations, case-conditioned, from the existing catalog's resolver. R5 stays the ordinary
  local-Place rule and never turns a local annotation into its own assumption.
- **Relations to check.** The planned F4 tuple (ends, Equality, Source, Clause, destination Type, CaseMask): return sources, call
  obligations and unresolved local alias/bound relations. Disjoint in meaning from the premises.
- **C2 result slots.** One formal per inferred signature Origin position, with canonical slot identity and composed variance; the
  result Type contains these formals. Repeated positions share a formal only when the contract makes them equal. Source rows stay
  separate even when their Origin terms agree. No per-call source list or per-capture identity map.

Formal identity reuses the aggregate Input-slot identity scheme (`InputIndex = -1`), and its storage from S3-4a, after a
behavior-preserving extraction of its allocator.
The allocator now serves only named functions, property accessors and clause-quantified results (§2), so serving closures extends
its responsibility: record it in DECISIONS in the unit that first allocates closure formals, under the F4/C2 package; the
extraction keeps named-function behavior unchanged. Until S3-4a, publication-only formals use the same identity scheme in
C2-owned storage, never the symbol's `AggregateInputOrigins`, which `InputOriginCount` (`Origins.cs:31`) counts and active call
paths size from. If the contract cannot admit closure results, stop and redesign the formal identity. `CallResultSlot` stays
only the old diagnostic projection until H11.

### 4.2 Result formation

The algorithm to prototype; current Binding does not implement it.

1. After structural result selection, reuse `ResultContext.Sources`/`SourceNodes`; do not resurvey the AST. Enumerate Origin
   positions with the existing component/slot variance rules. A nested Function Type's own binders are not this closure's slots.
2. For each slot, derive the contract-side source expression from inputs, captures and receiver under §15.8.2's five rows. Keep the
   full rows at the Binding obligation producer, before local-region projection or compaction, then run the common scoped inference
   over them. `LocalRegionSources` is not that representation (§2). Outer-storage atoms are imported fixed; this body's storage
   stays finite. Every source contributes, not only the first return. The walk ends on cycles by node identity.
3. A covariant slot takes the meet of admissible boundary sources. An invariant slot requires equal boundary Origins through the
   common proof owner and keeps the equalities as obligations. Contravariant and mixed positions follow §15.3.6; mixed is invariant.
   No second, closure-only solver.
4. Form a result formal and its contract bindings only from boundary expressions: a receiver-owned borrow uses the receiver Origin;
   a copied shared capture its external Origin; an exclusive reborrow both external and receiver Origins where §15.8.2 requires; a
   reference moved out of a consuming environment its referent's external Origin.
5. Publish every returned source → formal relation separately (direction or equality by variance). A finite source never becomes
   a premise bounding the result: its relation reaches the result boundary and the ordinary solver refutes it. An unbounded
   raw-derived source stays inference without a finite bound, never relabelled static.
6. Publish a contract binding only after its boundary expression is formed independently of source-fit judgment. Source-fit rows
   and local equalities being checked never enter the premises. A failed invariant formation publishes no partial subset.

For `p -> r -> result` the contract binds the formal to the boundary expression `p`, while `p -> r -> resultFormal` remain body
obligations. For `m@ref -> r -> result` the finite node never enters the boundary set, and the chain still fails. Two different fixed
inputs at an invariant result remain an unproved equality unless an independent declaration premise proves it.

### 4.3 Gate R (blocking, before S3-1)

A source-backed prototype and a reviewed contract answer every row. Ordinary input supplies the facts; synthetic graphs test the
solver but prove no correspondence.

| Required proof | Evidence that closes it |
| --- | --- |
| Lossless input, one solver | Name the common scoped-inference entry and the full-row owner; keep condition, equality, every source and evidence and composed variance before deduplication. Include trial fits apart from committed requirements, direct local checks that bypass `AddObligation`, and structural shortcuts (`OriginOutlives`) as their own outcome. |
| Premise scope per row | Each relation and each candidate computation names its permitted premises (use scope, admitted case) without unioning or intersecting unrelated scopes. Show which published facts determine them (the tuple's Source, Clause and CaseMask, or the inference owner's records). If none do, the obstruction becomes a 2a0-D/2c-D design input comparing a fix inside the common owner with a shared-tuple change; a genuine language ambiguity goes to the standing process with competing derivations. Neither is chosen here. |
| Fixed result target | For `p -> r -> result`: the body-side binding equation, formation phase, permitted premises and caller image, without assuming the source-fit relation being checked; every source checked independently. Pair with the finite `m@ref` refutation. |
| No-bound raw result | The target identity and caller image with no finite or boundary candidate; no erased target, top-like anchor, fabricated static or false finite class. |
| Complete inference | Covariant, invariant, contravariant and mixed positions; joins, reassignments and unresolved meets; cycles stay inference variables; nested Function binders and returned closures stay distinct. |
| Cases and receiver | Borrowed and consuming receivers, environment-binding borrows per case, captured shared/exclusive values, all admitted masks; never widen a case-specific premise. |
| Publication timing | Capture all `ResultContext` sources before reset; keep failed definitions under R5-M63; classify the receiver per case before result formation (today `Signature` precedes it, §2); form the contract before dependent call binding; structural source-position correspondence (an equal result Type proves none); stable structural slot keys for result-only Input atoms; audit all readers. |

Each witness records source/configuration hash, Type and slot identities, atom classes in both bodies, row source and mask,
permitted premises and binding, caller substitution, and expected and actual verdict and location. If no single common inference
representation proves these rows, hold S3 and return the counterexample for a shared redesign: no closure-only solver, shape
allowance or fallback. This is the main uncertainty of the selected direction.

### 4.4 Ownership and lifecycle

| Owner | Responsibility |
| --- | --- |
| Binding's C2 contract producer | Captures, receiver per case, result slot formation and source classification. |
| Planned `TyCtxt.Origins.cs` (2a0-2) | Definition-relative atoms, resolved rows and premise scope. |
| Planned `MirOrigins` | Relation judgment. |
| Planned `TyCtxt.Closure` (2c-2) | Call retention over receiver plus parameters, from the same contract. |
| `MirBuilder` | Imports receiver and result Types through C2; never inspects capture syntax or rebuilds them. |

- **Staging.** Publication-only data first: the C2 view has no production reader, and the active `BoundClosure.Signature`, capture
  and body Types and call path stay coherent until the first consuming unit. Removal condition: promotion of the validated C2
  contract and retirement of the legacy readers it replaces. Fresh result formals never enter the active signature early:
  `ValueCalls` sizes slots from current declarations and rejects surviving Input/Parameter atoms, and substitution can leave
  out-of-range slots unchanged.
- **Promotion.** Promote result Types, slot counts, substitution and call readers together after auditing `Calls`, `ValueCalls`,
  `CallableContracts`, `ContractMatching`, `ResultPremises`, `OriginInference`, `Storage` and result-slot enumeration. If that seam
  cannot keep existing acceptance, combine the first consuming changes into one verified unit and re-estimate it.
- **Legacy rejection.** `CallLocalOrigin` stays until H11 and reads the meet-rewritten `symbol.Type`; it flags only the call's own
  storage (FiniteBody), never FiniteStatic, deciding consumption from the minimum receiver. At S3-3 the shadow publication compares
  its current answer (verdict, Origin, span) with the first FiniteBody source row under the minimum receiver's case, in source
  order, over the closure owners and the diagnostic snapshot. S3-4a switches it to those
  rows and deletes the closure meet rewrite only if they agree; otherwise the rewrite stays its sole input until H11, recorded in
  PLAN. One input at a time; no runtime fallback.
- **Validation.** Publication follows a complete producing result. A rejected creation still exposes its resolved body (R5-M63);
  unavailable Origin facts keep the located `Unsupported_Kd`. The producer validates its new columns; `MirValidator` stays MIR's
  single validator and checks cross-column identity only. A missing fact in a promised contract is `InternalInvariant_Kd`.
- **Lifetimes.** `TyCtxt.ResetViews`/Binding invalidation clears cached immutable contracts; passes that recompute rows share
  backing storage. Live views and scratch ranges never retain stale syntax, scopes, captures or substitutions after an edit;
  interned Types and Substs keep their lifetime. F4's pass-specific projection follows its pass reset, C2/retention memo entries
  last until an edit: two lifetimes, not two copies. Warm rebind allocates nothing to republish equal facts.

## 5. Alternatives, deletions and cost

| Option | Assessment | Decision |
| --- | --- | --- |
| [1: owner-local queries](../Sketches/A5%20Step%202/S3-M69/S3%20Owner%20Local%20Design.md) | Smallest partial repair; cannot publish a complete result/receiver contract without leaving its scope. | Reject as completion; keep its census. |
| [2: inner binders](../Sketches/A5%20Step%202/S3-M69/S3%20Inner%20Binder%20Design.md) | Explicit boundary through fresh imports and substitution; adds inner Types and capture maps; owes the same proof. | Coherent but wider in representation and lifecycle. |
| 3: body-relative common view | Keeps imported identities, creates only result formals; one C1-F4/C2 publication and ordinary relation checking. | Selected, gated by gate R and the package approval. |

Option 3 fits one canonical form and local published contracts and avoids option 2's import and Type cloning. That is a structural
judgment, not a measured speed claim; gate R can still invalidate it.

Deletions (only when their readers move; no speculative deletion of general Function Type fixedness rules):

| Removed | Replaced by | When |
| --- | --- | --- |
| `ResultFixedForCall`'s capture scan | C2 result contract at the call | S3-4a |
| `ResolveLocalResultOrigins`' closure meet rewrite | C2 source rows (also feeding `CallLocalOrigin`) | S3-4a if S3-3 agrees, else H11 (§4.4) |
| Environment-to-result carrying in `MirBorrows.Calls` | C2 retention | 2c-3, atomically |
| Pair self-store rows for owner-or-uniq carriers | 2c-3's receiver stores (AB-2) | S3-4b |
| `CallLocalOrigin`, its `CallResultSlot` marker | F4 closure-result relations | H11 |

Cost estimates (hypotheses, not caps): production for this slice, not all of F4/F7, +250 to +420 / -120 to -210 lines at
completion; early publication units may be net positive; tests +250 to +400; behavior-preserving moves about net zero. Re-estimate
after gate R and count actual deletions per area in each commit.

- **Time.** Publication is linear in distinct atoms + relations + result occurrences; local-bound walks are memoized per body and
  mask class, not per result or call; retention is per contract instantiation, not per invocation. F4 keeps its planned bound
  Θ(N_o + E_o·⌈F/64⌉·ρ + F_t·P_m); result formals increase F and are counted.
- **Memory.** Cold immutable result-contract storage; warm 0 B for unchanged publication and retention; retained
  O(atoms + relations + result positions + F4 bitsets), with no outer/inner clone product.
- **Measurement.** Equivalent workloads under a fixed compiler and configuration: time, allocation and retained bytes. Added
  retained memory must buy simpler code or demonstrated speed; no relaxed assertion or extra warm-up.

## 6. Units

All follow design selection, review and the required owner approval; existing F4/C2 units are refined, not duplicated.

| Unit | Change and prerequisites | Checks and exit |
| --- | --- | --- |
| S3-0 | Reproducer, census and gate R prototype on the current F4/C2 head. Uses 2x-P4's Origin prototype. 2x-P7 has not run: S3-0 takes its listed closure cases as inputs, and 2x-P7 stays 2c-D's prerequisite. Map every §2 site and baseline its errors and rows. | Ordinary-source captures, joins, invariant results, raw borrow, cycles: actual Types, atom identities, rows, masks and verdicts recorded. No unidentified result-source path; gate R proven or explicitly blocking. |
| S3-1 | Behavior-preserving restructure: move closure contract formation out of `Binding.Closures` and extract the Origin slot allocator before behavior edits; respect 2c-0 and 2x-R11; no mechanical size evasion. | Closure/result/Origin tests plus semantic and diagnostic snapshots on the same inputs: no verdict, row or Type identity change. Review hotspot history before the next edit. |
| S3-2 | Body-relative atom publication and alias/obligation separation in 2a0-2; no consumer switch. | `OriginPremiseCatalogTest`, `OriginPremiseLifecycleTest`, `DeclaredOriginSlotTest`, `LocalRegionInferenceTest`, `LocalRegionCompositionTest`; published identities and masks, unchanged old path, no alias consumed as its own proof. |
| S3-3 | Reviewed result formation, capture/receiver imports and C2 retention (2c-2) as validated publication-only data; active signature and call path unchanged. Shadow comparison for `CallLocalOrigin` (§4.4). | `ClosureResultOriginTest`, `ClosureEnvironmentOriginTest`, `BlockClosureInferenceTest` and publication assertions: every result, source and mask represented; valid/invalid pairs; repeat/edit lifecycle; unchanged acceptance, codes and spans. |
| S3-4a | First consumer: caller result substitution, slot counts, Types, readers and C2 retention with 2c-3, then 2c-5's OQ4 gate transfer. Remove only replaced scans, the closure meet rewrite if S3-3 agreed (§4.4) and temporary environment carrying. Verify shadow MIR imports before closure-result checking. | `RetentionTest`, `MirBorrowsTest` (planned `.Closures` partial), `ClosureReceiverClassificationTest`, `GenericClosureIdentityTest`, `MirBuildTest`, `MirValidatorTest`: parameter/receiver Value/Content, consuming transfers, external referent independence, erasure. Default-pipeline acceptance, code and span unchanged under OQ4/OQ11. |
| S3-4b | BU 0c (2c-4) from 2c-3's pushed branch: remove owner-or-uniq pair self-store rows; same G:A5-F7 dev merge (AB-2). | Pair-case acceptance in both views; A3-closure still rejected at `v@follow = x`; `RetentionTest`, census, ledger. Neither side merges alone before the group review. |
| S3-5 | Activate F4 closure-result relations (2c-6) after 2a-1b, 2c-2 and 2c-3; AB-2 group completion. `CallLocalOrigin` stays until H11. | Planned `MirOriginsTest` and closure refutation theories: identity, masks, finite-to-result Refuted, independent fixed equality Unknown, check-only and unreachable sources, no obligation proving itself. Record twins and merge behavior before any default-pipeline promotion. |
| S3-6 | Independent final design/diff review, measurements and support-boundary records; H11 later deletes the old-path twins; no early default-pipeline switch. | Focused Unit and final Session, census/ledger, allocation/scaling, exact source evidence; no unexplained rejection loss, library/milestone regression or unapproved representation. |

Units serialize builds and checks per worktree, commit only their paths, associate `verify-commit.ps1` evidence and push. A final
Session over unchanged unit inputs replaces the Unit run.

## 7. Validation matrix and acceptance

- **Imported identity:** one outer atom classified differently in outer and inner bodies; three nested closures; sibling closures
  with equal Origin names but different captures; two generic contexts; capture-entry borrow versus borrowed captured content.
- **Result positions:** outer Origin, nested Origin arguments, tuple/struct/array positions, returned closure, nested Function
  binder; inferred versus explicit result and fixed expected signature; covariance, invariance, mixed; result-only universal names.
- **Sources:** all returns, conditional joins, reassignment through a local region, unreachable return, consuming environment
  borrow, receiver-owned borrow, copied shared reference, exclusive reborrow, moved captured reference, borrowed parameter, raw source.
- **Aliases and premises:** equality order independence; a local equality never proves itself; inner premises never leak outward;
  named-result R3 stays separate and gains no body-result obligation; pair-slot premises only in their admitted cases (SETTLED).
- **Retention:** per argument despite equal Origins; receiver writable storage and the Content/Value distinction; no Loan identity
  inferred from Origin identity; replacement and moves keep external Loans without retaining a consumed environment needlessly.
- **Lifecycle and performance:** warm unchanged Bind/analysis publication and retention; edits to valid, invalid and incomplete
  closures; no stale references after invalidation; N captures × M calls, depth D nesting, R result positions, K case masks.
- **Production correspondence:** normal source → HIR contract → MIR Types and rows → F4 → C2/F7 result; model-only graphs are
  insufficient. Malformed publication is tested through the producer or validator, never a forgiving consumer.

Test owners are the per-unit lists in §6; reuse their helpers and add no duplicate cases.

Compiler units run `scripts/verify.ps1 -Class <affected owners>` with the census/diff rows and end with `-Mode Session`; MIR
builder merges add the sel check; native fixtures and milestones only by explicit selection; no NativeAOT. Timing-only work goes
to `src/Benchmark`, evidence to `artifacts/benchmarks`.

Acceptance: source-backed gate R, one canonical publisher, no syntax-based Origin decision in MIR, no unverified support claim, no
open blocking finding and measured acceptable cost. Owner approval is still needed for the combined F4/C2 package (R4-M74 OQ2),
for any unavoidable vocabulary and for any further migration exception; the approved OQ4 gate needs none. Record first-implementation
decisions in DECISIONS. No SPEC or SETTLED revision serves this representation.

Authorities: [SPEC](../../docs/SPEC.md) §15.3.6, §15.6.5 and §15.8.2; [A5 step 2](2026-10-18%20A5%20Step%202%20Implementation%20Plan.md)
2a0-D, 2c-D, 2c-2–6, AB-2 and H11; [DECISIONS](../../docs/dev/DECISIONS.md) R4-M74 OQ2/OQ4/OQ11 and R5-M63;
[SETTLED](../../docs/SETTLED.md); [VERIFICATION](../../docs/dev/VERIFICATION.md).

## 8. Review record

Three options were independently authored, one agent per option, and reviewed independently in two correction rounds; the
[review record](../Sketches/A5%20Step%202/S3-M69/Comparison%20and%20Review.md) lists the findings and corrections.

### Revision (2026-10-20)

The owner asked for the plan to be scrutinized against the Principles. Claims were checked at `338831f7`:

| Finding | Principle / rule | Correction |
| --- | --- | --- |
| Legacy `CallLocalOrigin` reads `symbol.Type` after the meet rewrite, so the rewrite could not go while it lives to H11. | One canonical form; Short | §4.4: an S3-3 shadow comparison decides whether S3-4a feeds it from FiniteBody rows and deletes the rewrite, else H11. |
| Formal identity "reused" the Input-slot allocator, which serves only named functions, accessors and clause-quantified results. | Explicit Semantics; Record decisions | §4.1: a recorded responsibility extension; C2-owned storage until S3-4a. |
| S3-0 "required" 2x-P7, which has not run. | Local Reasoning (exact gates) | S3-0 takes 2x-P7's cases as inputs; 2x-P7 stays 2c-D's prerequisite. |
| Gate R lacked per-row premise scopes, trial fits, direct local checks, shortcuts, unresolved meets, receiver-before-result timing and position correspondence (adopted S3-0 direction). | Local Reasoning | Gate R rows extended; the premise-scope question is answered by gate R evidence, and becomes a 2a0-D/2c-D input only if no published fact decides it. |
| Environment-binding borrows were not classified per receiver case (SPEC §15.6.5). | Explicit Semantics | §4.1 maps them to the four classes. |
| Wrong line numbers and sources; `TyCtxt.Origins` treated as existing; `RecordLocalRegionBound`, `CommonOriginType`, `BlockClosureInferenceTest` and the planned `.Closures` partial missing. | Verifiable Changes | §2, §3 and §6 corrected. |
| Unnumbered sections, scattered deletions, a duplicate owner list. | Consistent; Short | Numbered sections; one deletion table; owners only in §6. |

No compiler, build, test or model ran for the revision. The selection, the units' order and the holds are unchanged.

Independent reviewer `review_s3_revision` (author of neither) checked the anchors and probed §3's example on a clean `338831f7`
build; round 1's two blocking findings (the legacy-rejection schedule, a premise-scope choice by fiat) were rewritten as above, and
round 2 accepted with wording points, applied.
