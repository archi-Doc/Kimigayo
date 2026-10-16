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
- **Loans**: one per borrow site and view, named by base (S1). A two-phase Loan is `ref` only in its reservation holder.
- **Syntactic access**: borrows and Moves of a Place reach Loans formed through references stored in it (P3; S1's ban
  over Places). There are no resolved access Places and no authority sets.
- **Check sets go by holder, never by Loan id.**
  - Accesses: the entry sets of holders live after P, minus those P overwrites, **plus the transit clause**: the entry
    sets of every value P stores into a holder live after P (directly or through the closure), or into `E`; a store
    through a reference stores into the holders its direct Loans name (owner decision; the plan's `stored`).
  - Activations: holders live into P, minus the reservation holder. This also checks the arguments of one call against
    each other.
- **Kills and destructions**:
  - `step` carries the kill on every rule, through MIR's Temps. The kill never crosses a Deref (plan base: the Place
    before the last Deref).
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
| P4 | §15.6.3 Iterator descent, §15.6.4 step 4 | A result retaining a borrowed input's referent content retains every Loan that content holds; a Closure result retains all captured Loans. | An Iterator or Closure with two independently borrowed sources whose result names one, then a conflict on the other | 0 (no multi-slot struct in lib) / 0 / small (H) | **Needed** (follows from P1) |
| P10 | §15.6 writes through references | Reads and writes through a reference reach every holder its carried direct Loans name, including holders of the references it was reborrowed through. | A Loan-carrying write through `r@follow@follow@uniq`, then a conflict on the intermediate holder | 0 (no `@follow =` in lib) / 0 (the ms writes store owned or Copy values) / small | **Needed** (layer-blind provenance) |
| P11 | §13.7.2, §15.6 | A compound assignment, increment or decrement writes through its two-phase reservation, so it ends none of the destination's earlier Loans. | A compound assignment to a Loan-capable destination, then a conflict with a Loan its old value carried | 0 (39 compound lines: integers, plus one raw pointer `slot += layout.1` at `DictionaryStorage.kimi:41`; raw pointers are not Loan-capable) / 0 (20 lines, all numeric) / 0 (H) | **Needed** by the writeback activation |
| P5 | §15.6.2 key bullet, §22.4.4 | All keys of one static Field are one root. | A live Loan on `Cache<i32>.S.x` against an access to `Cache<string>.S.x` | 0 / 0 (Milestone42: whole reads and writes only) / 0 | Optional (no key Substs, no open-key composition in A4) |
| P6 | §15.6.4 anchors, §15.6.5, §11.8 | A Borrow of mutable static storage is finite, bounded by the body; a value carrying it never leaves the body. | §15.6.4's `current` becomes an error; `leak`, `f = current` and `run` keep their verdicts; §11.8 is unchanged | 0 / 0 / 0 | Optional (no A4 per-result anchors; dissolves c2) |
| P9 | §7.2.3, §15.6.4 | A call of a default template (recursive default SCCs only) retains every prepared input into its result. | A template default holding references that keeps no input, with a reserved input accessed while the callee's result lives | 0 / 0 / corpus ≤6 (H) | Optional (no inter-body `default_retains`) |

**S1′'s own losses** (listed with the plan, not A3's):

| Form | Rejects | lib / ms / tests |
| --- | --- | --- |
| Base operations | The Move, `exchange`/`swap` target, or exclusive borrow other than as a `replace` target, of a base or ancestor while a child is live (prototype 9k, 9l, 14) | counted by the census (plan §6.2) |
| No kill across a Deref (`within`; 23a, 30d) | Replacing the outer holder does not end a child based past a borrow Deref, so later access through the new value conflicts:<br>23a: `c = rr@follow@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7`;<br>30d: a `uniq/Node`-linked traversal that reads through the new node's link.<br>obj links (30a: an own step, so B = `node`) and plain `uniq` links (30b) stay valid. | 23a: 0 / 0 (Milestone33's two-layer follows are compound writes) / ≤8 reborrows in 3 files.<br>30d: 0 (lib links are `isize` indexes, `DictionaryStorage.kimi:21`, `Storage.kimi:50-68`) / 0 / 3 files with reference links, 0 traversal lines. |
| Optional recovery: root-most kill | Sound with P3 (prototype, 92 cases); see below | removes the row above |
| Raw-site form | audit-core row 24 | (H) 0 / 0 / small |

**Optional precision recovery for 23a (owner decision).** In prototype round 6 (92 cases), the root-most kill was sound
in both modes once P3 was present (variants `P3RootMost`, and A with `KillAcrossDeref`). That kill applies when the
holder before the first borrow Deref is destroyed in place. P3 rejects the crossing counterexample (29a) at
`let w = q@follow@ref`, and the root-most kill accepts 23a and 30d. Rules 2 and 3 of plan §4.3's crossing list changed no
case (H). A3 keeps the plan's `within` kill, for consistency with the plan and S1. Adopting the root-most kill removes
the 23a row, provided the plan and S1 change with it.

**Soundness.** P1, P4, P9, P10 and P11 enlarge carried sets or keep Loans the SPEC ends. `content` and `refs` are monotone
in the carried set (`Φ_E`, r1 finding 4), so these losses only add errors. P3 adds conflicts and is what makes syntactic
comparison sound (§3). P5 enlarges overlap. Under P6, the Origin stratum Refutes every chain from such a borrow to a fixed
Origin, so no call result carries an anchor.

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
- **Inline steps**: Field, Base, ConstIndex, Downcast, Index.
- **Overlap** is prefix comparison:
  - distinct Fields, Tuple elements and constant indices are disjoint;
  - an Index meets every index of its base;
  - distinct Downcasts are disjoint. This is sound with transit: a variant changes under a live payload Loan only by a
    whole write, which meets the Loan even in the stored value, or by a formation the deep ban rejects (r2 finding 8);
  - distinct roots are disjoint, references included (row 8; sound under P3 and the caller's activation rule, S15:614).

**Loans.**

- A Loan exists per non-raw `Ref` site and view, with its Place, its `base(L, B)`, mode, issue location and destination
  root.
- B is the plan's base: the Place before the *last* borrow Deref, with own (`obj`/`rc`/`arc` payload) Derefs counted as
  storage, as design B does. B matters only for the kill; the ban and provenance use the Loan's whole Place and direct
  Loans, so per-root holders need no other base.
- **Direct** Loans have no borrow Deref and name `root(place)`.
- A `TwoPhaseUniq` Loan's *reservation holder* is its issue destination; the Loan is `ref` only there.

**Ban and kill** (plan fe1a5b34):

- **Ban more.** A deep access of Place K reaches every Loan whose Place has K as a prefix, compared syntactically across
  Deref steps; an Index matches any index that §15.6.2 does not prove distinct. This contains `runs_through(B, K)` for
  any B ⊑ place(L), so `exchange(arr[j]@uniq, …)` meets a child based at `arr[i]` (22a).
- **Kill less.** The plan's `within`: `ended` needs a whole in-place destruction of B or of a move-path ancestor of B,
  with no borrow Deref step between them and Index steps only for identical constant indexes. Own (`obj`/`rc`/`arc`
  payload) Derefs are storage, so the kill passes them; this is sound, because the handle's storage is destroyed with
  its owner (r3 N2), and the plan's `within` should say "borrow Deref" too. A B containing a borrow Deref is never
  ended. A write through a non-path index ends nothing.
- **P1 changes neither rule.** Both are on Places; P1 only decides which holder carries what.
- **No kill across a Deref** (planning decision, overriding r2 finding 4). The plan's (H) "does `q = …` end a Loan based
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
| `base(L, B)`, `direct(L)`, `named(L, T)` | Loan | Place projections, step classes | derived (pre-pass) |
| `holder(H)`, `flowHolder(H)` | local | `MirTypes.HoldsLoans(cases.Type(…))`, `MirMoveData.FlowCount` | input |
| `access(ℓ, X, k)`, `use(H, ℓ)`, `kill(H, ℓ)` | location | `MirUses` rows plus statement kind (as `MirInit.cs:112-121`) | input |
| `stores(ℓ, v, dest)` | location | an Assign or Call destination, a write through a reference, a store retention into referents | input |
| `destroys(ℓ, K, elements)` | Call | callee's published row `Destroys = i` (`TyCtxt.Published`): K = the Place of the Ref producing argument i; `elements` for the `clear`s | input |
| `destroyed_in_place(K, ℓ)` | Place | a whole assignment of direct K; StorageLive/StorageDead of root K; a `destroys` row that also has `InPlace` (`replace` only; `clear` only under relaxation (c)) | input |
| `destroying(site)` | Ref site | the TwoPhaseUniq Ref producing an `InPlace` argument | derived (pre-pass) |
| `ended(L, ℓ)`, `step(L, ℓ, ℓ′)` | Loan | `base(L, B), destroyed_in_place(K, ℓ), within(B, K)` (move paths: no Deref, Index only for identical constant indexes); `step :- edge, !ended` | derived |
| `base_op(ℓ, K)` | Place | a Move, a borrow (P3), an `exchange`/`swap` target, the destruction of an old value whose drop may reach a base below K (`obs`: MIR's separate Drop at `=`, a destroying call such as `replace`), except a `destroying` Ref | derived (deep accesses) |
| `reserved(L, H, ℓ1, ℓ2)` | site | TwoPhaseUniq issue into H at ℓ1; its Call, or the writeback write through H, at ℓ2; reserved only in H | derived |
| `observes(T)` | Type | `MirTypes.Observes(case Type)`; for a Drop also its group verdict ≠ Elide (`M/Analysis/MirDrops.cs:88`) | input |
| `retains(c, i, κ, o)`, `untied(c)` | (callee, Subst), slot | Binding `TyCtxt.Retains(callee, Subst)` (C1/C8), `TyCtxt.Closure` (C2); intrinsics: every operand → result | input |
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

`EndBorrow` (`M/MirVocabulary.cs:21`) is restated: "a use of the local, after which every Loan issued into it must be
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
conflicts(k, m)    :- m = uniq ; k ∈ {write, move, uborrow, destroy, obs}.
mode(L, H)         :  Shared → ref; Uniq → uniq; Φ_c → uniq; TwoPhaseUniq → ref iff H is L's reservation holder.
content(S)         =  ⋃{σ(T) | L∈S, named(L,T)} ∪ (σ(E) if Φ_E∈S)            // monotone in S
refs(S)            =  {T | L∈S, named(L,T)} ∪ ({E} if Φ_E∈S);  refs^k(S) = refs(content^(k-1)(S))
val(Aggregate|Cast{Erasure,Upcast,Pointer}) = ⋃ val(operands);  val(BinaryOp|UnaryOp|Discriminant|Len|Const) = ∅
val(Opaque s)      =  ⋃ σ(roots of s) ∪ {τ_s, Φ_E};  val(static or raw-derived Place) ∪= {Φ_E};  Adapt per MirCases

// ---- Carrying (forward)
σ(H, entry)        =  {Φ_E} for a Loan-capable Arg, else ∅;  σ(E, entry) = ∅
ended(L, ℓ)        :- base(L, B), destroyed_in_place(K, ℓ), within(B, K).   // move paths; never across a Deref
step(L, ℓ, ℓ′)     :- edge(ℓ, ℓ′), !ended(L, ℓ).   // every carrying rule takes the step: σ_out(K) \= ended(ℓ), ∀K and E
issued(L,X,_,ℓ,H)  ⇒  σ_out(H) = {L} ∪ ⋃_{j<k} content^j(σ_in(root(X)))           // R2: parents of every followed layer
assign H := rv  (whole root)         ⇒ σ_out(H) = val(rv)                          // complete replacement
assign D := rv  (direct, D ⊐ H)      ⇒ σ_out(H) = σ_in(H) ∪ val(rv)                // P1
assign D := rv  (k ≥ 1 borrow Deref) ⇒ ∀T ∈ refs^k(σ_in(root(D))): σ_out(T) ∪= val(rv)   // S15:454, S4: ends nothing
move H (whole root)                   ⇒ σ_out(H) = ∅
StorageLive | StorageDead | EndBorrow H ⇒ σ_out(H) = ∅
join               :  per-slot union; P1/P2 as MirDataflow; prune(head): σ(H) = ∅ if ¬live_in ∧ no Loan names H

// ---- Calls (F3): c at ℓ, arguments a_i, destination D
ret(i, Value)      =  val(a_i) ∪ (content(val(a_i)) if a_i is a reference);   ret(i, Content) = content(val(a_i))
out                =  ⋃{ret(i,κ) | retains(c,i,κ,result)} ∪ ({Φ_E} if untied(c)) ∪ ({Φ_c} if abstract(c), descent)
                   ⇒  assign D := out;   ∀ retains(c,i,κ,store j), ∀T∈refs(val(a_j)): σ_out(T) ∪= ret(i,κ)
                   // a retained TwoPhaseUniq Loan has left its reservation holder, so it counts as activated
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
CS(ℓ)              =  closure( ⋃{σ_in(H) | H ∈ live_out(ℓ), ℓ does not overwrite H} ∪ σ_in(E) ∪ transit(ℓ) )
CS_call(ℓ, t)      =  closure( ⋃{σ_in(H) | H ∈ live_in(ℓ), H ≠ t} ∪ σ_in(E) )      // t: the reservation holder
access(ℓ,X,k)      :- Copy|FakeRead|Discriminant|Len → read; Move → move; Ref Shared|TwoPhaseUniq|Raw → sborrow,
                      a destroying Ref → read (shallow); Ref Uniq → uborrow; destination → write;
                      StorageDead → destroy;  Drop → destroy, plus obs if observes(Type(X)).

err(conflict,L,ℓ)  :- access(ℓ,X,k), L ∈ CS(ℓ) via H, real(L), hits(X, place(L), k), conflicts(k, mode(L,H)),
                      !excused(L,ℓ), !partition(L,ℓ,k,X).
err(activate,R,L,ℓ):- reserved(R,t,_,ℓ), L ∈ CS_call(ℓ,t) via H, real(L), hits(place(R), place(L), uborrow),
                      conflicts(uborrow, mode(L,H)), !excused(L,ℓ).     // a destroying R: hits as write (shallow)
err(destroy,L,ℓ)   :- destroys(ℓ, K, e), X = (K's elements if e else K), L ∈ CS_call(ℓ,t_K) via H, real(L),
                      observes(Type(X)), hits(X, place(L), obs), ¬hits(X, place(L), uborrow),
                      conflicts(destroy, mode(L,H)).   // at the call, on σ_in, so before ended
                      // only the obs part beyond the shallow activation: the target's own Places are already
                      // checked by the call's activation, so one Loan never gets two codes (r3 N4)
// Arguments of one call (S15:614): every exclusive argument borrow is a reservation (G5), and its activation scans the
// other arguments' holders, live into ℓ, where a reservation counts as ref. A non-reserved exclusive argument is a
// stored borrow, live when formed, so an overlapping later argument conflicts at its own formation.
err(effect,L,ℓ)    :- key(ℓ,f,k), L∈CS_call(ℓ,−) via H, root(place(L)) a static of Field f (P5), conflicts(k, mode(L,H)).
err(effect,L,ℓ)    :- unknown(ℓ), L∈CS_call(ℓ,−), root(place(L)) a mutable static.  // calls, erased Drops (S16:196)
err(ended,L,ℓ)     :- EndBorrow H at ℓ, issued(L,_,_,_,H), L ∈ CS(ℓ).               // guard-local Origin end
partition(L,ℓ,k,X) :- k = destroy, root(X) a body root, L ∈ σ_in(_0) and L in no other part of CS(ℓ).   // DIAGNOSTICS

// ---- F6 preserves results (S2)
disturbed at ℓ     :- ℓ writes, Moves or destroys a direct D ⊑ recv(c), or writes through a Place hitting recv(c)
                   ⇒  Φ_c is replaced by Φ_c^dist in every σ (by holder, like ended)
excused(L,ℓ)       :- Call c2 at ℓ, preserves(c2), L = Φ_c1 (not ^dist), callee(c1) = callee(c2), recv(c1) ≡ recv(c2).
// Φ_c: place = recv(c)'s referent with its content, mode uniq, issued into the result of an abstract requirement or
// Callable call whose result descends (§8.11.4). Conformance (H, S2 text): an escaped Φ_prev on (*self) with a fresh
// anchor at entry; a self-Field-path call of a preserves requirement undisturbed since entry is excused against it.

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
| Effect | `CallEffectConflict_Kd` |
| A `PreparedInput` root (R4-M10) | `DefaultArgumentAccess_Kd` / `DefaultArgumentBorrow_Kd` |
| `ended` on a `GuardCandidate` | DIAGNOSTICS choice (§9) |
| Otherwise, including `destroy` | `ComparisonLoanConflict_Kd` |

A problem whose access is an Opaque site's, or whose witness holder carries `τ_s`, is derived from s.

**Partition lemma** (DIAGNOSTICS): every Loan that exactly reaches `_0` passed a fit into the result's fixed Origin, so a
body-rooted one is the Refuted relation (S15:575). Over-approximated Loans are harmless to exclude.

## 6. Evaluator

### 6.1 Algorithm and data

Per body view, in `MirAnalysis` step 4 (after A4's `Run`; `flow.Begin` and `moves.Build` are redone per body):

1. **Pre-pass**, one `MirUses` walk: holders, Loans (CSR by place root and base root), `destroys`/`destroying` sites
   and per-root conflict flags. A view with no real Loan, or with no access that can conflict by root flags, skips the
   stratum (census: 161 + 266 + 16 Loan-free views).
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

**Containment** (row 9c): a Ref through borrow Derefs adds `contain(o_k, o_new)` per layer, up to and including the
first shared layer, the same cut as `obs` (`MirCases.Layer` for pair layers). Edges between fixed atoms are entailed by
Binding's meet.

**Boundary.** Each failure is reported once, by partition.

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

## 10. Implementation units (plan §6.2)

| Unit | Content | Completion condition | Checks |
| --- | --- | --- | --- |
| 0a | Ledger re-baseline at the step-0 base (census §3 command) | `ledger.tsv`, `summary.md` recorded | V13 failure-set equality |
| 0b | Prototype into `draft/Sketches`.<br>Recorded: the 92 cases, including 25a–25c (transit, dead store), 26a/26b (destroying calls), 27 (cut), 28a–28d (plain arguments: E, V, V, E), 29a–29c (crossing, escaped, path-exact) and 30a–30d (linked lists).<br>Added: `swap(a, b)` with parameters, `y = h@follow + 1`, `Φ_c^dist`. | All match the A column (70 same, 14 earlier, 8 listed losses) | `proto.cs` run |
| 0c | G1 `MirDataflow` | A1 behavior-preserving; a backward test problem | `MirDataflow`/A1 tests, `BodiesScaleLinearly`, ledger unchanged |
| 0d | G2 | Classified over library Types | Unit tests (`Logger` S15:598-604, abstract and Sealed parts) |
| 0e | Builder contracts G5, G6, writeback, Slice, Guard, C8 | Dump goldens updated; validator clean | `MirBuildTest`, goldens |
| 0f | A4 `Keys` | Keys per Call/Drop, linear | `MirEffects` tests, scaling |
| 1a | Binding C1+C8 `Retains(callee, Subst)`; catalog `InPlace` | Immutable after Bind; allow list | View tests, `KimiLibraryShapeTest` |
| 1b | `MirLoanSets`, `MirBorrows` F1, F2, F3, F10 (joint core) | Library and milestones produce no A5 problem (R4-M12 from here). Shadow watch: raw writes in `Storage.kimi` and `DictionaryStorage.kimi` moving Loans into `σ(E)` | SPEC-example table (`tests/diagnostics/spec-cases.json`: about 40 error and 34 accepting variants); 0b's cases as MIR-layer tests; scaling shapes 1–8 |
| 1c | A6 mapping and derived taint for 1b's codes | Codes per §5 | Corpus snapshots, ledger |
| 2a0 | Binding boundary: OriginObligations, atoms, universal relations, T1 rows, unowned items, `ValidateOriginRelations` split, ledger split | Rows immutable; no double owner | View tests, ledger |
| 2a | F4 `MirOrigins` | audit-origins §3 tests; the old reporter's rows matched | Shape 9, ledger |
| 2b | F5 (keys, unknown, P6) | S15:540-558 per P6; S16:199-202 | Rule tests |
| 2c | C2 view, then F7 | S15:715-723, S7:341-344 | Rule tests |
| 2d | F8: updates, match, guards, writeback, collections, destroying calls | S14:332-351, S15:650-689, S13:600-602, S4:570-575 | Rule tests |
| 2e | F9 defaults | S7:84-88; audit-calls row 26 | Rule tests |
| 2f | C5 view, then F6 (S2) | `takeTwo`, `takeAcross`, Drain, Merge; 12 library conformances accepted | Rule tests, library corpus |
| 2g | E4/E5 keys, A6 completion | Derived rules per DIAGNOSTICS | Corpus |
| Exit | R6-check deletion list (§7, §8), test migration | Every family complete, no `Unsupported_Kd` family | Session verify, final review |

## 11. Risks and open questions

- **P3 form (b)** may be a SPEC soundness gap rather than a loss: z gives shared access to x while x is written through
  r. The old path rejects it. Ask the owner.
- **`Φ_E` in the library.** Raw-derived or static-derived writes move Loans into `σ(E)` forever. Watch `Storage.kimi` and
  `DictionaryStorage.kimi` in 1b's shadow run (r2 finding 7).
- **Round 6** (92 cases) shows no unsound acceptance and no unlisted rejection. Pending in 0b: `Φ_c^dist`,
  `swap(a, b)` with parameters, `y = h@follow + 1`.
- **Other open items**: S1 without P3 (§9 item 4); relaxation (c) and V16 (§9); F6 (waits for S2); Binding views
  (snapshots); R5 (serialized, plan §9.1); A4's depth limit (S5).

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
