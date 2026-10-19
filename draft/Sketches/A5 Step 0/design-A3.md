# A5 design A3: Polonius-style borrow checking on MIR

Base: worktree `bin/worktrees/a5-step0` (`b2edea8b`); plan `fe1a5b34` §4.2–§4.3. Read-only; nothing was built or run.

- **Paths**: `M/` = `src/Kimi/Compiler/Mir/`, `B/` = `…/Binding/`, `A/` = `…/Analysis/`. `S15:n` is a SPEC line.
- **Sources**: "r1"/"r2" are the design reviews; "prototype" is `prototype.md` rounds 1–6 (92 cases).
- **Assumptions**: S1 is the plan's S1 (with its `replace` exemption) as corrected to S1′; S2–S5 are adopted. The plan's
  names `base`, `destroyed_in_place`, `ended`, `step` and `base_op` are used.
- **Owner decision 2026-10-17**: change the representation by adding the transit clause to the check set (r2 finding 1).
- (H) marks an unverified hypothesis.

## 1. Summary

- **Holders**: root locals plus an escaped pseudo-holder `E` (P1), each with an interned Loan-set node (`MirLoanSets`).
- **Loans**: one per borrow site and view, named by base (S1; none past a shared Deref, E3; no base, E8). A two-phase Loan is `ref` only in its reservation holder.
- **Syntactic access**: borrows and Moves of a Place reach Loans formed through references stored in it (P3; S1's ban
  over Places). There are no resolved access Places and no authority sets.
- **Check sets go by holder, never by Loan id.**
  - Accesses: the entry sets of holders live after P, minus those P overwrites, **plus the transit clause**: the entry
    sets of every value P stores into a holder live after P (directly or through the closure), or into `E`; a store
    through a reference stores into the holders its direct Loans name (owner decision; the plan's `stored`).
  - Activations: holders live into P, minus the reservation holder (E5: its holders still seed the closure). This also checks the arguments of one call against
    each other.
- **Kills and destructions**:
  - `step` carries the kill on every rule, through MIR's Temps. The kill never crosses a Deref (plan base: the Place
    before the last Deref; E8 ends by exact Move-Path prefix instead).
  - A call whose published row destroys its target makes a `destroy` access of it, checked before `ended`.
  - An observing destruction is an exclusive deep use up to the first shared Deref (`base_op`).
- **Provenance only for content**: direct Loans, materialized parents, `E` through `Φ_E`.
- **Passes**: backward holder liveness, forward Loan sets and one replay.
- **Origins**: A5 takes the old Analysis's role; Binding's fixed–fixed body judgments move at R7.
- **Size**: about 2,340 lines against 15,646 deleted. **Speed**: the owner restates V16 (§6.3).

## 2. OD13 loss table

Each row is a rule clause of §5 with its own fact (plan §5.3). Counts: lib, ms (43 milestones), tests (xUnit
literals, grep proxies). r1 spot-checked P1 (`Milestone33.kimi:70`, `Milestone16.kimi:36`) and found no rejection.
r2 finding 3's row is not needed, because the shared-layer cut is taken (§5).

| # | SPEC | Rule clause (simplified rule) | Rejects | lib / ms / tests | Class |
| --- | --- | --- | --- | --- | --- |
| P1 | §15.6 (S15:454 "Move Path granularity") | Holders are variables, temporaries and parameters. A write or Move of a part adds and ends nothing; reading a part reads the whole holder; an observing Drop uses the whole holder. | `a.0 = z@ref; x = 4; inspect(a)`; destructuring a local aggregate of several borrows; prototype 5b, 7, 18a | 0 / 0 / ≤10 tuple literals in 5 files | **Needed** (no subtree ranges, no G7 fix) |
| P3 | §15.6.2 "Inline parts exclude referents" (S15:500) | **Loans cover referents.** A Move or borrow of a Place reaches Loans formed through references stored in it; reads, writes and destructions do not. An access through `r` reaches Loans on `r`. | (a) `let z = r@ref` while a conflicting Loan formed through `r` is live; (b) `let z = r@ref; r@follow = 5; inspect(z)` | 0 / 0 / 3 rough hits; the old path rejects (b) (`OwnedStoredReferenceTest.cs:230-235`) | **Needed** (no access resolution or authority) |
| P4 | §15.6.3 Iterator descent, §15.6.4 step 4 | A result retaining a borrowed input's referent content retains every Loan that content holds; a Closure result retains all captured Loans. | An Iterator or Closure with two independently borrowed sources whose result names one, then a conflict on the other | 0 (no multi-slot struct in lib) / 0, corrected to 1: Milestone27:100's copy through a view of a temporary UniqSlice, whose holder carries its own Loan beside the elements' (re-spelled, owner, DECISIONS R4-M64) / small (H) | **Needed** (follows from P1) |
| P10 | §15.6 writes through references | Reads and writes through a reference reach every holder its carried direct Loans name, including holders of the references it was reborrowed through. | A Loan-carrying write through `r@follow@follow@uniq`, then a conflict on the intermediate holder | 0 (no `@follow =` in lib) / 0 (the ms writes store owned or Copy values) / small | **Needed** (layer-blind provenance) |
| P11 | §13.7.2, §15.6 | A compound assignment, increment or decrement writes through its two-phase reservation, so it ends none of the destination's earlier Loans. | A compound assignment to a Loan-capable destination, then a conflict with a Loan its old value carried | 0 (39 compound lines: integers, plus one raw pointer `slot += layout.1` at `DictionaryStorage.kimi:41`; raw pointers are not Loan-capable) / 0 (20 lines, all numeric) / 0 (H) | **Needed** by the writeback activation |
| P12 | §15.6.3 descent, §15.6.4 retention per argument | A result that descends from an abstract call's receiver keeps the receiver's parent Loans, as a Reborrow of the receiver does (`desc(c)`, F6; erratum, prototype round 9). | While the result is live and the receiver's holder is dead, an access that conflicts with that holder's Loan: `var r = h@uniq; let first = r.inner.take(); h.other = 5; return first` (36f), or `h.inner = src2` (rustc accepts both). A later `h.inner.take()` is rejected by S2 anyway (another receiver Place) | 0 (abstract descending calls reach their receiver through `self`: `Iteration.kimi:32, 59`) / 0 (`cursor.next()` is concrete) / 0 (grep proxy: abstract receivers in literals are parameters) | **Needed** (follows from P3); approved 2026-10-17 (§9 item 10) |
| P5 | §15.6.2 key bullet, §22.4.4 | All keys of one static Field are one root. | A live Loan on `Cache<i32>.S.x` against an access to `Cache<string>.S.x` | 0 / 0 (Milestone42: whole reads and writes only) / 0 | Optional (no key Substs, no open-key composition in A4) |
| P6 | §15.6.4 anchors, §15.6.5, §11.8 | A Borrow of mutable static storage is finite, bounded by the body; a value carrying it never leaves the body. | §15.6.4's `current` becomes an error; `leak`, `f = current` and `run` keep their verdicts; §11.8 is unchanged | 0 / 0 / 0 | Optional (no A4 per-result anchors; dissolves c2) |
| P9 | §7.2.3, §15.6.4 | A call of a default template (recursive default SCCs only) retains every prepared input into its result. | A template default holding references that keeps no input, with a reserved input accessed while the callee's result lives | 0 / 0 / corpus ≤6 (H) | Optional (no inter-body `default_retains`) |

**S1′'s own losses** (listed with the plan, not A3's):

| Form | Rejects | lib / ms / tests |
| --- | --- | --- |
| Base operations | The Move, `exchange`/`swap` target, or exclusive borrow other than as a `replace` target, of a base or ancestor while a child is live (prototype 9k, 9l, 14) | counted by the census (plan §6.2) |
| No kill across a Deref (`within`; 23a, 30d; recovered by E8, owner 2026-10-17) | Replacing the outer holder does not end a child based past a borrow Deref, so later access through the new value conflicts:<br>23a: `c = rr@follow@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7`;<br>30d: a `uniq/Node`-linked traversal that reads through the new node's link.<br>obj links (30a: an own step, so B = `node`) and plain `uniq` links (30b) stay valid. | 23a: 0 / 0 (Milestone33's two-layer follows are compound writes) / ≤8 reborrows in 3 files.<br>30d: 0 (lib links are `isize` indexes, `DictionaryStorage.kimi:21`, `Storage.kimi:50-68`) / 0 / 3 files with reference links, 0 traversal lines. |
| Optional recovery: root-most kill | Sound with P3 (prototype, 92 cases); see below; superseded by E8 | removes the row above |
| Raw-site form | audit-core row 24 | (H) 0 / 0 / small |

**Optional precision recovery for 23a (owner decision; superseded by E8, the case 23a proposal's option (c)).** In prototype round 6 (92 cases), the root-most kill was sound
in both modes once P3 was present (variants `P3RootMost`, and A with `KillAcrossDeref`). That kill applies when the
holder before the first borrow Deref is destroyed in place. P3 rejects the crossing counterexample (29a) at
`let w = q@follow@ref`, and the root-most kill accepts 23a and 30d. Rules 2 and 3 of plan §4.3's crossing list changed no
case (H). A3 keeps the plan's `within` kill, for consistency with the plan and S1. Adopting the root-most kill removes
the 23a row, provided the plan and S1 change with it.

**Soundness.** P1, P4, P9, P10, P11 and P12 enlarge carried sets or keep Loans the SPEC ends. `content` and `refs` are
monotone in the carried set (`Φ_E`, r1 finding 4), so these losses only add errors. P3 adds conflicts and is what makes
syntactic comparison sound (§3). P5 enlarges overlap. Under P6, the Origin stratum Refutes every chain from such a borrow
to a fixed Origin, so no call result carries an anchor.

## 3. Holders and references

**Holders.** A holder is a root local whose case Type may hold a Loan (`MirTypes.HoldsLoans`: a borrow layer, pair layer,
Slice, Function or Closure Type at any inline depth; raw pointers excluded), plus `E`. Flow holders are roots whose
`MirMoveData` root path is a flow path (`M/Analysis/MirMoveData.cs:64`); chain-local holders live in the scratch, as in
A1.

**Steps and overlap** (B's classes, compared on `MirBody.Projections` with no A5 interning; DEC:583):

- **Deref classes**:
  - **borrow**: `ref`, `uniq`, `objref`, `objuniq`, or a PairLayer the view makes a reference. It is *shared* for
    `ref`, `objref` and a ref-case layer.
  - **own**: an `obj`/`rc`/`arc` payload, part of the handle's storage. This gives SPEC 13.5.5's handle Loan and
    S16:194.
  - **raw**: a fresh anchor, compared only with Places derived from it (S15:496).
- **Inline steps**: Field, Base, ConstIndex, Downcast, Index (E6: a Slice handle's Index is a shared borrow Deref).
- **Overlap** is prefix comparison:
  - distinct Fields, Tuple elements and constant indices are disjoint;
  - an Index meets every index of its base;
  - distinct Downcasts are disjoint. This is sound with transit: a variant changes under a live payload Loan only by a
    whole write, which meets the Loan even in the stored value, or by a formation the deep ban rejects (r2 finding 8);
  - distinct roots are disjoint, references included (row 8; sound under P3 and the caller's activation rule, S15:614).

**Loans.**

- A Loan exists per non-raw `Ref` site and view (none past a shared Deref: E3), with its Place, its `base(L, B)` (E8: none), mode, issue location and destination
  root.
- (E8) B is the plan's base: the Place before the *last* borrow Deref, with own (`obj`/`rc`/`arc` payload) Derefs counted as
  storage, as design B does. B matters only for the kill; the ban and provenance use the Loan's whole Place and direct
  Loans, so per-root holders need no other base.
- **Direct** Loans have no borrow Deref and name `root(place)`.
- A `TwoPhaseUniq` Loan's *reservation holder* is its issue destination; the Loan is `ref` only there.

**Ban and kill** (plan fe1a5b34):

- **Ban more.** A deep access of Place K reaches every Loan whose Place has K as a prefix, compared syntactically across
  Deref steps; an Index matches any index that §15.6.2 does not prove distinct. This contains `runs_through(B, K)` for
  any B ⊑ place(L), so `exchange(arr[j]@uniq, …)` meets a child based at `arr[i]` (22a).
- **Kill less** (E8 replaces it). The plan's `within`: `ended` needs a whole in-place destruction of B or of a move-path ancestor of B,
  with no borrow Deref step between them and Index steps only for identical constant indexes. Own (`obj`/`rc`/`arc`
  payload) Derefs are storage, so the kill passes them; this is sound, because the handle's storage is destroyed with
  its owner (r3 N2), and the plan's `within` should say "borrow Deref" too. A B containing a borrow Deref is never
  ended. A write through a non-path index ends nothing.
- **P1 changes neither rule.** Both are on Places; P1 only decides which holder carries what.
- **No kill across a Deref** (planning decision, overriding r2 finding 4; superseded by E8). The plan's (H) "does `q = …` end a Loan based
  at `(*q).p`?" is answered **no**, for consistency with the plan and S1.
  - Without P3, crossing is unsound (29a, rejected by SPEC and Rust E0502).
  - P3 rejects 29a at `let w = q@follow@ref` under either kill, so the root-most kill stays an optional recovery (§2).
  - 23a and 30d become S1 losses.
  - S1′ is unsound on 29a without P3, and on 30c through its exclusion by Loan id; A3 rejects both. S1 needs P3 and the own-site (holder-based) check (§9; r3 N3).

**Markers**, which never conflict:

- `Φ_E` ("may refer to storage the body does not own") is carried by every Loan-capable value of unknown provenance:
  Args at entry, Opaque values, static and raw-pointer reads, unknown callees' results, and results with an untied
  Origin (`Retains.Untied`).
- `τ_s` marks Opaque site s, for A6.
- `γ_H` marks a guard candidate's guard-local Origin (E7).

A *phantom* `Φ_c` does conflict (F6).

**Content** of a carried set S: the nodes of the holders S's direct Loans name, plus `σ(E)` if `Φ_E ∈ S`. Child Loans are
skipped, because a reborrow's holder carries its parents (prototype R2).

**Why syntactic access is sound.** Under S1′ + P3, an alias of a referent is one of:

- a direct Place, which meets the carried parent Loan;
- the same base path;
- one formed by a borrow or Move of a prefix, which conflicts at formation;
- a parameter, disjoint by caller activation.

rc and arc payloads are read-only (S15:509). The argument held on all 92 cases.

**Liveness through references** is a closure at check time: a live direct Loan naming T makes `σ(T)` live. Borrowing T is
a use of T, so no backward feedback is needed (4b, 17a, 17b).

**Rejected:**

- Origin positions: R4-M24 stalled on them;
- move-path holders: about 150 lines plus G7;
- dense holder × Loan bits: cubic;
- per-Loan traversal;
- R3 authority sets;
- exclusion by Loan id: unsound;
- B's descendant Loan: a loss against S15:517;
- separate destruction and store points: they miss the drop-free case.

## 4. Fact schema

Inputs come from producers; derived facts come from §5. One Semantics case view at a time (R4-M11).

| Relation | Keys | Producer | Kind |
| --- | --- | --- | --- |
| `edge(ℓ, ℓ′)`, P1/P2 | location | `MirDataflow` chains, `MirReach` | input |
| `issued(L, X, mode, ℓ, H)` | site | `Ref` rvalues under the view (`MirUses.ForEach(MirCases, …)`) | input |
| `base(L, B)` (E8: none), `direct(L)`, `named(L, T)` | Loan | Place projections, step classes | derived (pre-pass) |
| `holder(H)`, `flowHolder(H)` | local | `MirTypes.HoldsLoans(cases.Type(…))`, `MirMoveData.FlowCount` | input |
| `access(ℓ, X, k)`, `use(H, ℓ)`, `kill(H, ℓ)` | location | `MirUses` rows plus statement kind (as `MirInit.cs:112-121`) | input |
| `stores(ℓ, v, dest)` | location | an Assign or Call destination, a write through a reference, a store retention into referents | input |
| `destroys(ℓ, K, elements)` | Call | callee's published row `Destroys = i` (`TyCtxt.Published`): K = the Place of the Ref producing argument i; `elements` for the `clear`s | input |
| `destroyed_in_place(K, ℓ)` | Place | a whole assignment of direct K; StorageLive/StorageDead of root K; a `destroys` row that also has `InPlace` (`replace` only; `clear` only under relaxation (c)) | input |
| `destroying(site)` | Ref site | the TwoPhaseUniq Ref producing an `InPlace` argument | derived (pre-pass) |
| `ended(L, ℓ)`, `step(L, ℓ, ℓ′)` (E8) | Loan | `base(L, B), destroyed_in_place(K, ℓ), within(B, K)` (move paths: no Deref, Index only for identical constant indexes); `step :- edge, !ended` | derived |
| `base_op(ℓ, K)` | Place | a Move, a borrow (P3), an `exchange`/`swap` target, the destruction of an old value whose drop may reach a base below K (`obs`: MIR's separate Drop at `=`, a destroying call such as `replace`), except a `destroying` Ref | derived (deep accesses) |
| `reserved(L, H, ℓ1, ℓ2)` | site | TwoPhaseUniq issue into H at ℓ1; its Call, or the writeback write through H, at ℓ2; reserved only in H | derived |
| `observes(T)` | Type | `MirTypes.Observes(case Type)`; for a Drop also its group verdict ≠ Elide (`M/Analysis/MirDrops.cs:88`) | input |
| `retains(c, i, κ, o)`, `untied(c)` | (callee, Subst), slot | Binding `TyCtxt.Retains(callee, Subst)` (C1/C8), `TyCtxt.Closure` (C2); intrinsics through their declarations (E14) | input |
| `key(ℓ, f, k)`, `unknown(ℓ)` | Call or Drop | A4 `MirEffects.Keys` | input |
| `preserves(c)`, `recv(c)`, `abstract(c)` | call | `DispatchBounds`/`CallableBounds` (`B/Binding.EffectObligations.cs:83,98`); receiver Place; Requirement or abstract Callable callee | input |
| `σ`, `live`, `transit`, `CS`, `err` | holder, location | §5 | derived |
| `obligation`, `atom`, `entails`, `contain` | definition, Borrow | Binding `OriginObligations`, `OriginAtom.Of`, `UniversalRelations.For`; MIR Ref Places | input / derived (F4) |

### 4.1 MIR fact contracts (step 0)

| Gap | Contract | Change |
| --- | --- | --- |
| G1 | `MirDataflow.Backward` on the same chains (a P1 chain joins runtime successors only; P1 phases from `Begin`); one shared arena; a join hook (`IMirFlow.Join`; A1 keeps OR). | `MirDataflow.cs` +90, `MirInit.cs` +4 |
| G2 | `MirTypes.HoldsLoans`, `Observes` (the `Destruction` walk reports a user `drop`, an abstract part, a Function value or a non-Sealed handle) | `MirTypes.cs` +40 |
| G3 | **Plan §3 departure (DEC)**: no drop-row Path; A5 reads only verdicts, and Group −1 means check-only. | none |
| G4, G7 | **Closed by design** (DEC): statement kinds; no flow-closed paths under P1. | none |
| G5 | Every exclusive borrow producing an invocation's receiver or argument is `TwoPhaseUniq` (`MirBuilder.Values.cs:83, 175, 232`; `MirBuilder.Expansions.cs:396, 443`; `MirBuilder.Formatting.cs:70-87`). | builder |
| G6 | An invocation whose operand diverges continues in that operand's CheckTarget continuation; pending `EndBorrow`s go on the transfer's runtime edge only (`MirBuilder.Control.cs:135-142`, `MirBuilder.Calls.cs:116-120`). | builder |
| Writeback | Compound updates form `$w = TwoPhaseUniq(dest)` at locate and write through `$w` (S13:595, `MirBuilder.Expansions.cs:127-240`). TwoPhaseUniq (`M/MirVocabulary.cs:113`) activates at its Call or at that write; `EndBorrow` never activates. | builder, vocabulary doc |
| Slice | A range selection passes a borrow of its whole backing as `RawSlice`'s first operand (arity 2→3, `M/MirDescriptors.cs:146`), closing the unsound S4:570-575 acceptance. | builder, intrinsic row |
| Guard | `EndBorrow` of each `GuardCandidate` local at guard cleanup on every path (S14:325-336). | builder |
| C8 | A Value callee's `MirCallee.Arg` is its selected Callable signature; a Virtual callee's `Arg` is its slot (`MirBuilder.Calls.cs:131`). | builder, `MirBody.cs` doc |
| In place | `PublishedRow` gains `InPlace`; row `Target` splits into `Replace` (`Destroys: 0, InPlace`) and `Target` for the `clear`s (`B/KimiLibraryCatalog.cs:34, 78, 96, 157`); A4 keeps `Destroys`. | Binding catalog |
| E-keys | `MirOpaque` carries its Binding failure-cause keys (A6's representative cause is the minimum key). | Binding, builder |

`EndBorrow` (`M/MirVocabulary.cs:21`) is restated (E7 replaces the guard part): "a use of the local, after which every Loan issued into it must be
dead; its carrying ends". This is trivial for abandoned comparisons and reservations, and is the guard-local Origin end
for guards.

### 4.2 A4 publication

`MirEffects.Keys(body, view, block)` returns `(bool Unknown, ReadOnlySpan<(int Field, bool Write)>)` for a Call or Drop.

- It is A4's transitive union, a bitset per summary node over mutable static Fields.
- Under P5 there are no Substs or open keys (`MirBody.Static` is keyed by symbol, `M/MirBody.cs:441`); under P6 there
  are no per-result anchors.
- A4 reads nothing from A5.
- Without P5, `MirStatic` gains owner Type arguments, and A4 composes open keys (about +120 lines).

### 4.3 Binding views, each landing just before its family (§10)

| View | Shape | Family |
| --- | --- | --- |
| C1+C8 | `TyCtxt.Retains(callee or Callable signature, Subst)`, memoized: `Retention(Input, Kind, Output)` plus `Untied`.<br>Reachability over the instantiated signature's Origin atoms, with abstract carriers and edges from substituted clauses and well-formedness.<br>`Value` from a reference input's outer Origin, `Content` only from inside its referent. `Untied`: a result atom is unreached and not `static`.<br>Retention is decided by the instantiated contract (S15:529). | step 1 |
| C1-F4 | `TyCtxt.OriginObligations(definition)`, immutable `(SourceRef Site, BoundOrigin Longer, BoundOrigin Shorter, ulong CaseMask)`: an end-of-Bind snapshot of `BindingObligation` rows (`B/Binding.Origins.Model.cs:154-162`), without clauses proved or failed as Constraints (T1).<br>Also `OriginAtom.Of` and `UniversalRelations.For(definition, case)`. | F4 |
| C2 | `TyCtxt.Closure(k)`: per view, receiver mode and `Retention` rows; removes `B/Binding.ValueCalls.cs:428-442, 531-543` `Unsupported` | F7 |
| C5-Binding | Preserves kind on `EffectObligation` | F6 |
| E4/E5 | failure keys per Opaque site | A6 |

## 5. Rules

ℓ is a location; `σ_in` is the state on entry to ℓ and `σ_out` the state after. Reading `X` with k borrow Derefs gives
`val(X) = content^k(σ_in(root(X)))`.

```text
// ---- Places (F1, F2); step classes per §3
within(A, B)       :- B's chain is a prefix of A's; an Index meets any index step; distinct Field, ConstIndex, Downcast
                      steps are disjoint; raw-anchored chains compare only through the same raw Deref.
through(X, A)      :- within(X, A), a borrow Deref of X lies below A.
throughShared(X,A) :- within(X, A), a shared borrow Deref of X lies below A.
deep(k)            :- k ∈ {move, sborrow, uborrow};   obs is a deep exclusive use that stops at shared Derefs.
hits(A, X, k)      :- within(A, X) ; within(X, A), ( !through(X, A) ; deep(k) ; k = obs ∧ !throughShared(X, A) ).
                      // E1: also a write or destruction while L sits in its reservation holder; E3: throughShared vacuous
conflicts(k, m)    :- m = uniq ; k ∈ {write, move, uborrow, destroy, obs}.
mode(L, H)         :  Shared → ref; Uniq → uniq; Φ_c → uniq; TwoPhaseUniq → ref iff H is L's reservation holder.
content(S)         =  ⋃{σ(T) | L∈S, named(L,T)} ∪ (σ(E) if Φ_E∈S)            // monotone in S
refs(S)            =  {T | L∈S, named(L,T)} ∪ ({E} if Φ_E∈S);  refs^k(S) = refs(content^(k-1)(S))
val(Aggregate|Cast{Erasure,Upcast,Pointer}) = ⋃ val(operands);  val(BinaryOp|UnaryOp|Discriminant|Len|Const) = ∅
val(Opaque s)      =  ⋃ σ(roots of s) ∪ {τ_s, Φ_E};  val(static or raw-derived Place) ∪= {Φ_E};  Adapt per MirCases   // E15: {Φ_E}

// ---- Carrying (forward)
σ(H, entry)        =  {Φ_E} for a Loan-capable Arg, else ∅;  σ(E, entry) = ∅
ended(L, ℓ)        :- base(L, B), destroyed_in_place(K, ℓ), within(B, K).   // move paths; never across a Deref (E8: K exactly prefixes place(L))
step(L, ℓ, ℓ′)     :- edge(ℓ, ℓ′), !ended(L, ℓ).   // every carrying rule takes the step: σ_out(K) \= ended(ℓ), ∀K and E
issued(L,X,_,ℓ,H)  ⇒  σ_out(H) = {L} ∪ ⋃_{j<k} content^j(σ_in(root(X)))           // R2: parents of every followed layer (E3 past a shared Deref)
assign H := rv  (whole root)         ⇒ σ_out(H) = val(rv)                          // complete replacement
assign D := rv  (direct, D ⊐ H)      ⇒ σ_out(H) = σ_in(H) ∪ val(rv)                // P1
assign D := rv  (k ≥ 1 borrow Deref) ⇒ ∀T ∈ refs^k(σ_in(root(D))): σ_out(T) ∪= val(rv)   // S15:454, S4: ends nothing
move H (whole root)                   ⇒ σ_out(H) = ∅
StorageLive | StorageDead | EndBorrow H ⇒ σ_out(H) = ∅
join               :  per-slot union; P1/P2 as MirDataflow; prune(head): σ(H) = ∅ if ¬live_in ∧ no Loan names H

// ---- Calls (F3): c at ℓ, arguments a_i, destination D
ret(i, Value)      =  val(a_i) ∪ (content(val(a_i)) if a_i is a reference);   ret(i, Content) = content(val(a_i))
out                =  ⋃{ret(i,κ) | retains(c,i,κ,result)} ∪ ({Φ_E} if untied(c)) ∪ (desc(c) if abstract(c), descent)
                   ⇒  assign D := out;   ∀ retains(c,i,κ,store j), ∀T∈refs(val(a_j)): σ_out(T) ∪= ret(i,κ)
                   // a retained TwoPhaseUniq Loan has left its reservation holder, so it counts as activated
desc(c)            =  {Φ_c} ∪ (val(a_r) \ {R_c})   // a_r: the receiver operand; R_c: the Loan of the Ref producing it, if any
                   // erratum (prototype round 9, PLAN G113): val(a_r) is R_c plus its R2 parents, so Φ_c's values carry
                   // its parent Loans, as a child's do (S15.6.3), and keep no R_c (S8.11.3). Without the parents, 36c,
                   // 36e and 36g are accepted. It reads the call's operand, as every ret(i,κ) does; R2 of recv(c) at ℓ
                   // agrees only while writes to the reservation's route are rejected (§15.6.7; 36e, 36h)
unknown callee     :  every argument → result and → store j for every exclusive a_j; untied.
writeback          :  the write through $w activates the Loan reserved in $w (nothing retains $w).

// ---- Liveness (backward)
use(H,ℓ)           :- ℓ reads, Moves or borrows a Place rooted at H (also through borrow Derefs, and an assignment through
                      a reference rooted at H); FakeRead; SwitchInt or callee operand; EndBorrow H; Return (_0); a Drop of
                      a Place rooted at H whose Type observes (check-only Drops: no group, case Type).
kill(H,ℓ)          :- whole assignment or Call destination H; move H; StorageLive/StorageDead H; EndBorrow H (after use).
live_in(ℓ)         =  (live_out(ℓ) \ kill(ℓ)) ∪ use(ℓ);   live_out(ℓ) = ⋃ live_in(succ)   // P1 chain: runtime successors

// ---- Check sets: by holder, never by Loan id
closure(S)         =  least S′ ⊇ S with σ_in(T) ⊆ S′ for each L∈S′, named(L,T)     // liveness through references
transit(ℓ)         =  ⋃ val_in(v) over stores(ℓ, v, dest) with dest a holder live after ℓ (in live_out(ℓ), or made
                      live by closure over CS(ℓ)'s live direct Loans), or E. A store through a reference stores into
                      the holders its direct Loans name, or into E (Φ_E). A dead target stays unchecked (r3 N1)
                      // val_in: the stored value's set computed from σ_in; a Loan born at ℓ is in no entry set.
                      // = the plan's stored(L,P,T) with live(H,P) ∧ overlap(T,H) (per-root: T's holder) or escaped(T)
CS(ℓ)              =  closure( ⋃{σ_in(H) | H ∈ live_out(ℓ), ℓ does not overwrite H} ∪ σ_in(E) ∪ transit(ℓ) )   // E5: own-value seed
CS_call(ℓ, t)      =  closure( ⋃{σ_in(H) | H ∈ live_in(ℓ), H ≠ t} ∪ σ_in(E) )      // t: the reservation holder; E5: seeded by σ_in(t)
access(ℓ,X,k)      :- Copy|FakeRead|Discriminant|Len → read; Move → move; Ref Shared|TwoPhaseUniq|Raw → sborrow,
                      a destroying Ref → read (shallow); Ref Uniq → uborrow; destination → write;
                      StorageDead → destroy;  Drop → destroy, plus obs if observes(Type(X)).

err(conflict,L,ℓ)  :- access(ℓ,X,k), L ∈ CS(ℓ) via H, real(L), hits(X, place(L), k), conflicts(k, mode(L,H)),
                      !excused(L,ℓ), !partition(L,ℓ,k,X).
err(activate,R,L,ℓ):- reserved(R,t,_,ℓ), L ∈ CS_call(ℓ,t) via H, real(L), hits(place(R), place(L), uborrow),
                      conflicts(uborrow, mode(L,H)), !excused(L,ℓ).     // a destroying R: hits as write (shallow)
                      // (E15: mode(L,H) = ref only)
err(destroy,L,ℓ)   :- destroys(ℓ, K, e), X = (K's elements if e else K), L ∈ CS_call(ℓ,t_K) via H, real(L),
                      observes(Type(X)), hits(X, place(L), obs), ¬hits(X, place(L), uborrow),   // E11: ¬hits(…, write)
                      conflicts(destroy, mode(L,H)).   // at the call, on σ_in, so before ended (E15: an err(conflict) row)
                      // only the obs part beyond the shallow activation: the target's own Places are already
                      // checked by the call's activation, so one Loan never gets two codes (r3 N4)
// Arguments of one call (S15:614): every exclusive argument borrow is a reservation (G5), and its activation scans the
// other arguments' holders, live into ℓ, where a reservation counts as ref. A non-reserved exclusive argument is a
// stored borrow, live when formed, so an overlapping later argument conflicts at its own formation.
err(effect,L,ℓ)    :- key(ℓ,f,k), L∈CS_call(ℓ,−) via H, root(place(L)) a static of Field f (P5), conflicts(k, mode(L,H)).
err(effect,L,ℓ)    :- unknown(ℓ), L∈CS_call(ℓ,−), root(place(L)) a mutable static.  // calls, erased Drops (S16:196)
err(ended,L,ℓ)     :- EndBorrow H at ℓ, issued(L,_,_,_,H), L ∈ CS(ℓ).               // guard-local Origin end (E7: by γ_H, guards only)
partition(L,ℓ,k,X) :- k = destroy, root(X) a body root, L ∈ σ_in(_0) and L in no other part of CS(ℓ).   // DIAGNOSTICS (E12: A6's merge instead)

// ---- F6 preserves results (S2)
disturbed at ℓ     :- ℓ writes, Moves or destroys a direct D ⊑ recv(c), or writes through a Place hitting recv(c)
                   ⇒  Φ_c is replaced by Φ_c^dist in every σ (by holder, like ended)
excused(L,ℓ)       :- Call c2 at ℓ, preserves(c2), L = Φ_c1 (not ^dist), callee(c1) = callee(c2), recv(c1) ≡ recv(c2).
excused(L,ℓ)       :- ℓ forms an argument reservation of a Call c2 with excused(L, c2's location).   // E2: derived from reserved
                      // erratum (prototype round 7, 33c): MIR forms c2's receiver reservation before the Call, so
                      // without this row SPEC's takeTwo (§8.11.3) is rejected at that formation; takeAcross still errs
// Φ_c: place = recv(c)'s referent with its content, mode uniq, issued with desc(c)'s parent Loans into the result of an
// abstract requirement or Callable call whose result descends (§8.11.4). Conformance (H, S2 text): an escaped Φ_prev on
// (*self) with a fresh anchor at entry; a self-Field-path call of a preserves requirement undisturbed since entry is
// excused against it.

// ---- F7: an Aggregate(Closure) flow; [x@ref] issues on the outer slot; calls use TyCtxt.Closure; a Consuming call
// Moves the Closure. F9: inline copies are ordinary code; template calls use P9. F10: forward on P1 + P2 edges, liveness
// by the chain rule, checks in every reached chain (S14:514).
```

**Worked verdicts** (prototype round 6 unless noted):

- **25a, `x = f(x@ref)`**, retained, x used later, no Drop: at `x = move _r`, `transit` holds `{L_x}`, and the write hits
  it. Rejected (S15:453, S15:502; Rust E0506). 25b, with x dead afterwards, is valid.
- **25c, enum `x = pin(x@ref)`**: rejected at the same store, so x never holds `.B` aliasing its own `.A` payload.
- **19a/21a, valid**: the new Loan is in no entry set at `_t = Ref(…)`; at `cursor = move _t`, the transit Loan lies
  through a borrow Deref, so the shallow write misses it, and `step` ends it. 19b and 21b still err.
- **26a, `replace` with a clearing drop** (`let c = s.r@follow[0]@uniq; replace(s@uniq, with: S(r: y@uniq))`): the
  call's `destroy` of s is `obs` and meets c's Loan on `s.r.*` before `ended`. Rejected.
- **26b, `Array<S>.clear`**: the `destroy` of `arr`'s elements is `obs` and meets the child of `arr[0].r`. Rejected.
- **29b, escaped destination**: `p@follow = S(r: p.r@follow@uniq)`, with p a `uniq/S` parameter and a writing drop.
  `Drop((*p))` is `obs` and meets the child in the stored `_v`; at one point, a store through a reference is transit.
  Rejected.
- **29c, path-exact**: `s.inner = f(s.inner@ref)` with only s used later. Under P1 the destination is s, which is live,
  so the stored value is transit. Rejected.
- **30a–30d, linked lists**: 30a and 30b are valid; 30c (a write through the old link) is rejected; 30d is the listed
  loss.
- **27, shared field, valid**: `let s = S(r: x@ref); v = s.r@follow@ref`. At s's Drop, `obs` stops at the shared Deref
  (S15:509, S15:513); `StorageDead(s)` ends the child, and v keeps `L_x`.

**A6 mapping:**

| Problem | Code |
| --- | --- |
| A TwoPhaseUniq Loan in its reservation holder | `CallReservationConflict_Kd` |
| Activation at a Call | `CallActivationConflict_Kd` |
| Activation at the writeback write (`OwnershipModel.cs:238`) | `PlacementActivationConflict_Kd` |
| Effect | `CallEffectConflict_Kd` (E15) |
| A `PreparedInput` root (R4-M10) | `DefaultArgumentAccess_Kd` / `DefaultArgumentBorrow_Kd` (E15) |
| `ended` on a `GuardCandidate` | DIAGNOSTICS choice (§9) (E15) |
| Otherwise, including `destroy` | `ComparisonLoanConflict_Kd` |

A problem whose access is an Opaque site's, or whose witness holder carries `τ_s`, is derived from s (E15).

**Partition lemma** (DIAGNOSTICS; E12 replaces it): every Loan that exactly reaches `_0` passed a fit into the result's fixed Origin, so a
body-rooted one is the Refuted relation (S15:575). Over-approximated Loans are harmless to exclude.

## 6. Evaluator

### 6.1 Algorithm and data

Per body view, in `MirAnalysis` step 4 (after A4's `Run`; `flow.Begin` and `moves.Build` are redone per body):

1. **Pre-pass**, one `MirUses` walk: holders, Loans (CSR by place root and base root; E8: no base root), `destroys`/`destroying` sites
   and per-root conflict flags. A view with no real Loan, or with no access that can conflict by root flags, skips the
   stratum (census: 161 + 266 + 16 Loan-free views) (E15).
2. **Liveness solve**: `flow.Backward` over `⌈Hf/64⌉` words.
3. **Carrying solve**: `flow.Forward` over `Hf + 1` node slots, with the node-union join and head pruning.
4. **Replay**, per reached chain, in two sweeps:
   - backward from the tail's live-out, recording deaths and next uses;
   - forward from the head's slots, evaluating `CS`/`CS_call` at candidate accesses (live-holder bit scan, node-id
     stamps, the stored values' node ids, closure), then the transfer and `step`.

**`MirLoanSets`**: sorted `int[]` contents in one pool, with hash interning. Memo dictionaries cover `Add`, `Union`,
`Remove` (for `ended`) and `Map` (for disturbance). Node 0 is ∅.

A check scans holder by holder, which gives `mode(L, H)`. Transit sets count as non-reservation holders.

### 6.2 Complexity and work counter

N locations, Hd heads, Hf flow holders, H holders, L Loans, A★ candidate accesses (destroying calls included), ρ head
re-visits (ρ_f ≈ n for an in-loop copy chain of length n, K1), κ `ended` and δ disturbance points, ν node bytes created:

`W = Θ(N + L) + ρ_b·(N + Hd·⌈Hf/64⌉) + ρ_f·(N + Hd·Hf) + ν + (κ + δ)·H + Θ(N + Hd·Hf) + Σ_{ℓ∈A★} (⌈H/64⌉ + Σ_{distinct live, transit and closure nodes} |node|)`

The transit clause adds only the stored values' nodes at candidate accesses: at most the operands' node ids, counted
under the same term. `MirBorrows.Work` counts each term: transfer steps, slot joins, node bytes, sweeps, check scans.

### 6.3 V16 restatement (owner decision)

- **Linear** are the `N`, `L` and replay terms and the alias shape. The `Hd` terms are R4-M20's dense reading.
- **Not linear in input + output** are the access, kill and pass terms (linear per dimension). No simple linear form
  exists.
- **Wording for plan §4.6**: "For A5, work ≤ c·(input + output + Dense), where output is the live Loans per point plus the
  problems, and Dense = heads × flow holders + Σ candidate accesses × sizes of the distinct live and stored nodes they scan
  + Σ (ended and disturbance points) × holders + (passes − 1) × locations. `MirBorrows.Work` counts every term; the 2× and
  4× scaling tests include an in-loop copy chain (K1) and k overlapping live nodes."
- **Scaling shapes**: (1) a straight line; (2) N references used at the end; (3) N Copies of one reference; (4) N loops
  with M live references; (5) N writes through one reference; (6) borrow/write pairs in a loop; (7) K1; (8) k-overlap;
  (9) N chained locals and N signature Origins.
- **Memory**: `⌈Hf/64⌉ + Hf + 1` words per head, sized once in `MirDataflow.Prepare` against 64 MiB (R4-M15a); above
  that, `OwnershipStorageLimit_Kd` is recorded once. Pools are reused.

### 6.4 Integration and errors

- **Integration**:
  - `MirDataflow` owns both solves, the chains, the P1/P2 rule and the arena; A5 supplies struct transfers and walks no
    edges itself.
  - `MirReach` stays the one traversal.
  - Views come from `MirCases`; a body without case-dependent accesses runs once.
  - A2 is read only through `Group`.
- **Errors**: each adds `MirProblem(code, subject, views)` with fact rows (R4-M3a): the Loan anchor; the witness holder's
  next use, or the store site for a transit Loan; for an activation, the other anchor. No check re-runs analysis.

## 7. Origin stratum

**Algorithm.** Per body and per distinct case-mask class of its rows:

- **Nodes** are the atoms of `OriginObligations` rows and containment edges, classified by `OriginAtom.Of` as fixed,
  finite(body) (mutable-static Borrows under P6 included) or inferred (raw anchors and Borrows through a borrow, c1).
- **Refuted**: one multi-source BFS from finite nodes through inner nodes. Reaching a fixed node gives
  `UnsatisfiedOriginRelation_Kd` at the last edge's site, with predecessor witnesses.
- **Unknown**: fixed-source bitsets (64 per word) propagate through inner nodes. An edge into fixed g carrying f with
  `¬entails(f, g)` gives `UnprovenOriginRelation_Kd`.
- **Bound**: Θ(N_o + E_o·⌈F/64⌉·ρ + Ft·Pm). The stratum reads no liveness, CFG or σ, and Loan flow reads only `Retains`.
  The strata are independent, and A6 merges their problems.

**Containment** (row 9c; E4 corrects the cut): a Ref through borrow Derefs adds `contain(o_k, o_new)` per layer, up to and including the
first shared layer, the same cut as `obs` (`MirCases.Layer` for pair layers). Edges between fixed atoms are entailed by
Binding's meet.

**Boundary.** Each failure is reported once, by partition (E12: A6's merge).

- **Binding keeps** OJ1–OJ7, T1–T4 and the fixed–fixed body relations it forms directly (B1/B2 fixed ends, B3, B4, B6's
  fixed part, B7, B9–B11, B14, B15).
- **A5 judges** every published row and every chain through an inner node (B5 occurrences, B8, B13, B16–B18): the old
  Analysis's role (`A/OwnershipAnalysis.Borrows.cs:13-121`, `A/OwnershipAnalysis.cs:228-240`).
- **DEC entry**:
  - plan §3's "chains inside bodies" means chains through an inner node;
  - Binding's fixed–fixed body judgments (`CallRelations`, `ResultPremises`, `RecordMismatch` parts) move at R7,
    superseding PLAN10's R6 share;
  - R4-M4's compared set splits `Binding.OriginRelation` by owner.
- **T1**: Binding leaves clauses it proved or failed as Constraints out of the rows.
- **Deleted at R6-check**: the old reporter, the gates `A/OwnershipAnalysis.Borrows.cs:74-79` and
  `B/Binding.Diagnostics.cs:985`, and the `CallBorrows` skip (`B/Binding.TypeOrigins.cs:182-192`).
- **Unowned items** (unit 2a0, before 2a):
  - unresolved inference obligations become local regions in a body (S15:348) and are Binding's
    `MissingOriginBinding_Kd` elsewhere (`A/OwnershipAnalysis.Borrows.cs:45-47`, `B/Binding.OriginProof.cs:443-449`);
  - under P6, a mutable-static Borrow is finite(body) (B19);
  - the `ValidateOriginRelations` split;
  - a census of the containment skip at `B/Binding.ArgumentOperations.cs:565`.

## 8. Components and files

| File | Contract (entry file starts with it) | Lines |
| --- | --- | ---: |
| `Mir/Analysis/MirBorrows.cs` | Owns A5's Loan stratum (SPEC 15.6–15.8) per body view.<br>Input: body, view, move paths, dataflow, A2 groups, A4 keys, Binding facts.<br>Output: Loan-family problems with facts through `MirReport`.<br>Invariants: root holders plus `E`; base-named Loans; syntactic access; checks by holder, with transit; regions keep no Loan live.<br>Must not read syntax or HIR, judge Origins, re-derive retention, intern projections, or copy the body per view. | 290 |
| `Mir/Analysis/MirBorrows.Flow.cs` | Forward carrying transfer, `step`, join | 290 |
| `Mir/Analysis/MirBorrows.Liveness.cs` | Backward holder liveness and per-chain transitions | 130 |
| `Mir/Analysis/MirBorrows.Checks.cs` | Step classes, `hits`, check sets, transit and closure, conflict, activation, destroy, effect, `ended`, partition, S2 | 320 |
| `Mir/Analysis/MirLoanSets.cs` | Owns interned immutable Loan sets: Add, Union, Remove, Map; the only Loan-set form in Mir | 140 |
| `Mir/Analysis/MirOrigins.cs` | Owns A5's Origin stratum: judges rows and containment per case-mask class; reads no liveness or Loans | 260 |
| `MirDataflow.cs`, `MirInit.cs` | G1 | +94 |
| `MirTypes.cs` | G2 | +40 |
| `Mir/Build/*` | G5, G6, writeback, Slice, Guard, C8 | +110 |
| `MirEffects*.cs` | `Keys` (P5 bitsets) | +110 |
| `MirReport.cs`, `MirAnalysis.cs` | A6 mapping, derived taint, facts; step 4 | +100 |
| Binding (`TyCtxt.Retention.cs`, `TyCtxt.Origins.cs`, `Binding.Closures.cs`, `Binding.EffectObligations.cs`, catalog `InPlace`, Opaque keys, boundary fixes) | §4.1, §4.3, §7 | +460 |
| **Total** | | **≈2,340** |

- **Deleted at R6-check**:
  - `A/Ownership*` (46 files, 15,446 lines), `A/BorrowLiveness.cs` (141) and `A/PackedAnalysisTable.cs` (59);
  - the two `Unsupported_Kd` gates;
  - the Transitional couplings, with the 183 `.Ownership.Bodies` test reads migrated per family.
- **Net**: about −13,300 lines (−11,200 counting the landed A1–A4).
- **Size limit**: each file stays under 5,000 tokens.

## 9. Owner decisions

Item 1 of r2 ("the finding 1 family: the representation or a hold") was **decided on 2026-10-17: the representation**,
the transit clause in `CS` (§5).

**Items 2–9 were approved by the owner on 2026-10-17 as recommended:**

- the needed losses P1, P3, P4, P10 and P11;
- the optional losses P5, P6 and P9, all adopted;
- the root-most kill recovery and relaxation (c) (`clear` as `InPlace`): not adopted;
- relaxations (a) and (b): confirmed;
- the S1 additions, clarifications, V16, the Origin boundary, the MIR contracts, and the DEC and DIAGNOSTICS records:
  approved.

**Item 10 was approved by the owner on 2026-10-17** (「承認します。」, relayed by the planning session): the needed
loss P12, `desc(c)` (PLAN G113, prototype round 9).

Until the SPEC simplifications landed (plan §5.3), a violation of an approved loss was a located `Unsupported_Kd`. They
were integrated at `4372b058` (`draft/INTEGRATED.md`), so A5 reports a
violation of an approved loss with its Language code, in shadow (plan §5.3; S1: DEC R4-M39); the default pipeline keeps
the earlier rules until R6-check (DEC R4-M50). P12 (item 10) is in SPEC since `0779b975` (SPEC 15.6.3, 15.6.4), so A5 reports a violation of it (36f) with its Language
code.

**Item 11 was decided by the owner on 2026-10-17** (DECISIONS R4-M52), choosing each recommended option of
`../A5 Step 1/choice-1b.md` §4: G92b the fold (b2), G92a assumed completion with α, the case 23a proposal as option (c),
Loan ending by exact Move-Path prefix (§12 E8), and the SPEC revision (P12, E8, the reported gaps) in a separate session; and, asked for unit 1a, that a callable whose
implementation a call cannot see relates its fixed input-position Origins to its fixed result and writable-storage Origins.

| # | Decision | Content | Recommendation |
| --- | --- | --- | --- |
| 2 | OD13 loss list | §2's tables, with counts and class:<br>needed: P1, P3 (both forms), P4, P10, P11;<br>optional, each its own decision: P5, P6, P9.<br>S1's own: base operations (9k, 9l, 14), the `within` kill (23a, 30d), the raw-site form.<br>Optional recovery: the root-most kill (sound with P3; prototype, 92 cases).<br>No row for r2 finding 3, since the cut is taken. | Approve; decide the recovery |
| 3 | Relaxations | (a) Row 8: referents of distinct bases are disjoint (S15:494 read as "distinct anchors, one exclusive").<br>(b) Distinct Downcasts are disjoint (S3:253 has payloads share bytes); sound with the transit clause.<br>(c) `clear` as an `InPlace` destroying target over the elements; sound only with the destroying-call `destroy` access. Default: `InPlace` for `replace` only. | Confirm (a) and (b); decide (c) |
| 4 | S1 proposal | **Add P3** (S1′ without it is unsound on 29a) **and the own-site check** (exclusion by holder, never by Loan id; S1′ is unsound on 30c without it); the kill on every step, never across a Deref (23a, 30d listed); an observing destruction as an exclusive use up to the first shared layer, at `=` and in destroying calls; the exemption as "a target destroyed in place" | Add to S1 |
| 5 | Clarifications | c1: a Borrow through a borrow is inferred (Unknown; S15:567). c2: dissolved by P6 (without it, about +40 lines and A4 anchors) | Clarify only |
| 6 | V16 | The restated bound of §6.3 | Adopt |
| 7 | Origin boundary | §7: A5 takes the old Analysis's role at R6-check; Binding's fixed–fixed body judgments move at R7; the ledger split; T1 | Adopt |
| 8 | MIR contracts | The TwoPhaseUniq writeback activation; the `RawSlice` backing operand; `MirCallee.Arg`; the `EndBorrow` meaning; the catalog `InPlace` column. No closed-vocabulary entry; no `MirLocalRole` member | Approve |
| 9 | DEC and DIAGNOSTICS | **DEC**: the A5 representation (§3, §5); the `MirDataflow` join hook and backward pass (superseding R4-M20's word-OR detail); A4 keys; the boundary; G3, G4, G7; Relate and Clauses only inside `Retains`; `PublishedBindingTypes` additions.<br>**DIAGNOSTICS**: the partition rule; the guard `ended` code; E5's minimum-key cause | Record |
| 10 | P12 (G113 erratum) | `desc(c)` (§5): a descending result keeps `Φ_c` with the receiver's parent Loans. Needed for soundness (36c, 36e, 36g); costs 36f, 0 / 0 / 0 found. Rejected: `Φ_c` alone (unsound), keeping R_c (rejects `takeTwo`), R2 of `recv(c)` at the call (sound only while A3 rejects writes to a reservation's route, which it does not yet: 36e, 36h) | Approve |
| 11 | Step 1 (choice-1b §4) | G92b fold; G92a assumed completion with α; case 23a option (c), §12 E8; the SPEC revision in a separate session; retention through a callable whose implementation the call cannot see (a Function Type value, a Callable-bound value, a callable Type inside a contract; unit 1a): its fixed input-position Origins reach its fixed result and writable-storage Origins | Each as recommended; decided 2026-10-17 (DECISIONS R4-M52) |

## 10. Implementation units (plan §6.2)

| Unit | Content | Completion condition | Checks |
| --- | --- | --- | --- |
| 0a | Ledger re-baseline at the step-0 base (census §3 command) | `ledger.tsv`, `summary.md` recorded | V13 failure-set equality |
| 0b | **Closed by round 10** (2026-10-17: 246 cases, no unsound acceptance and no unlisted rejection under design A and E8's kill).<br>Prototype into `draft/Sketches`.<br>Recorded: the 92 cases, including 25a–25c (transit, dead store), 26a/26b (destroying calls), 27 (cut), 28a–28d (plain arguments: E, V, V, E), 29a–29c (crossing, escaped, path-exact) and 30a–30d (linked lists).<br>Added (round 7): `swap(a, b)` with parameters, `y = h@follow + 1`, `Φ_c^dist`. | All match the A column: no unsound acceptance and no unlisted rejection (round 6: 70 same, 14 earlier, 8 listed losses) | `proto.cs` run |
| 0c | G1 `MirDataflow` | A1 behavior-preserving; a backward test problem | `MirDataflow`/A1 tests, `BodiesScaleLinearly`, ledger unchanged |
| 0d | G2 | Classified over library Types | Unit tests (`Logger` S15:598-604, abstract and Sealed parts) |
| 0e | Builder contracts G5, G6, writeback, Slice, Guard, C8 | Dump goldens updated; validator clean | `MirBuildTest`, goldens |
| 0f | A4 `Keys` | Keys per Call/Drop, linear | `MirEffects` tests, scaling |
| 1a | Binding C1+C8 `Retains(callee, Subst)`; catalog `InPlace` | Immutable after Bind; allow list | View tests, `KimiLibraryShapeTest` |
| 1b | `MirLoanSets`, `MirBorrows` F1, F2, F3, F10 (joint core), with §12's errata. Units (`../A5 Step 1/choice-1b.md` §3): 1b-1 `MirLoanSets`; 1b-2 pre-pass, liveness, carrying with retention and stores, check sets and the conflict check; 1b-3 reservations, activation, E1, destroying calls and `obs`; 1b-4 guards (E7), with `partition` leaving the Loan stratum (E12); 1b-5 the driver step in shadow. Before 1b-5: the MIR units G92b, G123, G94 (one MirBuilder lane) and G91; G92a may follow | Library and milestones produce no A5 problem (R4-M12 from here). Shadow watch: raw writes in `Storage.kimi` and `DictionaryStorage.kimi` moving Loans into `σ(E)` | SPEC-example table (`tests/diagnostics/spec-cases.json`: about 40 error and 34 accepting variants); 0b's cases as MIR-layer tests; scaling shapes 1–8 |
| 1c | A6 mapping and derived taint for 1b's codes | Codes per §5 | Corpus snapshots, ledger |
| 2a0 | Binding boundary: OriginObligations, atoms, universal relations, T1 rows, unowned items, `ValidateOriginRelations` split, ledger split | Rows immutable; no double owner | View tests, ledger |
| 2a | F4 `MirOrigins` | audit-origins §3 tests; the old reporter's rows matched | Shape 9, ledger |
| 2b | F5 (keys, unknown, P6) | S15:540-558 per P6; S16:199-202 | Rule tests |
| 2c | C2 view, then F7 | S15:715-723, S7:341-344 | Rule tests |
| 2d | F8: updates, match, guards, writeback, collections, destroying calls | S14:332-351, S15:650-689, S13:600-602, S4:570-575 | Rule tests |
| 2e | F9 defaults | S7:84-88; audit-calls row 26 | Rule tests |
| 2f | C5 view, then F6 (S2) | `takeTwo`, `takeAcross`, Drain, Merge; 12 library conformances accepted; prototype 36a–36g (`desc(c)`) | Rule tests, library corpus |
| 2g | E4/E5 keys, A6 completion | Derived rules per DIAGNOSTICS | Corpus |
| Exit | R6-check deletion list (§7, §8), test migration | Every family complete, no `Unsupported_Kd` family | Session verify, final review |

## 11. Risks and open questions

- **P3 form (b)** may be a SPEC soundness gap rather than a loss: z gives shared access to x while x is written through
  r. The old path rejects it. Decided on 2026-10-17: a needed loss, "P3 (both forms)" (§9 item 2).
- **`Φ_E` in the library.** Raw-derived or static-derived writes move Loans into `σ(E)` forever. Watch `Storage.kimi` and
  `DictionaryStorage.kimi` in 1b's shadow run (r2 finding 7).
- **Unit 0b** (closed by round 10, §12). Round 6 (92 cases) showed no unsound acceptance and no unlisted rejection. Round 7 added `swap(a, b)`
  with parameters (31a, 31b), `y = h@follow + 1` (32a, 32b) and `Φ_c^dist`, and found the unlisted rejection 33c (the
  erratum below; 99 cases). Round 8 (109 cases) found design A unsound on 36c: F6 does not state that `Φ_c`'s values
  carry the receiver's Loans, and the prototype encodes them without (PLAN G113). Round 9 (113 cases) gives them the
  receiver's parent Loans (`desc(c)`, erratum below), so 36c is rejected. 0b stays open until a rerun meets its §10
  condition; round 9 still has the unlisted 33c and the unsound 36h (the reservation route, below).
- **Other open items**: F6 (waits for S2); Binding views (snapshots); R5 (serialized, plan §9.1); A4's depth limit
  (S5). S1 with P3 (§9 item 4), relaxation (c) and V16 were decided on 2026-10-17 (§9).
- **F6 stores (H, round 9).** `Φ_c` goes only into the result, never into storage that `retains(c, i, κ, store j)`
  writes. A stored value retaining the receiver's content then carries no `Φ_c`, while §8.11.4 rule 1 lets a later
  abstract call on the receiver reach its Loans. A fix stores `desc(c)`, not `Φ_c` alone. Not prototyped; for unit 2f.
- **Reservation route (round 9 review, 36h; resolved by E1).** §15.6.7 makes any write to a reference a reservation was reached
  through an error. `hits` gives a write no reach across a Deref, so A3 accepts
  `cur.inner.takeWith(do { cur = b; 0 })`. This is outside F6 and needs its own rule (PLAN G119).

## 12. Step 1 errata (2026-10-17)

The 1b design diff (`../A5 Step 1/choice-1b.md`: two independent designs per part, the case 23a review, a design review;
prototype round 10, 246 cases, no unsound acceptance and no unlisted rejection) corrects A3 as follows. Where a section
above disagrees, these errata govern; the lines they replace are marked "(E…)".

- **E1, reservation route (PLAN G119; SPEC 15.6.7).** `hits` gains: while L sits in its reservation holder H (the holder
  that gives `mode(L, H) = ref`), a write or destruction of a prefix of `place(L)`, across borrow Derefs, reaches L. A
  destroying activation (`replace(cur@uniq, …)`) is such a destruction. Checked at the access on `σ_in`. Under E9's fold no
  reservation is formed through another reservation's holder, so no chain fact is needed.
- **E2, the excuse at c2's reservations (33c).** The round-7 erratum's row is derived:
  `excused(L, ℓ1) :- reserved(R, _, ℓ1, ℓ2), excused(L, ℓ2)`. Unit 2f.
- **E3, Borrows past a shared Deref (PLAN G110; SPEC 15.6.3, S1).** A Ref whose Place has a shared borrow Deref forms no
  Loan (no `issued` row); its holder gets `⋃_{m ≤ j < k} content^j(σ_in(root(X)))`, m the index of the last shared Deref.
  With no Loan past a shared Deref, `throughShared` and `obs`'s shared cut are vacuous and 1b omits them; unit 2f re-adds a
  cut only if F6 places `Φ_c` past a shared Deref.
- **E4, containment (§7).** The containment cut is E3's derivation cut, counted outward from the Borrow, not "the same cut
  as `obs`". Unit 2a.
- **E5, own-value seed (SPEC 15.6, "a value is also used wherever…").** `CS(ℓ)`'s closure is also seeded with `σ_in(T)` for
  each T named by a direct Loan of the value ℓ stores into a holder live after ℓ (live as `transit` reads it: in
  `live_out(ℓ)` or made live by the closure) or into `E`: the Ref's own Loan and
  its R2 parents from the layers it carries; `CS_call(ℓ, t)`'s closure with those of `σ_in(t)`. Conflict candidates are
  unchanged (E13 (1) adds a borrow's R2 parents to its own check). Without it a self-referential holder is unsound (39o, 100, 100d, 101).
- **E6, Slice index.** The Index of a Slice handle is a shared borrow Deref in the step classes (SPEC 4.8.1: the element
  is the handle's referent), not an inline step. UniqSlice has no built-in Index (`index`/`indexUniq` calls).
- **E7, guard-local Origin (SPEC 14.8.3).** A marker `γ_H` (with `Φ_E` and `τ_s`): each value issued into a guard
  candidate H (`MirLocalRole.GuardCandidate`) carries `γ_H`, which never conflicts and is carried like a Loan but is no
  Place Loan (`ended` does not remove it). `err(ended,ℓ) :- EndBorrow H at ℓ, H a guard candidate, γ_H ∈ CS(ℓ)`; the
  Loan-based `err(ended)` row is deleted, and other `EndBorrow`s get no ended check. A6's merge (E12) also covers
  `err(ended)` when `γ_H` reaches `_0` (one failure, one code; unit 1c) (E15). It makes the case 23a review's C2 lowering
  contract unnecessary.
- **E8, Loan ending (owner, 2026-10-17: the case 23a proposal, option (c); DECISIONS R4-M52).** Destroying in place a Move
  Path K (by `=`, StorageLive/StorageDead or an `InPlace` target) ends every Loan whose access path K exactly prefixes:
  K's steps compare exactly (a constant index only with the same constant, never a runtime index), and the path may
  continue with any steps, borrow Derefs included. This replaces `base(L, B)`, the `within` kill and "no kill across a
  Deref" (§3), and recovers 23a, 30d, 34c, 35a and 35b. Checks still precede the end. Its SPEC revision (§15.6.3, Appendix E,
  §13.5.5) landed with P12's at `0779b975`. A Move Path takes no own step (SPEC 15.1.3, 15.6.2), so a K through an obj or rc `payload`
  ends nothing: case 75 (an obj payload's Field replaced while a child borrowed through it lives) is an error, as SPEC's letter
  gives (owner, 2026-10-18; PLAN G147 (6)); the case 23a review's verdict "valid" (`../A5 Step 1/review-23a.md`) and the
  prototype's PrefixKill, which ended it, are superseded (prototype's 2026-10-18 correction).
- **E9, MIR contracts decided by the owner (2026-10-17, DECISIONS R4-M52; supersede parts of R4-M37).** G92a, assumed
  completion: on a check-only continuation every node is built and makes its own value and accesses; a node of Type
  Never has no value, its consumer taking an unwritten Never temp that fits any operand position; a placement rvalue
  (`Set`) that has an input and whose every input is that value is not built, and then the old value at `=` is not
  dropped either (`Replace` takes a Never-typed temp when its right side has Type Never), so `x = stop()` leaves x
  unwritten, as today; a Call is still built with its Never operands. Binding gives a Never element of a literal its
  expected Type (α). G92b, the fold: on a reserved input's path, a written exclusive borrow that the path continues
  through is not formed, so the input has one reservation at its lending point; on a shared input's path a written
  exclusive borrow stays a live exclusive Borrow (SPEC 7.3.3, `tasks@uniq.length`; corrected by the planning session on
  the SPEC revision session's finding, DECISIONS R4-M52).
- **E10, MIR contracts decided by the planning session (2026-10-17; recorded in DECISIONS with their units).** G123: an
  operand is read where it is evaluated (a Place operand is secured into a temp when a later operand of the same
  operation builds code). G94: a built-in selection lends its receiver's storage shared where it locates it (a range's
  backing, an element's guard ended once the index is evaluated), amending R4-M40. G91: the Destruction walk enters each
  Type once per query (every walked Type, not only reported parts: the A1–A4 audit's DAG case).
- **E11, a destroying call's observing part (§5 `err(destroy)`; review r3 N4's intent).** The row's exclusion is
  `¬hits(X, place(L), write)` for an `InPlace` target (`uborrow` for the `clear`s), the activation's own kind; as written
  (`¬hits(…, uborrow)`, a deep kind) it never fires, and A3 accepts a `replace` that destroys an observing target while a child lives (26a, SPEC 15.6.6's "in a call that
  destroys a target") and 123.
  Checked at the call on E13 (1)'s activation set for t_K, with E5's seed.

- **E12, the partition row (§5 `partition`, its lemma and §7's boundary; DIAGNOSTICS; decided by the planning session,
  2026-10-17, on the SPEC revision's review, PLAN G113).** The Loan stratum cannot tell a Loan a value carries exactly
  from one it carries by a simplification, so it excludes nothing: it reports every conflict it finds, a `destroy`
  conflict on a Loan `_0` carries included, and `partition` leaves `err(conflict)` (unit 1b-4). A6's merge (unit 1c,
  completed in 2g; E15) drops such a Loan-stratum problem only when the Origin stratum reported the relation of the same
  Borrow as Refuted (one failure, one code), never by Loan membership. A Loan `_0` carries only by a simplification,
  such as P1's whole holders or P12's parent Loans, has no Refuted relation, so its conflict stays: SPEC states those
  simplifications (15.6's holders, 15.6.3–15.6.4), and 36f without its write errs at `return first` (SPEC 15.6.5,
  16.2.2). The lemma's "over-approximated Loans are harmless to exclude" no longer holds. Until the Origin stratum lands
  (2a), a returned borrow of a local is reported by the Loan stratum alone.


- **E13, the values an access takes (the core lane's group review B1 and B2; its design reviews of 2026-10-18, the last accepting
  with wording changes; the owner's choices of 2026-10-18: A5 orders operands (R1) and, after the second design round, one rule for
  every access; prototype round 13, 332 cases; SPEC 3.5, 6.5.1, 7.3.4, 7.7.3, 10.1.1, 12.2, 13.7.1, 15.6, 15.6.3, 15.6.6, 15.6.7;
  decided by the planning session).**
  1. **Values an access takes (SPEC 12.2, 15.6).** An operation evaluates its operands in order before it (a receiver or Value
     callee first, arguments in source order (10.1.1), a setter's value before its receiver (13.7.1), elements, captures and
     operator operands left to right) and uses each operand's value after that operand's access. With o_1 … o_n in that order,
     each access at ℓ is checked on `closure(⋃{σ_in(H) | H ∈ live_out(ℓ), ℓ does not overwrite H} ∪ σ_in(E) ∪ T)`, where T is:
     - (a) for operand i's access: `⋃_{j≤i} val(o_j) ∪ ⋃_{j>i} σ_in(root(o_j))` (its own value included: `s.r = s.n@ref; let t =
       s@move` errs at the Move even with t dead);
     - (b) for the activation of a reservation in t, a destroying call's `destroy` and a call's effects: `⋃_{root(o_j)≠t} val(o_j)`,
       t left out of the live holders and the closure seeded by σ_in(t) (E5); this replaces `CS_call`'s `live_in(ℓ)`, so
       `let r = y@ref; give(y@uniq, r@follow)` is valid;
     - (c) for a Ref's or a Reborrowing PairAcquire's borrow of X: its R2 parents as E3 bounds them
       (`⋃_{m≤j<k} content^j(σ_in(root(X)))`, m the index of the last shared Deref, else 0), whatever its destination's liveness,
       and E5's seed as before (supersedes DECISIONS R4-M57's "a borrow's stored value only seeds", whose reason, over-strictness
       on 30b, no longer holds under E8's ending; without the seed, round 13's 41o is accepted);
     - (d) for an observing destruction of X (a Drop or destroying call whose Type observes): `val(X)` (SPEC 15.6: a Loan is live
       including an observation by its destruction; 15.6.6); a Drop is a terminator whose placed value's store follows it, and a
       destroying call's stored value is an operand whose value row (b) takes, so this row needs no transit; at a destroying call
       val(X) lies in the closure seeded by σ_in(t_X) (E5; E1 keeps X's route fixed), so there it adds nothing;
     - (e) for the destination write and the stores: transit and E5's seed, as before.
     An operand's value is scanned under its root's holder for `mode`, a reservation in its own holder counting as `ref`, except
     at its own call's effects and cleanup, which follow its activation, so there it counts as `uniq` (SPEC 15.6.7: every
     reservation becomes live at once with the call's effects on static storage; needed before unit 2b). Discriminant, Len and
     FakeRead take no value (`val` is ∅); a destruction that observes nothing takes no value; an Opaque site's accesses take
     val(Opaque) (E15).
  2. **Operand order in MIR (amends DECISIONS R4-M55).** MIR reads the Place operands it leaves at ℓ in operand order, a Value
     callee's first, and the builder makes that order the evaluation order: a Place operand is secured into a temp where it is
     evaluated when a later operand builds code (G123) or when an operand evaluated after it precedes it in the operation's
     operand order (named arguments, constructors' included (10.1.1); an unbound reference's receiver supplied after earlier
     parameters, which `MirCallee.Position` passes first (7.3.4); a setter's value (13.7.1)); Case payloads are positional
     (6.5.1). A Closure's captures follow the same rule. An operand that follows no reference (a temp, a whole local, inline
     steps only) has `val(o) = σ_in(root(o))`, so its position is immaterial. Evaluation order is a source fact the MIR does not
     record, so MirValidator cannot check it: it is a lowering contract like G123's (R4-M55), tested by MirBuildTest goldens
     (named arguments, an unbound reference's `self`, a setter, captures).
  3. **Activation.** Every input that is a reservation activates at its Call, a Value callee's operand included (the review's
     B1); R4-M57's "a Call takes t as an argument" reads "as an input".
  4. **Pair-layer acquisitions.** A PairAcquire is always the whole rvalue of an Assign: where it is an operand of another
     operation, the builder secures it into its own temp (supersedes R4-M37's "acquired at its Call"). The acquiring rvalue's
     `Op` holds its `MirRefKind`, `TwoPhaseUniq` when it produces an invocation's input and `Uniq` otherwise, and an Adapt's
     `Op` gains an input bit beside its shorthand bit, read by `MirCases.Adapt` instead of `input: false` (`x@(s/T)` at an
     input; its shorthand test becomes a bit test); `MirBuilder.Defaults`' `FoldOperand`, which re-acquires with `input: false`,
     shares the cause. No closed-vocabulary entry (`MirRefKind` gains no member). The validator admits a PairAcquire only as the
     sole operand of a Use assigned to a local's whole Place (a temp, a local such as `var v = p`, or `_0`), with `Op` `Uniq` or
     `TwoPhaseUniq`; any other Use has `Op` 0; an Adapt's `Op` has only its shorthand and input bits. A5 issues a Reborrowing
     PairAcquire with that kind, its temp the reservation holder when `TwoPhaseUniq`, at its Assign's location, as a Ref's Loan;
     the at-Call PairAcquire Loans (`loanOfOperand`) go. In a view where the pair layer is a reference, a PairLayer step and a
     borrow Deref step are the same following step. R4-M54's "a pair-layer input, exclusive only by case, starts none here"
     gains: its own PairAcquire reserves in the exclusive case, and the fold still does not apply on its path.
  5. **Cost (V16).** One gather per operation builds every access's set: each (holder, node) entry is tagged with the first
     operand whose value takes it and the last operand whose root it is; the closure propagates the tags (least, greatest) in
     operand order; an activation excludes its holder t. Each access scans its own set: V16's per-access term. The taken nodes
     are σ_in of holders live into ℓ or reached by the closure, so §6.3's "live and stored nodes" reads "live-in, stored and
     closure nodes". A scaling shape: one operation with N operands, each carrying a Loan.
  6. **Codes (unit 1c).** An input's own access failure takes the "Otherwise" row (`ComparisonLoanConflict_Kd`); a marked
     PairAcquire's activation takes `CallActivationConflict_Kd`, as its non-generic twin does. A Loan in its reservation holder
     keeps R4-M57's precedence (`CallReservationConflict_Kd`); a Loan found through a value an access takes has the access's
     location as its witness (§6.4).
- **E14, call retention (the core lane's group review B4 and N2; its design review, 2026-10-18; SPEC 15.6, 15.6.4; decided by the
  planning session).** A Call retains as its callee's contract says: `TyCtxt.Retains` over the callee's declaration and Subst, an
  intrinsic's library declaration included (its MIR callee keeps that declaration symbol). A store row into a raw-pointer input
  reaches `E` by the store rule (the refs of a value with no direct Loan are `E`), so `Raw.initialize(p, x@ref)` stores `L_x` into
  `E` and a later `x = 2` conflicts (SPEC 15.6: raw storage is storage the body does not own, and a value stored there may be used
  after every point). The intrinsic table keeps rows only for operands the builder adds beyond a declaration (`RawSlice`'s backing
  → result, Value); an intrinsic without a library declaration (those the builder synthesizes and the compiler-only `$abort`,
  SPEC 17.4) has a result that holds no Loan and retains nothing. A default template's call (until F9) is the unknown-callee row set: every input → result (Value) and → each exclusive
  input's referents; untied. A5 applies every row set through one loop. This replaces §4's "intrinsics: every operand → result",
  which made `clone` carry the handle borrow (SPEC 15.6.4 retains only the payload's Loans, Content) and so rejected
  `entries.append(clone(owner@ref)); _ = owner@move` (the design review's case 135b). Among today's intrinsics only `RawInitialize`
  writes into storage the body does not own (the design review's census); the library's `Raw.initialize` calls store generic or
  parameter values, which already carry `Φ_E`. Prototype round 11 (278 cases) models the store; the design review's extension
  (cases 134a–135c) joins round 12 with E13.
- **E15, group 1c's codes, Opaque values and default taint (the design review of 1c's two designs, 2026-10-18; group 1c's final
  review B1; the owner-approved design, DECISIONS R4-M75; recorded in DECISIONS R4-M68, R4-M70 and R4-M72; SPEC 7.2.3, 8.11.4,
  14.8.3, 15.6.4, 15.6.7, 23.1.2).**
  1. **Activation (R4-M68).** `err(activate)` meets only a Loan whose mode is shared in the holder where it is found: its
     `conflicts(uborrow, mode(L,H))` reads `mode(L,H) = ref`, for the deep activation and a destroying one's shallow write alike. An
     exclusive Loan that hits the reservation R was met where the later of the two Borrows was formed or activated: at R's formation
     (`ComparisonLoanConflict_Kd`), or at its own formation or inner activation (`CallReservationConflict_Kd`, R being shared in its
     holder). So each (Loan, reservation) is reported once (SPEC 15.6.7: the target is checked once).
  2. **err(destroy) as a conflicts row (group 1c's final review B1).** Its condition is an access kind `obsPast` of the target X at
     the destroying call, checked as `err(conflict)` on the activation's set (E11, E13 (1) (b)): `hits(X, place(L), obsPast) :-
     hits(X, place(L), obs), ¬hits(X, place(L), write)`, and `conflicts(obsPast, m)` for every mode m. Its Loan is then chosen as every
     conflict's is (item 4), not by the first holder that holds one.
  3. **Opaque values (R4-M70, option P).** `val(Opaque s) = {Φ_E}`: an Opaque value carries none of its roots' Loans, and `τ_s` leaves
     the Loan sets. Every Opaque site rests on a recorded error, so a Loan problem found only through one never arises, which is
     R4-M10's "derived and dropped" by construction, while a Loan another route reaches stays checked (SPEC 23.1.2). §5's sentence on
     problems derived from s is void. An Opaque site's own accesses are not checked: a problem rooted at one is derived (R4-M10), as
     the code leaves it (PLAN G136); this replaces E13 (1)'s "take val(Opaque)".
  4. **Default taint (R4-M70; temporary, removed by A5-F9, unit 2e).** A value a default's evaluation places into its pending slot D
     (a template call's result, or an inline copy's store into D, a pending slot outside the copy's Inlined scope) carries D's marker,
     the default taint. The per-local markers (element 1 + L) are a role table: `GuardCandidate` → `γ_H` (E7), `PendingSlot` → the
     default taint. A Loan conflict whose Loan only sets carrying a taint hold in the access's check set is a located `Unsupported_Kd`
     at D's source, the omitting call, never a Language problem (SPEC 7.2.3's checks of a default are F9's); a Loan that a set without
     a taint also holds stays the Language problem and is reported first. A holder that joins a tainted and a clean value is tainted
     (accepted until F9).
  5. **The A6 table (R4-M72).** err(effect), a static or unknown-environment effect (SPEC 15.6.4), is `ComparisonLoanConflict_Kd` at
     the call; `CallEffectConflict_Kd` is F6's requirement-call effect `Φ_c` at the later call (SPEC 8.11.4; unit 2f), also where the
     later call's receiver formation meets it (item 1). The `PreparedInput` row has no Loan-stratum producer: a default's copy reaches
     its pending slots only past the prologue's shared borrows (E3), so `DefaultArgumentAccess_Kd` and `DefaultArgumentBorrow_Kd` are
     F9's (unit 2e), and A1 codes a Move of the slot or its owned contents `DefaultArgumentMove_Kd` (R4-M69). `ended` on a
     `GuardCandidate` is `ComparisonLoanConflict_Kd` at the candidate's cleanup, anchored at the candidate (DIAGNOSTICS). A Language
     problem found inside an inline copy's Inlined scope is its template's and is dropped (R4-M69), and a problem carries its body
     (R4-M71).
  6. **The view skip (§6.1; unit 1c-3).** A view skips the stratum only when it has no Loan and no guard candidate: E7's `γ_H` needs no
     Loan, so skipping a Loan-free view accepted a guard value escaping there (the design review's r02). The root flags only select the
     accesses checked.
  7. **E7's and E12's merge (unit 2a).** The merge moves from unit 1c to 2a (2a-2, completed by 2g-5; the step-2 plan's PD11): the
     Refuted relations it reads come from the Origin stratum (A5-F4).

## Revision history

- **A** (step 0): per-root holders, Loan-set nodes, base-named Loans, syntactic access with deep borrows.
- **A2** (review round 1): checks by holder; `step`; the `replace` exemption; P3 (b) and P11; grafts 2 and 4; a monotone
  `Φ_E`; `Retains(callee, Subst)`; the boundary records.
- **A3** (review round 2; owner decision of 2026-10-17): the transit clause; destroying calls' `destroy` access; the
  `obs` cut; no kill across a Deref (23a and 30d listed, root-most as an optional recovery); `InPlace` for `replace` only;
  sound distinct Downcasts; P11 counts; the `Φ_E` watch; one decision table.
- **A3, confirmed** (review round 3): N1 transit uses only live or escaped targets, as the plan's `stored` does; N2 the
  kill passes own Derefs ("borrow Deref"); N3 S1 needs P3 and the own-site check; N4 a destroying call's `destroy`
  reports only the observing part beyond its shallow activation.
- **A3 erratum** (prototype round 7, unit 0b): `excused` also covers the argument reservations of the excused Call (33c),
  so SPEC's `takeTwo` stays valid. For unit 2f (F6).
- **Records** (2026-10-17, no design change): §9 notes the SPEC integration at `4372b058`; §10 states 0b's condition in
  words, keeping round 6's counts as history; §11 marks the items §9 decided and states 0b after prototype rounds 7 and 8.
- **A3 erratum** (prototype round 9, PLAN G113): `out` issues `desc(c)`, `Φ_c` with the receiver's parent Loans, so
  design A rejects 36a–36e and 36g under either base and keeps `takeTwo`. P12 is listed (§9 item 10, pending). The
  review found the reservation-route gap (36h, §11). For unit 2f.
- **Owner decision** (2026-10-17): §9 item 10, P12, approved.
- **Step 1 errata** (2026-10-17, §12; prototype round 10, unit 0b closed): E1 reservation route, E2 derived excuse, E3
  Borrows past a shared Deref, E4 containment wording, E5 own-value seed, E6 Slice index, E7 guard-local marker; owner
  decisions §9 item 11: E8 Loan ending by exact Move-Path prefix (case 23a, option (c)) and E9 MIR contracts; E10 the
  planning session's MIR contracts; E11 the `err(destroy)` exclusion (records review round 2); E12 the partition row (SPEC revision's review).
- **E14** (2026-10-18, §12): call retention through the callee's declaration, intrinsics included (core review B4, N2; its design
  review).
- **E13** (2026-10-18, §12; prototype round 13): the values an access takes, operands in evaluation order in MIR, activation of
  every reservation input, pair-layer acquisitions secured and marked (core review B1, B2; the owner's choices R1 and one rule).
- **E15** (2026-10-19, §12; group 1c's records, unit 1c-9): an activation meets only Loans shared in their holder, err(destroy) a
  conflicts row, Opaque values `{Φ_E}`, the default taint, the A6 table's effect, default and guard rows, the view skip, E7's and
  E12's merge in unit 2a and E13's Opaque accesses (the design review of 1c's two designs; group 1c's final review B1; DECISIONS
  R4-M68, R4-M70, R4-M72).
