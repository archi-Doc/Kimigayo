# S3 Closure Origin Publication Plan

Selected planning direction: option 3, one body-relative Origin contract. Intake only; not implementation approval.
Base: `f373cd678ea3b8f6e4065c23b08029d17dea06e8` (2026-10-20). Two correction rounds after independent review;
round 2 only corrects citations and clarifies existing lifecycle/verification obligations.
This S3 is the closure publication gap in 2a0-D/C2, not the earlier S3 wording about inference without a principal candidate.
The investigation below is source inspection only. No build, test, benchmark or compiler reproducer was executed for this candidate.

## Required meaning and root cause

SPEC 15.6.5 makes fixedness relative to the checked body. A captured outer local Origin can be an inferred/finite Origin outside and
fixed inside; this does not change its identity, erase its outer bound, or merge Loans. SPEC 15.8.2 additionally requires each omitted
result Origin to belong to a per-call contract and checks every returned source against that contract, including unreachable sources.

The required meaning -> representation gap -> owner -> deletion chain is:

1. A body consumes a contract whose captured/input/receiver Origins are fixed -> `BoundOrigin.Kind` alone cannot answer this ->
   Binding publishes one body-relative classification through C1-F4; `MirOrigins` consumes it -> no closure test in the solver.
2. A result inferred from its sources is a distinct outward contract, not the source's own local Origin -> `BoundClosure.Signature`
   currently reuses the inferred result Type and its source Origins -> C2 publishes result slots and their source relations -> remove
   first-local-origin rejection and capture scanning once the common relation checker and call consumers replace them.
3. Aliases and inference bounds must survive publication without becoming assumptions -> private declaration replacement maps and
   local bounds do not constitute a published proof contract -> Binding owns resolution and publishes aliases/obligations separately
   from R1-R4 premises -> consumers neither inspect syntax nor repeat Binding inference to explain a failure.

This is a missing shared contract, not evidence that any particular currently supported program is accepted unsoundly.
`Unsupported_Kd` gates and the old closure-local rejection currently hide parts of the gap.

## Inspected cause sites and affected owners

Paths below are repository-relative; line numbers name this base, not future patch positions.

| Source | Observed fact / consequence |
| --- | --- |
| `Binding/Binding.OriginProof.cs:29 FixedOrigin`, `:118 JudgeOriginAtoms` | Static/Parameter/Input and meets alone count as fixed; no checked-body parameter. |
| `Binding/Binding.Diagnostics.cs:146 RefutesOriginRelation` | BodyLocalOrigin + global FixedOrigin; cannot be the new body-relative classifier. |
| `Binding/Binding.OriginDeclarations.cs:446 ResolveOrigin`, `:472 CompleteOriginDeclaration` | Equalities rewrite private `Replacements`; established declaration aliases differ from obligations being judged. |
| `Binding/Binding.OriginDeclarations.cs:735 ResolveInitializerOrigins` | Replacement bounds are recorded before inference obligations are compacted; capture them at this owner, not by reconstructing syntax later. |
| `Binding/Binding.LocalRegions.cs:280 ResolveLocalResultOrigins` | Recursively replaces local regions with meets of `LocalRegionSources`, irrespective of position variance. It is not a complete C2 result-contract algorithm. |
| `Binding/Binding.ControlFlow.cs:178 BeginResult`, `:241 FinishResult`, `:291 DecideResult`, `:350 ResultContext` | Existing result collection retains source Types and `SourceNodes`, including explicit transfer operands. Reuse this collection. |
| `Binding/Binding.Closures.cs:107 CallLocalOrigin`, `:479 CallResultOrigin`, `:516 BindConcreteClosure` | One recursively found local origin is rejected; CallResultSlot is a diagnostic marker, not a per-position signature. |
| `Binding/BoundClosure.cs:19` | Captures, Signature, EnvironmentType, minimum Receiver; no body-relative Origin facts or result source rows. |
| `Binding/Binding.FunctionTypes.cs:294 ResultFixedForCall` | Separately scans every result Origin and capture for Copy/external dependence. |
| `Binding/Binding.ValueCalls.cs:248 FixedInBodyOrigin`, `:531` | Different fixedness question; receiver-dependent/environment results are Unsupported. Do not mechanically merge different call-site and body questions. |
| `Binding/Binding.OriginPremises.cs:146 ExtractOriginPremises` | R1-R4 contextual catalog plus R5; excludes local/Field clauses from premises and anonymous results from named-result R3. Preserve these boundaries. |
| `Binding/Binding.Origins.cs:31 InputOriginCount`, `:639` and `OriginDeclarations.cs:598` | Existing binder-indexed aggregate Input atoms include result-only slots with InputIndex = -1. Reuse their identity mechanism for result formals. |
| `Binding/Binding.ClosureInstances.cs:12 ClosureSignature`; `TyCtxt.Substitutions` | Closure context is canonical Subst (R5-M36); new views must instantiate through this same path. |
| `Binding/TyCtxt.Retention.cs`, `Binding/Binding.Retention.cs:103 RetentionOf` | Retention consumes declaration premises, not caller-solved Origins, and is per argument; C2 must preserve this invariant. |
| `Mir/Build/MirBuilder.cs:214` | Closure receiver Type currently uses one BoundClosure.Receiver; a borrowed receiver has no explicit receiver Origin. |
| `Mir/Analysis/MirBorrows.Calls.cs:23 Call` | Temporary environment-to-result carrying exists until F7; it is not a substitute for C2 retention. |
| `Mir/MirTypes.cs:Agree`, `MirValidator.Types.cs` | Type agreement ignores Origins; neither may infer missing contracts or become another Origin validator. |

`MirOrigins`, `TyCtxt.OriginObligations`, `OriginAtom.Of`, `UniversalRelations.For`, and `TyCtxt.Closure` do not exist at this base.
They are planned F4/C2 owners, not APIs this proposal can assume already work. CODEMAP's Binding/MIR/analysis rows identify test owners.

## Concrete sources and observation status

Existing regression sources, inspected but not run here:

```kimi
// ClosureResultOriginTest.AResultOverTheEnvironmentIsUnsupported: current expected Unsupported at f().
let n = 7
let f = func [n] () => n@ref
let r = f()
```

```kimi
// ClosureEnvironmentOriginTest.AResultOverCallLocalStorageIsRefuted: current expected Unsatisfied at x@ref.
public func main() -> ()
    let f = func (x: i32) => x@ref
    let r = f(1)
```

```kimi
// Existing ClosureResultOriginTest cases accept the result and preserve its external Loan.
var n = 7
let values: [1 of ref/i32] = [n@ref]
let f = func [values] () => values[0]
let result = f()
```

New probe, not executed, with intended semantic expectation rather than a claimed current verdict:

```kimi
public func main() -> ()
    let n = 7
    let p = n@ref
    let f = func [p] ()
        var r = p
        r = p
        return r
    let result = f()
```

Required inspection: `p`'s Origin identity is shared at capture, fixed in `f`, and retains its finite outer bound in `main`;
`r` has its own inference node; both assignments and return remain relation rows; the result's public contract cannot name `r`.
A variant returning a reference to a local `let m = 8` inside `f` must be Refuted. A join of `p` and another captured reference
must meet at covariant positions; an invariant aggregate result must prove equality, not reuse the meet-only helper.

## Chosen representation

Use the already planned C1-F4 view, not an additional closure-analysis database. Its definition key includes the checked body;
its instantiated key also includes the existing closure Subst and premise scope where those affect the contract.

The concrete published columns are:

- **Atoms:** canonical BoundOrigin identity plus body-relative class (Fixed, FiniteBody, FiniteStatic, Inferred), bound and case mask.
  C2 publishes which existing capture atoms and the receiver contract are imported; the common publisher classifies them once.
  Meets retain interned operand identity. A captured atom is not globally relabelled and never copied just to mark it fixed.
- **Premises:** immutable R1-R4 contract relations, case-conditioned, resolved by the existing catalog's canonical resolver.
  R5 stays the ordinary local-Place rule and cannot turn a local annotation into its own assumption.
- **Relations to check:** the planned F4 tuple (ends, Equality, Source, Clause, destination Type, CaseMask), including return sources,
  call obligations and unresolved local alias/bound relations. This list is disjoint in meaning from the premise list.
- **C2 result slots:** one formal for each actual inferred signature Origin position, with canonical slot identity and composed
  variance; the result Type contains these formals. Repeated positions share only when the contract makes them equal.
  Source-site rows remain separate even if their Origin terms agree. No per-call persistent source list or per-capture identity map.

Reuse the existing aggregate Input-slot storage/identity (`InputIndex = -1`) after a behavior-preserving extraction of its allocator.
Do not reuse CallResultSlot for all result positions; it can remain only the old diagnostic projection until H11 removes it.
A new vocabulary entry is unnecessary if the existing Input contract admits this use; its responsibility extension still needs
DECISIONS and the F4/C2 owner-approved package. If code investigation disproves that contract, stop and redesign the formal identity.

### Result formation: the hard boundary

The following is the selected algorithm to prototype, not a claim that current Binding already implements it:

1. After structural result selection, reuse `ResultContext.SourceNodes/Sources`; do not resurvey the AST. Enumerate all Origin
   positions with the existing component/slot variance rules. A nested Function Type's own binders do not become this closure's slots.
2. For each slot, derive its contract-side source expression from the closure's inputs, captures and receiver under SPEC 15.8.2's
   five rows. Preserve the full rows at the existing Binding obligation producer before local-region projection or compaction,
   then use common scoped inference over those rows. `LocalRegionSources` alone is not that representation: `RecordRegionBound`
   drops the condition/equality and deduplicates sources/evidence. Do not reconstruct this information by walking closure syntax.
   A source atom belonging to outer storage is imported/fixed here; an atom of this body's storage remains finite. All sources,
   not merely the first return, contribute. The graph walk must terminate on cycles by node identity.
3. At a covariant slot use the meet of admissible boundary sources. At an invariant slot require equality among their boundary
   Origins through the common proof owner; retain the equalities as obligations. Contravariant and mixed positions use SPEC 15.3.6's
   ordinary principal-candidate rules; mixed variance is invariant. Do not add a second closure-only inference solver.
4. Form a result formal and its contract bindings only from those boundary expressions. A receiver-owned borrow uses the receiver
   Origin; a copied captured shared reference uses its external Origin; an exclusive reborrow includes both external and receiver
   Origins where the SPEC requires them; a reference moved out of a consuming environment keeps its referent's external Origin.
5. Independently publish every concrete returned source -> formal relation (direction/equality by variance). A finite source is never
   admitted as a premise bounding the result. Its relation reaches the outside result boundary and is Refuted by the ordinary solver.
   An unbounded raw-derived source remains inference with no finite bound; it is not relabelled static to fabricate a candidate.
6. Publish a contract binding only after its boundary expression is formed independently of source-fit judgment. Gate R must specify
   its exact body-side equation and premise scope as well as the caller substitution; calling it a consumer guarantee is insufficient.
   Source-fit rows and local equalities being checked never enter that premise set. Failed invariant formation does not publish a
   valid-looking subset.

For the `p -> r -> result` probe, the intended contract binds the result formal to the independently inferred boundary expression `p`,
while preserving `p -> r -> resultFormal` as body obligations. Gate R must show how the fixed body target can use that binding without
assuming the very source-fit relation being checked. For `m@ref -> r -> result`, the finite node cannot enter the contract's boundary
source set; the obligation chain still reaches the fixed result boundary and fails. Two different fixed inputs at an invariant result
remain an unproved equality unless an independent declaration premise proves it.

**Preimplementation gate R:** produce a source-backed prototype and a reviewed contract answering every row below. This is a
blocking design/proof unit, not permission to implement later units while the details remain undecided.

| Required proof | Evidence that closes it |
| --- | --- |
| Lossless input and one solver | Name the common scoped-inference entry point and full row owner; retain condition, equality, every source/evidence and composed variance before local-region deduplication. `ResolveLocalResultOrigins` and `LocalRegionSources` alone fail this requirement. |
| Fixed result target | For captured `p -> r -> result`, state the body-side contract-binding equation, formation phase, permitted premise scope and caller image; independently check every source. Pair with finite `m@ref -> r -> result` refutation. |
| No-bound raw result | State the target identity and caller image when no finite/boundary candidate exists; do not erase the target, create a top-like anchor, fabricate static, or falsely classify the source finite. |
| Complete inference | Covariant, invariant, contravariant and mixed positions; joins/reassignments; cycles remain inference variables rather than invented fixed equalities; nested Function binders and returned closures stay distinct. |
| Cases and receiver | Borrowed/consuming receivers, captured shared/exclusive values and all admitted masks; never widen a case-specific premise. |
| Publication timing | Capture all ResultContext sources before reset; preserve failed definitions under R5-M63; form the contract before dependent call binding; define stable structural slot keys for result-only Input atoms and audit all readers. |

Each witness records source/configuration hash, Type/slot identities, atom classes in both bodies, row source and mask, permitted
premises/binding, caller substitution, and expected/actual verdict and location. Ordinary input supplies the facts; synthetic graphs
can test the solver but cannot prove this correspondence. No build or probe has passed this gate in the present documentation task.
If no single common inference representation proves these obligations, hold S3 and return the counterexample for shared redesign.
Do not add a closure-only solver, shape allowance or fallback. This is the main uncertainty of the selected direction.

### Ownership and lifecycle

Binding's C2 contract producer owns captures, receiver per case, result slot formation and source classification.
The common `TyCtxt.Origins` publication owns definition-relative atoms, resolved rows and premise scope; the planned `MirOrigins`
owns relation judgment. C2's `TyCtxt.Closure` owns call retention over receiver plus parameters, derived from that same contract.
`MirBuilder` imports the receiver/result Types through C2; it does not inspect capture syntax or rebuild them.

Publication-only staging uses the planned C2 view with no production reader. Keep the active `BoundClosure.Signature`, capture/body
Types and existing call path coherent until the first consuming unit. This temporary view has one removal condition: promotion of
the validated C2 contract and retirement of its replaced legacy readers. Do not publish fresh result formals into the active signature
early: `ValueCalls` sizes slots from current declarations and rejects surviving Input/Parameter atoms, while substitution can leave
out-of-range slots unchanged. Audit `Calls`, `ValueCalls`, `CallableContracts`, `ContractMatching`, `ResultPremises`, `OriginInference`,
`Storage` and result-slot enumeration. Promote result Types, slot counts, substitution and call readers together; if that seam cannot
preserve existing acceptance, combine the first-consuming changes into one verified unit and re-estimate it. Verify that legacy
`CallLocalOrigin` still receives original source facts (`symbol.Type` at this base) until H11; promotion to fixed formals must not
erase the finite-source evidence that its rejection needs.

Publication occurs only after the producing Binding result is complete enough to provide the full contract. A rejected creation
still exposes the resolved body under R5-M63; unavailable Origin facts keep the located Unsupported boundary, not an invented contract.
The producer validates its new columns; MirValidator remains the single validator of MIR and validates cross-column identity only.
A missing fact in an otherwise promised valid contract is InternalInvariant_Kd, not a recovery path.

Use `TyCtxt.ResetViews`/Binding invalidation for cached immutable contracts; share reusable backing storage where a pass recomputes
rows. Newly published/live views and scratch ranges must not retain stale syntax, scopes, captures or substitutions after an edit;
existing immutable Type/Subst interners retain their established lifetime. Warm rebind must not allocate merely to republish equal facts.
F4's pass-specific obligation projection follows its planned pass reset, while invariant C2/retention memo entries last until edit;
these are different lifetimes, not two copies of a result contract.

## Alternatives and cost

Three options were independently authored, one agent per option, then reviewed independently:

| Option | Assessment | Decision |
| --- | --- | --- |
| [1: owner-local queries](../Sketches/A5%20Step%202/S3-M69/S3%20Owner%20Local%20Design.md) | Smallest partial repair, but cannot publish a complete result/receiver contract without leaving its scope. Its own plan stops at that boundary. | Reject as a complete S3 solution; retain useful census work. |
| [2: inner binders](../Sketches/A5%20Step%202/S3-M69/S3%20Inner%20Binder%20Design.md) | Explicit boundary through fresh imports and substitution, but adds inner Types and maps for captures; still owes the result-formation proof. | Coherent alternative, with wider representation and lifecycle cost. |
| 3: body-relative common view | Preserve imported identities; create only required result formals; one C1-F4/C2 publication and ordinary relation checking. | Select subject to gate R and package approval. |

Option 3 best matches one canonical form and local published contracts, and avoids option 2's import/Type cloning. That is a structural
cost judgment, not a measured speed claim. The [review record](../Sketches/A5%20Step%202/S3-M69/Comparison%20and%20Review.md) tracks
findings and corrections. Gate R can invalidate this choice; selection does not certify the missing inference algorithm.

Estimated production delta for this S3 slice, not all F4/F7: +250 to +420 / -120 to -210 lines at completion; early publication
units may be net positive. Tests +250 to +400. Behavior-preserving moves are approximately net zero. These are comparison hypotheses,
not repository size caps. Re-estimate after gate R and count actual deletions per area in each implementation commit.
The largest immediate deletions are ResultFixedForCall's capture scan and the meet-only closure result rewrite when their readers move.
CallLocalOrigin/LocalBorrow and its diagnostic marker stay until H11; the environment-to-result blanket remains until 2c-3 atomically
installs C2 retention. No speculative deletion of general Function Type fixedness rules.

Expected time: publication linear in distinct atoms + relations + result occurrences; local-bound walking memoized per body and
mask class, not per result/call. Retention stays per contract instantiation, not per invocation. F4 retains its planned bound
Theta(N_o + E_o * ceil(F/64) * rho + F_t * P_m); result formal nodes increase F and must be counted.
Expected allocation: cold immutable result contract storage; warm 0 B for unchanged publication/retention. Retained memory O(atoms +
relations + result positions + existing F4 bitsets), with no outer/inner clone product. These are unmeasured hypotheses.
Measure equivalent workloads under fixed compiler/configuration and record time, allocation and retained bytes. Any increased retained
memory must buy simpler code or demonstrated speed; no relaxed assertion or extra warm-up to pass.

## Small, verifiable implementation units

All are future work after design selection/review and required owner approval. Existing F4/C2 units are refined, not duplicated.

| Unit | Change and prerequisites | Checks and exit |
| --- | --- | --- |
| S3-0 | Reproducer/census and gate R prototype; requires 2x-P4, 2x-P7/corresponding evidence and actual current F4/C2 head. Map every cause site above and baseline errors/rows. | Ordinary-source captures, joins, invariant results, raw borrow, cycles; record actual Types, atom identities, source rows, case masks and verdicts. No unidentified result-source path; gate R either proven or explicitly blocks implementation. |
| S3-1 | Restructure first, behavior unchanged: move closure contract formation out of `Binding.Closures`, separate any origin-slot allocator/result-origin concern before behavior edits. Respect planned 2c-0 and 2x-R11; no mechanical size evasion. | Relevant closure/result/Origin tests plus semantic and diagnostic snapshots on same inputs; no verdict, row or Type identity change. Review hotspot history before next edit. |
| S3-2 | Implement common body-relative atom publication and alias/obligation separation in 2a0-2. No production consumer switch yet. | OriginPremiseCatalogTest, OriginPremiseLifecycleTest, DeclaredOriginSlotTest, LocalRegionCompositionTest; published identities/masks and unchanged old path. No alias consumed as its own proof. |
| S3-3 | Implement reviewed result formation, capture/receiver imports and C2 retention (2c-2) as validated publication-only data. Keep the active signature/call path unchanged. | ClosureResultOriginTest/ClosureEnvironmentOriginTest and publication assertions; every result/source/mask represented; valid/invalid counterparts, repeat/edit lifecycle and unchanged existing acceptance as well as rejection codes/spans. |
| S3-4a | First-consumer promotion: coordinate caller result substitution, slot counts, Types/readers and C2 retention with 2c-3, then 2c-5's OQ4 gate transfer. Remove only replaced scans and temporary environment carrying. Verify shadow MIR imports before activating closure-result checking. | RetentionTest, MirBorrowsTest.Closures, ClosureReceiverClassificationTest, GenericClosureIdentityTest, MirBuildTest/MirValidatorTest; parameter/receiver Value/Content, consuming transfers, external referent independence and erasure. Existing default-pipeline acceptance/code/span stays unchanged under OQ4/OQ11. |
| S3-4b | BU 0c (2c-4) starts from 2c-3's pushed branch; remove owner-or-uniq pair self-store rows. AB-2 requires it in the same G:A5-F7 dev merge. | Pair-case acceptance rows in both views plus A3-closure still rejected at `v@follow = x`; RetentionTest, census and ledger. Neither side merges independently into dev/main before group review. |
| S3-5 | Activate F4 closure-result relations (2c-6), after 2a-1b, 2c-2 and 2c-3; preserve AB-2 group completion. Binding CallLocalOrigin stays until H11. | MirOriginsTest and closure refutation theories; identity, masks, finite-to-result Refuted, independent fixed equality Unknown, check-only/unreachable sources; no obligation proves itself. Record twins/merge behavior before any default-pipeline promotion. |
| S3-6 | Independent final design/diff review, measurements and support-boundary records. H11 later deletes named old-path twins; no early default-pipeline switch. | Focused unit verification and final Session, census/ledger, allocation/scaling, exact source evidence; no unexplained rejection loss, library/milestone regression or new representation without approval. |

Compile/test work must be serialized within its worktree. Each unit commits only its own paths after required checks, associates
`scripts/verify-commit.ps1` evidence and pushes. A final Session covering unchanged unit inputs replaces that Unit, not duplicates it.

## Required validation matrix and acceptance

- Imported identity: same outer atom, different classification in outer/inner bodies, three nested closures, sibling closures with
  different captured values but equal Origin names, two generic contexts, capture-entry borrow versus borrowed captured content.
- Result positions: outer Origin, nested Origin arguments, tuple/struct/array positions, returned closure, nested Function binder;
  inferred versus explicit result and fixed expected signature; covariance, invariance, mixed variance, result-only universal names.
- Sources: all returns, conditional joins, reassignment through a local region, unreachable return, consuming environment borrow,
  receiver-owned borrow, copied shared reference, exclusive reborrow, moved captured reference, borrowed parameter and raw source.
- Aliases/premises: equality order independence, local equality not proving itself, inner premises not leaking outward, independent
  named-result R3 kept, no body-result obligation added to R3; pair-slot premises valid only in their admitted cases (SETTLED).
- Retention: per argument despite equal Origins; receiver writable storage and Content/Value distinction; no Loan identity inferred
  from Origin identity; replacement and moves preserve external Loans while not unnecessarily retaining a consumed environment.
- Lifecycle/performance: warm unchanged Bind/analyze publication and retention, edit to valid/invalid/incomplete closure, no stale
  references after invalidation; N captures x M calls, depth D nested closures, R result positions and K distinct case masks.
- Production correspondence: assertions inspect normal source -> HIR contract -> MIR Types/rows -> F4 -> C2/F7 result; model-only
  graphs are insufficient. Test malformed publication through the producer/validator, not a forgiving consumer.

Existing owners: ClosureResultOriginTest, ClosureEnvironmentOriginTest, ClosureReceiverClassificationTest, GenericClosureIdentityTest,
OriginPremiseCatalogTest, OriginPremiseLifecycleTest, LocalRegionInferenceTest, LocalRegionCompositionTest, DeclaredOriginSlotTest,
RetentionTest, MirBuildTest/MirValidatorTest, MirBorrowsTest and the planned MirOriginsTest. Reuse helpers and avoid duplicate cases.

Future compiler units use `scripts/verify.ps1 -Class <affected owners>` plus the required census/diff rows; the final session runs
`-Mode Session`. MIR builder merges also perform the documented sel check. Native fixtures/milestones require explicit relevant
selection; no NativeAOT. Timing-only work belongs in `src/Benchmark`, evidence in `artifacts/benchmarks`.

Acceptance requires source-backed gate R, one canonical publisher, no syntax-based Origin decision in MIR, no unverified support
claim, no open blocking review findings, and measured acceptable cost. Proposal-only work needs diff/reference review and no tests.
Owner approval is still required for the combined F4/C2 design package (R4-M74 OQ2), new vocabulary if unavoidable, and any additional
migration exception; the already approved OQ4 legacy gate need not be reapproved. Record first-implementation decisions in DECISIONS.
Do not revise SPEC/SETTLED to make this representation work. This is the formal gated plan: S3-0 has a concrete evidence deliverable;
later units cannot start until gate R and applicable approvals pass. If gate R fails, hold those units and obtain a reviewed common
redesign rather than treating this selection as implementation authority.

Authorities: [SPEC](../../docs/SPEC.md) 15.3.6, 15.6.5 and 15.8.2;
[A5 step 2](2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) 2a0-D, 2c-D, 2c-2–6, AB-2 and H11;
[DECISIONS](../../docs/dev/DECISIONS.md) R4-M74 OQ2/OQ4/OQ11 and R5-M63;
[CODEMAP](../../docs/dev/CODEMAP.md), [SETTLED](../../docs/SETTLED.md) and
[verification](../../docs/dev/VERIFICATION.md). No language-rule revision or support claim is made.
