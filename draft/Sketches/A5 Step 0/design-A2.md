# A5 design A2: Polonius-style borrow checking on MIR (revision of design A)

Base: worktree `bin/worktrees/a5-step0` (`b2edea8b`); plan `fe1a5b34` §4.2–§4.3. Read-only; nothing was built or run.

- **Paths**: `M/` = `src/Kimi/Compiler/Mir/`, `B/` = `…/Binding/`, `A/` = `…/Analysis/`. `S15:n` is a SPEC line.
- **Sources**: "review" is `review-design.md`; "prototype" is `prototype.md` rounds 1–4, 66 cases (39 + 10 + 11 + 6).
- **Assumptions**: S1 is the plan's S1 (with its `replace` exemption) as corrected to S1′; S2–S5 are adopted. The plan's
  names `base`, `destroyed_in_place`, `ended`, `step` and `base_op` are used.
- (H) marks an unverified hypothesis.

## 1. Summary

- **Holders** are root locals plus an escaped pseudo-holder `E` (P1), each carrying an interned Loan-set node
  (`MirLoanSets`).
- **Loans**: one per borrow site and view, named by base (S1). A two-phase Loan is `ref` only in its own reservation
  holder; every other holder carries an earlier, activated incarnation.
- **Syntactic access**: every borrow and Move of a Place reaches Loans formed through references stored in it (P3,
  Rust's rule; the ban over Places). There are no resolved access Places and no authority sets.
- **Check by holder, never by Loan id** (finding 1, prototype round 4). Accesses use entry sets over holders live after
  P, minus those P overwrites. Activation uses holders live into P, minus its reservation holder, which also checks the
  arguments of one call against each other.
- **`step`** carries the kill on every rule, through MIR's Temps (finding 2). The kill needs whole in-place destruction
  of a direct Place. The `replace` exemption comes from the published-row column `Destroys`.
- **Provenance only for content** (direct Loans, materialized parents, `E` via `Φ_E`, issued for unknown provenance).
- **Passes**: backward holder liveness, forward Loan sets (join hook), one replay. **Origins**: A5 takes over the old
  Analysis's role; Binding's fixed–fixed body judgments move at R7.
- **Size**: about 1,860 Mir and 450 Binding lines against 15,646 deleted. **Speed**: needs the owner's V16 restatement
  (§6.3).

## 2. Precision-loss list (OD13 rows)

Each row is a rule clause of §5 with its own fact (plan §5.3). Counts: lib, ms (43 milestones), tests (xUnit
literals, grep proxies). The review spot-checked P1 (`Milestone33.kimi:70`, `Milestone16.kimi:36`): no rejection.

| # | SPEC | Rule clause (simplified rule) | Rejects | lib / ms / tests | Class |
| --- | --- | --- | --- | --- | --- |
| P1 | §15.6 (S15:454 "Move Path granularity") | Holders are variables, temporaries and parameters. A write or Move of a part adds the written Loans and ends none; a read of any part reads all of the holder's Loans; a Drop that may observe Origins uses the whole holder. | `a.0 = z@ref; x = 4; inspect(a)`; reading or destructuring one part of a local aggregate built from several borrows, then conflicting with another part's source; `refs[0] = z@ref` ending `refs[0]`'s Loan; prototype 5b, 7, 18a | 0 / 0 / ≤10 tuple literals in 5 files | Needed (no subtree ranges, no G7 fix in `MirMoveData`) |
| P3 | §15.6.2 "Inline parts exclude referents" (S15:500) | **Loans cover referents.** A Move or a borrow of a Place reaches every Loan on a Place reached through references stored in it. A read, a write and a destruction do not; a destruction in place ends those Loans (S1). An access through `r` reaches Loans on `r` and on its holders. | (a) `let z = r@ref`, or a shared receiver borrow of a holder of `r`, while a conflicting Loan formed through `r` is live; (b) an access through `r` while a conflicting Loan on `r` is live: `let z = r@ref; r@follow = 5; inspect(z)` | 0 / 0 (no shared borrow of an exclusive-reference variable) / 3 rough hits; the old path already rejects (b) (`OwnedStoredReferenceTest.cs:230-235`) | Needed (no access resolution or authority); form (b) may close a SPEC soundness gap (§11) |
| P4 | §15.6.3 Iterator descent, §15.6.4 step 4 | A result retaining a borrowed input's referent content retains every Loan that content holds; a Closure result retains all captured Loans. | An Iterator or Closure with two independently borrowed sources whose result names one, then a conflict on the other source | 0 (no multi-slot struct in lib) / 0 / small (H) | Needed (follows from P1) |
| P10 | §15.6 writes through references | Reads and writes through a reference reach every holder its carried direct Loans name, including holders of the references it was reborrowed through. | A Loan-carrying write through `r@follow@follow@uniq`, then a conflict on the intermediate holder | 0 (no `@follow =` in lib) / 0 (the ms writes store owned or Copy values) / small | Needed (layer-blind provenance) |
| P11 | §13.7.2, §15.6 | A compound assignment, increment or decrement writes through its two-phase reservation, so it ends none of the destination's earlier Loans. | A compound assignment to a Loan-capable destination, then a conflict with a Loan its old value carried | 0 / 0 / 0 (H; compound operators on Loan-capable Types) | Needed by graft 2 (writeback activation) |
| P5 | §15.6.2 key bullet, §22.4.4 | All keys of one static Field are one root. | A live Loan on `Cache<i32>.S.x` against an access to `Cache<string>.S.x` | 0 / 0 (Milestone42: whole reads and writes only) / 0 | Optional (no key Substs, no open-key composition in A4) |
| P6 | §15.6.4 anchors, §15.6.5, §11.8 | A Borrow of mutable static storage is finite, bounded by the body; a value carrying it never leaves the body. | §15.6.4's `current` becomes an error; `leak`, `f = current` and `run` keep their verdicts; §11.8 is unchanged | 0 / 0 / 0 | Optional (no A4 per-result anchors; dissolves c2) |
| P9 | §7.2.3, §15.6.4 | A call of a default template (recursive default SCCs only) retains every prepared input into its result. | A template default holding references that keeps no input, with a reserved input then accessed while the callee's result lives | 0 / 0 / corpus ≤6 (H) | Optional (no inter-body `default_retains`) |

**Assumed from S1′ (not mine)**: Move, `exchange`/`swap` target, or exclusive borrow other than as a destroying target,
of a base or ancestor while a child is live (prototype 9k, 9l, 14); the raw-site form (audit-core row 24). The `replace`
exemption is kept (prototype 15a needs it, 15b shows it sound), so design A's `replace` narrowing is withdrawn.

**Soundness.** P1, P4, P9, P10 and P11 enlarge carried sets or keep Loans the SPEC ends. `content` and `refs` are monotone
in the carried set, because `Φ_E` replaces the old "no named Loan" fallback (finding 4). So these only add errors. P3 adds
conflicts and is what makes syntactic comparison sound (§3). P5 enlarges overlap. P6 makes the Origin stratum Refute every
chain from such a borrow to a fixed Origin, so no call result carries an anchor.

## 3. Holders and references

- **Holder**: a root local whose case Type may hold a Loan (`MirTypes.HoldsLoans`: a borrow layer, pair layer, Slice,
  Function or Closure Type at any inline depth), plus `E`. Flow holders are roots whose `MirMoveData` root path is a flow
  path (`M/Analysis/MirMoveData.cs:64`); chain-local holders live in the scratch, as in A1.
- **Steps** (graft 4, B's classes, compared on `MirBody.Projections` with no A5 interning; DEC:583):
  - A Deref is **borrow** (`ref`, `uniq`, `objref`, `objuniq`, or a PairLayer the view makes a reference), **own** (an
    `obj`/`rc`/`arc` payload, part of the handle's storage, which gives SPEC 13.5.5's handle Loan and S16:194) or
    **raw** (a fresh anchor, compared only with Places derived from it, S15:496).
  - Field, Base, ConstIndex, Downcast and Index steps are inline.
- **Overlap**: prefix comparison. Distinct Fields, Tuple elements, constant indices and **Downcasts** are disjoint (the
  Downcast case is a relaxation for the owner). An Index meets every index of its base. Distinct roots are disjoint,
  references included (row 8 relaxation; sound under P3 and the caller's activation rule, S15:614).
- **Loan**: per non-raw `Ref` site and view, with its Place, its `base(L, B)`, mode, issue location and destination
  root. B is the Place before the first borrow Deref (the first move path the borrow path enters, as in the prototype; a
  Field, ConstIndex or Index step may follow the root). **Direct** Loans have no borrow Deref and name `root(place)`. A
  `TwoPhaseUniq` Loan's *reservation holder* is its issue destination; it is `ref` only there.
- **Ban and kill** (plan fe1a5b34):
  - *Ban more*: a deep access of Place K reaches every Loan whose Place has K as a prefix. The comparison is syntactic,
    across Deref steps, with an Index matching any index that §15.6.2 does not prove distinct. This contains the plan's
    `runs_through(B, K)` for any choice of B ⊑ place(L), so `exchange(arr[j]@uniq, …)` meets a child based at `arr[i]`.
  - *Kill less*: `ended` needs a whole in-place destruction of a direct Place K (Fields and constant indices only, no
    Index or Deref) with K ⊑ B and no Deref between. A write through a non-path index ends nothing.
  - P1 changes neither rule: both are on Places, and P1 affects only which holder carries what.
  - Ending at the root-most base on a two-layer reborrow (`rr = …` ends a Loan at `rr.*.*`) follows prototype 13a. The
    plan leaves `q = …` for a base `(*q).p` to S1 as (H).
- **Markers**, which never conflict: `Φ_E` ("may refer to storage the body does not own") and `τ_s` (Opaque site s, for
  A6).
  - `Φ_E` is carried by every Loan-capable value of unknown provenance: Args at entry, reads of static Places, reads
    through raw pointers, Opaque values, results of unknown callees, and call results with an untied result Origin
    (`Retains.Untied`).
  - A *phantom* `Φ_c` does conflict (F6).
- **Content of a carried set S**: the nodes of the holders S's direct Loans name, plus `σ(E)` if `Φ_E ∈ S`. Child Loans
  are skipped, because a reborrow's holder carries its parents (prototype R2).

**Why syntactic access is sound.** Under S1′ + P3 an alias of a referent is one of four kinds:

- a direct Place, which meets the parent Loan every child holder carries;
- the same base path, compared syntactically;
- one formed by a borrow or Move of a prefix, which conflicts at formation;
- a parameter, disjoint by the caller's activation rule.

rc and arc payloads are read-only (S15:509). The argument held on all 66 prototype cases.

**Liveness through references** is a closure at check time: a live direct Loan naming T makes `σ(T)` live. Borrowing T
is a use of T, so no backward feedback is needed (prototype 4b, 17a, 17b).

**Rejected**:

- Origin positions: they need variance-directed flow, and R4-M24 stalled on them.
- Move-path holders: about 150 lines plus the G7 fix.
- Dense holder × Loan bits: cubic.
- Per-Loan traversal: Loans become interdependent.
- R3 authority sets.
- Exclusion by Loan id: unsound (finding 1).
- B's descendant Loan (graft 1): a loss against S15:517 for concrete Iterators.

## 4. Fact schema

Inputs come from producers; derived facts come from §5. One Semantics case view at a time (R4-M11).

| Relation | Keys | Producer | Kind |
| --- | --- | --- | --- |
| `edge(ℓ, ℓ′)`, P1/P2 | location | `MirDataflow` chains, `MirReach` | input |
| `issued(L, X, mode, ℓ, H)` | site | `Ref` rvalues under the view (`MirUses.ForEach(MirCases, …)`) | input |
| `base(L, B)`, `direct(L)`, `named(L, T)` | Loan | Place projections, step classes | derived (pre-pass) |
| `holder(H)`, `flowHolder(H)` | local | `MirTypes.HoldsLoans(cases.Type(…))`, `MirMoveData.FlowCount` | input |
| `access(ℓ, X, k)`, `use(H, ℓ)`, `kill(H, ℓ)` | location | `MirUses` rows plus statement kind (as `MirInit.cs:112-121`) | input |
| `destroyed_in_place(K, ℓ)` | Place | a whole assignment of direct K; StorageLive/StorageDead of root K; a Call whose callee's published row destroys argument i's target (`TyCtxt.Published(…).Destroys`, `KimiLibraryCatalog` row `Target`: `replace`, `Array.clear`, `Dictionary.clear`), K = the Place of the Ref that produced argument i | input |
| `destroying(site)` | Ref site | the TwoPhaseUniq Ref producing that argument (reservations do not pass through storage) | derived (pre-pass) |
| `ended(L, ℓ)`, `step(L, ℓ, ℓ′)` | Loan | `base(L, B), destroyed_in_place(K, ℓ), K ⊑ B` with no Deref between (K direct, no Index); `step :- edge, !ended` | derived |
| `base_op(ℓ, K)` | Place | a Move, borrow (P3), `exchange`/`swap` target or observing Drop of K, except a `destroying` Ref | derived (as deep accesses) |
| `reserved(L, H, ℓ1, ℓ2)` | site | TwoPhaseUniq issue into H at ℓ1; its Call, or the writeback write through H, at ℓ2; reserved only in H | derived |
| `observes(ℓ)` | Drop | `MirTypes.Observes(case Type)`, group verdict ≠ Elide (`M/Analysis/MirDrops.cs:88`) | input |
| `retains(c, i, κ, o)`, `untied(c)` | (callee, Subst), slot | Binding `TyCtxt.Retains(callee, Subst)` (C1/C8), `TyCtxt.Closure` (C2); intrinsics: every operand → result | input |
| `key(ℓ, f, k)`, `unknown(ℓ)` | Call or Drop | A4 `MirEffects.Keys` | input |
| `preserves(c)`, `recv(c)`, `abstract(c)` | call | `DispatchBounds`/`CallableBounds` (`B/Binding.EffectObligations.cs:83,98`); receiver Place; Requirement or abstract Callable callee | input |
| `σ(H, ℓ)`, `σ(E, ℓ)`, `live(H, ℓ)`, `CS(ℓ)`, `err` | holder, location | §5 | derived |
| `obligation`, `atom`, `entails`, `contain` | definition, Borrow | Binding `OriginObligations`, `OriginAtom.Of`, `UniversalRelations.For`; MIR Ref Places | input / derived (F4) |

### 4.1 MIR fact contracts (step 0)

| Gap | Contract | Change |
| --- | --- | --- |
| G1 | `MirDataflow.Backward` on the same chains: a P1 chain joins runtime successors only, a P2 chain all (R4-M24's reading). P1 phases come from `Begin`. Both problems share the per-head arena (liveness words, then node slots), and the join is the problem's hook (`IMirFlow.Join(stored, incoming, first)`; A1 keeps OR). | `MirDataflow.cs` +90, `MirInit.cs` +4 |
| G2 | `MirTypes.HoldsLoans`, `Observes` (the `Destruction` walk reports a user `drop`, an abstract part, a Function value or a non-Sealed handle) | `MirTypes.cs` +40 |
| G3 | **Departure from plan §3, recorded in DEC**: no drop-row Path. A5 reads only the verdict; it runs only on bodies A1/A2 analysed, so Group −1 means check-only. | none |
| G4 | **Closed by design** (DEC): A5 switches on the statement kind; `MirAccess.Kills` stays. | none |
| G5 | Every exclusive borrow producing an invocation's receiver or argument is `TwoPhaseUniq` (`MirBuilder.Values.cs:83, 175, 232`; `MirBuilder.Expansions.cs:396, 443`; `MirBuilder.Formatting.cs:70-87`). | builder |
| G6 | An invocation whose operand diverges continues in that operand's CheckTarget continuation; pending `EndBorrow`s go on the transfer's runtime edge only (`MirBuilder.Control.cs:135-142`, `MirBuilder.Calls.cs:116-120`). | builder |
| G7 | **Not needed** (P1; DEC). | none |
| Writeback (graft 2) | Compound assignment, increment and decrement form `$w = TwoPhaseUniq(dest)` at locate and write the result through `$w` (S13:595, `MirBuilder.Expansions.cs:127-240`). **TwoPhaseUniq meaning** (`M/MirVocabulary.cs:113`): activated at the Call it is an argument of, or at the write through it; `EndBorrow` never activates. | builder, vocabulary doc |
| Slice | A built-in range selection borrows its whole backing (`Shared`, or `TwoPhaseUniq` for a `uniq` Slice) and passes it as `RawSlice`'s first operand (arity 2→3, `M/MirDescriptors.cs:146`). The intrinsic rule "every operand → result" makes the Slice carry it, closing the unsound S4:570-575 acceptance. | builder, intrinsic row |
| Guard | `EndBorrow` of each `GuardCandidate` local at guard cleanup on every path (S14:325-336). | builder |
| C8 | A Value callee's `MirCallee.Arg` is its selected Callable signature; a Virtual callee's `Arg` is its slot (`MirBuilder.Calls.cs:131`). | builder, `MirBody.cs` doc |
| E-keys | `MirOpaque` carries its Binding failure-cause keys (A6's representative cause = the minimum key). | Binding, builder |

`EndBorrow` (`M/MirVocabulary.cs:21`) is restated: "a use of the local, after which every Loan issued into it must be
dead; its carrying ends". This is trivial for abandoned comparisons and reservations, and is the guard-local Origin end
for guards (S14:336).

### 4.2 A4 publication

`MirEffects.Keys(body, view, block)` → `(bool Unknown, ReadOnlySpan<(int Field, bool Write)>)` for the Call or Drop at a
block. It is the transitive union over A4's worklist, stored as a bitset per summary node over the program's mutable
static Fields (P5: no Subst, no open keys; `MirBody.Static` is already keyed by symbol, `M/MirBody.cs:441`). There are no
per-result anchors (P6). A4 reads nothing from A5. Without P5, `MirStatic` gains Origin-erased owner Type arguments and A4
composes keys through each Subst with the open-key rule (about +120 lines). A4's depth limit is plan S5.

### 4.3 Binding views, each landing just before its family (§10)

| View | Shape | Family |
| --- | --- | --- |
| C1+C8 | `TyCtxt.Retains(callee, Subst)` and `(Callable signature, Subst)`, memoized per pair → `Retention(int Input, RetainKind Kind, int Output)` plus `bool Untied`. Instantiate the signature (`TyCtxt.Signature`/`Apply`); nodes are its Origin atoms, with still-abstract Type parameters and projections as carrier atoms and a projection joined to its base. Edges are substituted clauses and well-formedness. Input i retains to output o if o's atom is reachable from i's. `Value` if reached from a reference input's outer Origin, `Content` if only from inside its referent. `Untied` if a result atom is unreached and not `static`. This keys by the instantiated contract, as S15:529 requires (finding 9). | step 1 |
| C1-F4 | `TyCtxt.OriginObligations(definition)` immutable `(SourceRef Site, BoundOrigin Longer, BoundOrigin Shorter, ulong CaseMask)`: a snapshot of `BindingObligation` rows (`B/Binding.Origins.Model.cs:154-162`) at the end of Bind, without clauses Binding proved or failed as Constraints (T1 rule). Also `OriginAtom.Of`, `UniversalRelations.For(definition, case)`. | F4 |
| C2 | `TyCtxt.Closure(k)` → per view, receiver mode and `Retention` rows; removes `B/Binding.ValueCalls.cs:428-442, 531-543` `Unsupported` | F7 |
| C5-Binding | Preserves kind on `EffectObligation` | F6 |
| E4/E5 | failure keys per Opaque site | A6 |
| M39, U8p | none new | — |

## 5. Rules

ℓ is a location. `σ_in` is the state on entry to ℓ and `σ_out` the state after. Reading `X` with k borrow Derefs:
`val(X) = content^k(σ_in(root(X)))`.

```text
// ---- Places (F1, F2); steps per §3
within(A, B)       :- B's chain is a prefix of A's, an Index step meeting any index step; distinct Field, ConstIndex,
                      Downcast steps disjoint; raw-anchored chains compared only with chains through the same raw Deref.
through(X, A)      :- within(X, A), a borrow Deref of X lies below A.
deep(k)            :- k ∈ {move, sborrow, uborrow}.          // P3; base_op(ℓ, K) is a deep access of Place K
hits(A, X, deep)   :- within(A, X) ; within(X, A), (deep ; !through(X, A)).   // K ranges over Places (plan §4.3 erratum)
conflicts(k, m)    :- m = uniq ; k ∈ {write, move, uborrow, destroy}.
mode(L, H)         :  Shared → ref;  Uniq → uniq;  phantom Φ_c → uniq;
                      TwoPhaseUniq → ref if H is L's reservation holder (issue to activation), else uniq (an earlier
                      incarnation, activated). H is the holder whose set the check scans.
content(S)         =  ⋃{σ(T) | L∈S, named(L,T)} ∪ (σ(E) if Φ_E∈S)            // monotone in S
refs(S)            =  {T | L∈S, named(L,T)} ∪ ({E} if Φ_E∈S);  refs^k(S) = refs(content^(k-1)(S))
val(Aggregate|Cast{Erasure,Upcast,Pointer}) = ⋃ val(operands);  val(BinaryOp|UnaryOp|Discriminant|Len|Const) = ∅
val(Opaque s)      =  ⋃ σ(roots of s) ∪ {τ_s, Φ_E};  val(static or raw-derived Place) ∪= {Φ_E};  Adapt per MirCases

// ---- Carrying (forward)
σ(H, entry)        =  {Φ_E} for a Loan-capable Arg, else ∅;  σ(E, entry) = ∅
ended(L, ℓ)        :- base(L, B), destroyed_in_place(K, ℓ), K direct (no Index), K ⊑ B, no Deref between.
step(L, ℓ, ℓ′)     :- edge(ℓ, ℓ′), !ended(L, ℓ).   // every carrying rule below takes the step: σ_out(K) \= ended(ℓ) ∀K, E
issued(L,X,_,ℓ,H)  ⇒  σ_out(H) = {L} ∪ ⋃_{j<k} content^j(σ_in(root(X)))           // R2: parents of every followed layer
assign H := rv  (whole root)         ⇒ σ_out(H) = val(rv)                          // complete replacement
assign D := rv  (direct, D ⊐ H)      ⇒ σ_out(H) = σ_in(H) ∪ val(rv)                // P1
assign D := rv  (k ≥ 1 borrow Deref) ⇒ ∀T ∈ refs^k(σ_in(root(D))): σ_out(T) ∪= val(rv)   // S15:454, S4; ends nothing
move H (whole root)                   ⇒ σ_out(H) = ∅
StorageLive | StorageDead | EndBorrow H ⇒ σ_out(H) = ∅
// Temp shape: `cursor = cursor.children[0]@uniq` is `_t = Ref(cursor.*.children[i])` (issue, base cursor), then
// `cursor = move _t` (destroyed_in_place(cursor)): the step at the second point drops L from cursor's new value (19a);
// cursor keeps the parents, so 19b still errs.
join               :  per-slot union;  P1/P2 as MirDataflow;  prune(head): σ(H) = ∅ if ¬live_in ∧ no Loan names H

// ---- Calls (F3): c at ℓ, arguments a_i, destination D
ret(i, Value)      =  val(a_i) ∪ (content(val(a_i)) if a_i is a reference);   ret(i, Content) = content(val(a_i))
out                =  ⋃{ret(i,κ) | retains(c,i,κ,result)} ∪ ({Φ_E} if untied(c)) ∪ ({Φ_c} if abstract(c), descent)
                   ⇒  assign D := out;   ∀ retains(c,i,κ,store j), ∀T∈refs(val(a_j)): σ_out(T) ∪= ret(i,κ)
                   // a retained TwoPhaseUniq Loan is no longer in its reservation holder, so it counts as activated
unknown callee     :  every argument → result and → store j for every exclusive a_j;  untied.
writeback          :  the write through $w activates the Loan reserved in $w (nothing retains $w).

// ---- Liveness (backward)
use(H,ℓ)           :- ℓ reads, Moves or borrows a Place rooted at H (also through borrow Derefs, and an assignment through
                      a reference rooted at H); FakeRead; SwitchInt or callee operand; EndBorrow H; Return (_0);
                      a Drop of a Place rooted at H with observes(ℓ) (check-only Drops: no group, case Type).
kill(H,ℓ)          :- whole assignment or Call destination H; move H; StorageLive/StorageDead H; EndBorrow H (after use).
live_in(ℓ)         =  (live_out(ℓ) \ kill(ℓ)) ∪ use(ℓ);   live_out(ℓ) = ⋃ live_in(succ)   // P1 chain: runtime successors

// ---- Check sets: by holder, never by Loan id (finding 1; prototype round 4 wording)
closure(S)         =  least S′ ⊇ S with σ_in(T) ⊆ S′ for each L∈S′, named(L,T)     // liveness through references
CS(ℓ)              =  closure( ⋃{σ_in(H) | H ∈ live_out(ℓ), ℓ does not overwrite H} ∪ σ_in(E) )   // entry sets
CS_call(ℓ, t)      =  closure( ⋃{σ_in(H) | H ∈ live_in(ℓ), H ≠ t} ∪ σ_in(E) )      // t: the reservation holder
// The Loan issued at ℓ is in no entry set, and earlier incarnations stay in theirs, so no exclusion by id exists.
access(ℓ,X,k)      :- Copy|FakeRead|Discriminant|Len → read; Move → move; Ref Shared|TwoPhaseUniq|Raw → sborrow,
                      except a destroying Ref → read (shallow); Ref Uniq → uborrow; destination → write;
                      StorageDead|Drop → destroy (+ uborrow, deep, if observes(ℓ): a user drop may write through
                      the old value, S1's (H) for E0713, `s = S(r: s.r@follow@uniq)`).

err(conflict,L,ℓ)  :- access(ℓ,X,k), L ∈ CS(ℓ) via holder H, real(L), hits(X, place(L), deep(k)),
                      conflicts(k, mode(L,H)), !excused(L,ℓ), !partition(L,ℓ,k,X).
err(activate,R,L,ℓ):- reserved(R,t,_,ℓ), L ∈ CS_call(ℓ,t) via H, real(L), hits(place(R), place(L), deep),
                      conflicts(uborrow, mode(L,H)), !excused(L,ℓ).     // a destroying R: hits with deep = false
// Arguments of one call (S15:614 "both exclusive, or one shared and one exclusive"): every exclusive argument borrow is
// a reservation (G5), and its activation scans the other arguments' holders, which are live into ℓ. A reservation
// in another argument counts as ref, so two exclusive or a shared and an exclusive overlapping argument conflict
// (swap(p@uniq, p@uniq)). An exclusive argument that is not a reservation is a stored borrow, live when formed, so an
// overlapping later argument conflicts at its own formation.
err(effect,L,ℓ)    :- key(ℓ,f,k), L∈CS_call(ℓ,−) via H, root(place(L)) a static of Field f (P5), conflicts(k, mode(L,H)).
err(effect,L,ℓ)    :- unknown(ℓ), L∈CS_call(ℓ,−), root(place(L)) a mutable static.  // calls, erased Drops (S16:196)
err(ended,L,ℓ)     :- EndBorrow H at ℓ, issued(L,_,_,_,H), L ∈ CS(ℓ).               // guard-local Origin end
partition(L,ℓ,k,X) :- k = destroy, root(X) a body root, L ∈ σ_in(_0) and L in no other set of CS(ℓ).   // DIAGNOSTICS

// ---- F6 preserves results (S2)
disturbed at ℓ     :- ℓ writes, Moves or destroys a direct D ⊑ recv(c), or writes through a Place hitting recv(c)
                   ⇒  Φ_c is replaced by Φ_c^dist in every σ (by holder, like ended)
excused(L,ℓ)       :- Call c2 at ℓ, preserves(c2), L = Φ_c1 (not ^dist), callee(c1) = callee(c2), recv(c1) ≡ recv(c2).
// Φ_c: place = recv(c)'s referent, with all its content, mode uniq, issued into the result of an abstract requirement or
// Callable call whose result descends (§8.11.4 "through inputs"). Conformance (H, S2 text): the body starts with an
// escaped Φ_prev on (*self) with a fresh anchor; a self-Field-path call of a preserves requirement undisturbed since
// entry is excused against it.

// ---- F7: an Aggregate(Closure) flow; [x@ref] issues on the outer slot; calls use TyCtxt.Closure; a Consuming call
// Moves the Closure. F9: inline copies are ordinary code (inspection Loans fail activation); template calls use P9.
// F10: forward on P1 + P2 edges, liveness per chain rule, checks in every reached chain (S14:514).
```

**A6 mapping**:

| Problem | Code |
| --- | --- |
| Conflict with a TwoPhaseUniq Loan in its reservation holder | `CallReservationConflict_Kd` |
| Activation at a Call | `CallActivationConflict_Kd` |
| Activation at a writeback write (the conflict at the write, as the old path codes it, `OwnershipModel.cs:238`) | `PlacementActivationConflict_Kd` |
| Effect | `CallEffectConflict_Kd` |
| A `PreparedInput` root (R4-M10) | `DefaultArgumentAccess_Kd` / `DefaultArgumentBorrow_Kd` |
| `ended` on a `GuardCandidate` | owner choice (§9) |
| Otherwise | `ComparisonLoanConflict_Kd` |

A problem whose access is an Opaque site's, or whose witness holder carries `τ_s`, is derived with cause s.

**Corrections now in the rules**:

- check sets by holder; `step` on every carrying rule; the ban over Places; reservation mode per holder;
  `Φ_c^dist` per incarnation; observing Drops as deep exclusive accesses;
- `EndBorrow` is a use, then an end; Moves end carrying; check-only Drops are checked;
- `base_op` over Places; joins are unions.

**The partition lemma** (finding 10): every Loan that exactly reaches `_0` passed a fit into the result's fixed Origin,
so a body-rooted one is the Refuted relation (S15:575). Loans added only by over-approximation are harmless to exclude.

**S1 text additions**:

- A Borrow is checked against its own site's Loan while an earlier evaluation's Loan is live (finding 1).
- The kill holds on every carrying step, through Temps (finding 2).
- An observing destruction of an old value is an exclusive deep use of its Place (the recorded (H); A2 already does
  this through `observes`).

## 6. Evaluator

### 6.1 Algorithm and data

Per body view, in `MirAnalysis` step 4 (after A4's `Run`; `flow.Begin` and `moves.Build` redone per body):

1. **Pre-pass**, one `MirUses` walk:
   - holders, Loans and their CSR indexes (by place root, and by base root for `ended`);
   - `destroying` sites;
   - per-root conflict flags.
   A view with no real Loan skips the Loan stratum (census: 161 + 266 + 16 Loan-free views). If no access can conflict
   by root flags, the stratum skips too.
2. **Liveness solve**: `flow.Backward` over `⌈Hf/64⌉` words.
3. **Carrying solve**: `flow.Forward` over `Hf + 1` node slots (holders, `E`), with the node-union join and head
   pruning.
4. **Replay**, per reached chain:
   - a backward sweep from the tail's live-out records death locations and each holder's next use;
   - a forward sweep from the head's slots builds `σ_in`, evaluates `CS`/`CS_call` at candidate accesses (live-holder
     bit scan, node-id stamps for dedupe, closure), then applies the transfer.

**`MirLoanSets`**: sorted `int[]` contents in one pool with offsets; hash interning; memo dictionaries for `Add`,
`Union` (normalized, with subset and identity shortcuts), `Remove(n, K)` (`ended` sets) and `Map(n, L→L′)`
(disturbance). Node 0 is ∅. Storage resets per view by counts. A check scans holder by holder, so it knows each Loan's
holder for `mode(L, H)`; node-id stamps skip repeated nodes unless a TwoPhaseUniq Loan's reservation holder is involved.

### 6.2 Complexity and work counter

N locations, Hd heads, Hf flow holders, H all holders, L Loans, A★ candidate accesses, ρ head re-visits (ρ_f grows with
an in-loop copy chain of length n: about n passes, B's K1), κ `ended` points, δ disturbance points, ν node bytes created:

`W = Θ(N + L) + ρ_b·(N + Hd·⌈Hf/64⌉) + ρ_f·(N + Hd·Hf) + ν + (κ + δ)·H + Θ(N + Hd·Hf) + Σ_{ℓ∈A★} (⌈H/64⌉ + Σ_{distinct live and closure nodes} |node|)`

`MirBorrows.Work` counts each term: transfer steps, slot joins, node bytes, `ended`/disturbance sweeps and check scans.

### 6.3 V16 restatement (owner decision)

- **Linear** are the `N`, `L` and replay terms, and the alias shape, since copies share node ids. `Hd·⌈Hf/64⌉` and
  `Hd·Hf` are R4-M20's dense reading (DEC:550).
- **Not linear in input + output** are the access, kill and pass terms (each is linear per dimension). No simple linear
  form exists; R4-M24's counts by differences was one.
- **Proposed wording for plan §4.6.** "For A5, work ≤ c·(input + output + Dense), where output is the live Loans per
  point plus the problems, and Dense = heads × flow holders + Σ candidate accesses × sizes of the distinct live nodes
  they scan + Σ (ended and disturbance points) × holders + (passes − 1) × locations. `MirBorrows.Work` counts every term;
  the 2× and 4× scaling tests include an in-loop copy chain (K1) and k overlapping live nodes."
- **Scaling shapes**:
  1. a straight line;
  2. N references used at the end;
  3. N Copies of one reference;
  4. N loops with M live references;
  5. N writes through one reference;
  6. borrow/write pairs in a loop;
  7. the K1 chain;
  8. k overlapping live nodes;
  9. N chained locals and N signature Origins (the Origin stratum).
- **Memory.** The arena is `⌈Hf/64⌉ + Hf + 1` words per head, sized once in `MirDataflow.Prepare` against 64 MiB
  (R4-M15a). Over the limit, `OwnershipStorageLimit_Kd` is recorded once. Pools are reused across bodies.

### 6.4 Integration and errors

- **Ownership.** `MirDataflow` owns both solves, the chains, the P1/P2 rule and the arena. A5 supplies struct transfers
  (`IMirFlow`, `IMirBackFlow`) and never walks edges itself. `MirReach` stays the one traversal. Views come from
  `MirCases`; a body without a case-dependent access runs once for every view. A2 is read only through `Group`.
- **Errors.** Each error adds `MirProblem(code, subject = access syntax, views)` with fact rows (R4-M3a): the Loan
  anchor, the witness holder's next use (from the backward sweep), and for activation the other Loan's anchor. No check
  re-runs analysis to explain itself.

## 7. Origin stratum

**Algorithm.** Per body and per distinct case-mask class of its rows:

- **Nodes**: the atoms of `OriginObligations` rows and of containment edges, classified by `OriginAtom.Of`:
  - fixed;
  - finite(body), which includes mutable-static Borrows under P6;
  - inferred, which includes raw anchors and Borrows reached through a borrow (c1).
- **Refuted**: one multi-source BFS from finite nodes through inner nodes. Reaching a fixed node gives
  `UnsatisfiedOriginRelation_Kd` at the last edge's site, with predecessor witnesses.
- **Unknown**: fixed-source bitsets (64 per word) propagate from fixed nodes through inner nodes. An edge into fixed g
  carrying f with `¬entails(f, g)` gives `UnprovenOriginRelation_Kd`.
- **Bound**: Θ(N_o + E_o·⌈F/64⌉·ρ + Ft·Pm). No liveness, no CFG, no σ. Loan flow reads only `Retains`; the strata are
  independent, and A6 merges their problems.

**Containment** (row 9c): a Ref through borrow Derefs adds `contain(o_k, o_new)` per layer up to and including the first
shared layer (`MirCases.Layer` for pair layers). Edges between fixed atoms are entailed by Binding's meet
(`PathReborrowOrigin`).

**Boundary** (each failure reported once, by partition):

- **Binding keeps** OJ1–OJ7, T1–T4 and the fixed–fixed body relations it forms directly (B1's and B2's fixed ends, B3, B4,
  B6's fixed part, B7, B9, B10, B11, B14, B15).
- **A5 judges** every published row (a finite or inferred end) and every chain through an inner node (B5 occurrences, B8,
  B13, B16–B18): the old Analysis's role (`A/OwnershipAnalysis.Borrows.cs:13-121`, `A/OwnershipAnalysis.cs:228-240`).
- **DEC entry**:
  - plan §3's "chains inside bodies" means chains through an inner node;
  - Binding's fixed–fixed body judgments (`CallRelations`, `ResultPremises`, `RecordMismatch` parts) are not OJ
    judgments and move at R7, superseding PLAN10's R6 share of its deletion row;
  - the R4-M4 compared set splits `Binding.OriginRelation` by owner.
- **T1**: Binding leaves clauses it proved or failed as Constraints out of the rows (audit-origins §7.1).
- **Deleted at R6-check**: the old reporter, the gates `A/OwnershipAnalysis.Borrows.cs:74-79` and
  `B/Binding.Diagnostics.cs:985`, and the `CallBorrows` skip (`B/Binding.TypeOrigins.cs:182-192`).
- **Unowned items** (unit 2a0, before 2a):
  - unresolved inference obligations: a local region in a body (S15:348), else Binding's `MissingOriginBinding_Kd`
    (OJ6), covering `A/OwnershipAnalysis.Borrows.cs:45-47` and `B/Binding.OriginProof.cs:443-449`;
  - mutable-static anchors under P6: `OriginAtom.Of` makes a mutable-static Borrow finite(body) (B19, judged by nobody
    today);
  - splitting `ValidateOriginRelations`;
  - a census of the containment skip at `B/Binding.ArgumentOperations.cs:565`.

## 8. Components and files

| File | Contract (entry file starts with it) | Lines |
| --- | --- | ---: |
| `Mir/Analysis/MirBorrows.cs` | Owns A5's Loan stratum (SPEC 15.6–15.8) per body view.<br>Input: body, view, move paths, dataflow, A2 groups, A4 keys, Binding facts.<br>Output: Loan-family problems with facts through `MirReport`.<br>Invariants: root holders plus `E`; base-named Loans; syntactic access; checks by holder; regions keep no Loan live.<br>Must not read syntax or HIR, judge Origins, re-derive retention, intern projections, or copy the body per view. | 290 |
| `Mir/Analysis/MirBorrows.Flow.cs` | Forward carrying transfer and join | 290 |
| `Mir/Analysis/MirBorrows.Liveness.cs` | Backward holder liveness and per-chain transitions | 130 |
| `Mir/Analysis/MirBorrows.Checks.cs` | Step classes, `hits`, check sets and closure, conflict, activation, effect, `ended`, partition, S2 | 300 |
| `Mir/Analysis/MirLoanSets.cs` | Owns interned immutable Loan sets: Add, Union, Remove, Map; the only Loan-set form in Mir | 140 |
| `Mir/Analysis/MirOrigins.cs` | Owns A5's Origin stratum: judges rows and containment per case-mask class; reads no liveness or Loans | 260 |
| `MirDataflow.cs`, `MirInit.cs` | G1 | +94 |
| `MirTypes.cs` | G2 | +40 |
| `Mir/Build/*` | G5, G6, writeback, Slice, Guard, C8 | +110 |
| `MirEffects*.cs` | `Keys` (P5 bitsets) | +110 |
| `MirReport.cs`, `MirAnalysis.cs` | A6 mapping, derived taint, facts; step 4 | +100 |
| Binding (`TyCtxt.Retention.cs`, `TyCtxt.Origins.cs`, `Binding.Closures.cs`, `Binding.EffectObligations.cs`, Opaque keys, boundary fixes) | §4.3, §7 | +450 |
| **Total** | | **≈2,310** |

- **Deleted at R6-check**: `A/Ownership*` (46 files, 15,446 lines), `A/BorrowLiveness.cs` (141),
  `A/PackedAnalysisTable.cs` (59), the two `Unsupported_Kd` gates, and the Transitional couplings
  (`Binding.IsEffectRecord`; 183 `.Ownership.Bodies` test reads migrated per family).
- **Net**: about −13,300 lines, or about −11,200 counting the landed A1–A4 MIR analyses (2,127 lines).
- **Each file stays under 5,000 tokens.**

## 9. Owner decisions (one table)

| # | Decision | Content | Recommendation |
| --- | --- | --- | --- |
| 1 | OD13 losses | Needed: P1, P3 (both forms), P4, P10, P11. Optional, each its own decision: P5, P6, P9. Assumed via S1′: 9k, 9l, 14, raw-site form | Approve |
| 2 | Relaxations | (a) row 8: referents of distinct bases are disjoint (S15:494 read as "distinct anchors, one exclusive"); (b) distinct Downcasts of one Place are disjoint (S3:253 says Case payloads share bytes; one variant is active); (c) the `replace` exemption sourced from the published row also covers `Array.clear` and `Dictionary.clear` (row `Target`; sound: they destroy in place) | Confirm all three as SPEC clarifications |
| 3 | S1 wording | own-site check (finding 1); kill on every step through Temps; root-most base for multi-layer reborrows (13a); an observing drop as an exclusive use; exemption as "a target the operation destroys in place" | Add to the S1 proposal |
| 4 | c1 / c2 | c1: a Borrow through a borrow is inferred (Unknown), the current reading (S15:567). c2: dissolved by P6; without P6 the Loan stratum checks static Loans into `E`, statics or `_0` (about +40 lines) and A4 publishes anchors | Clarification only |
| 5 | V16 | The restated bound of §6.3 | Adopt the wording |
| 6 | Origin boundary | §7 (A5 = the old Analysis's role at R6-check; Binding's fixed–fixed body judgments move at R7; ledger split; T1) | Adopt |
| 7 | MIR contracts | TwoPhaseUniq meaning (writeback activation), `RawSlice` backing operand, `MirCallee.Arg`, `EndBorrow` meaning. No closed-vocabulary member (ARCH's six unchanged; no `MirLocalRole` member) | Approve |
| 8 | DEC records | A5 representation (per-root holders, Loan-set nodes, base-named Loans, syntactic access, `E`, `Φ_E`, holder-based exclusion); `MirDataflow` join hook and backward pass (superseding R4-M20's word-OR detail, replacing R4-M24's withdrawn clause); A4 keys; the boundary; G3 departure, G4 and G7 closed; Relate and Clauses consumed only inside `Retains`; `PublishedBindingTypes` additions (`Retention`, `OriginObligation`, atom kinds) | Record in implementing commits |
| 9 | DIAGNOSTICS | The destroy partition rule (one report: the Refuted relation); the guard `ended` code (`UnsatisfiedOriginRelation_Kd`, as the SPEC calls it the Origin's end, or a Loan-conflict code); E5: the representative cause = minimum site key | Record |

## 10. Implementation units (plan §6.2)

| Unit | Content | Completion condition | Checks |
| --- | --- | --- | --- |
| 0a | Ledger re-baseline at the step-0 base (census §3 command) | `ledger.tsv`, `summary.md` recorded | V13 failure-set equality |
| 0b | Prototype into `draft/Sketches`: the 66 cases (60 plus round 4's 20a–20f), plus `y = h@follow + 1` with `h = y@ref`, plain arguments of one call (`f(x@ref, x@uniq)`, `f(t, x@ref)` with stored `t = x@uniq`, `swap(a, b)` with parameters), `x = f(x@ref)` with retention, and A2's rules (holder exclusion, `Φ_E`, the exemption, `Φ_c^dist`) | All match; recorded with step-0 records | `proto.cs` run |
| 0c | G1 `MirDataflow` | A1 behavior-preserving; a backward test problem | `MirDataflow`/A1 tests, `BodiesScaleLinearly`, ledger unchanged |
| 0d | G2 | Classified over library Types | Unit tests (S15:598-604 `Logger`, abstract and Sealed parts) |
| 0e | Builder contracts G5, G6, writeback, Slice, Guard, C8 | Dump goldens updated; validator clean | `MirBuildTest`, goldens |
| 0f | A4 `Keys` | Keys per Call/Drop, linear | `MirEffects` tests, scaling |
| 1a | Binding C1+C8 `Retains(callee, Subst)` | Immutable after Bind; allow list | View tests, `KimiLibraryShapeTest` |
| 1b | `MirLoanSets`, `MirBorrows` F1, F2, F3, F10 (joint core) | Library and milestones produce no A5 problem (R4-M12 from here) | SPEC-example table (`tests/diagnostics/spec-cases.json`: about 40 error and 34 accepting variants), 0b's cases as MIR-layer tests, scaling shapes 1–8 |
| 1c | A6 mapping and derived taint for 1b's codes | Codes per §5 | Corpus snapshots, ledger |
| 2a0 | Binding boundary: OriginObligations, atoms, universal relations, T1 rows, unowned items, `ValidateOriginRelations` split, ledger split | Rows immutable; no double owner | View tests, ledger |
| 2a | F4 `MirOrigins` | audit-origins §3 tests; old reporter's rows matched | Shape 9, ledger |
| 2b | F5 (keys, unknown, P6) | S15:540-558 per P6; S16:199-202 | Rule tests |
| 2c | C2 view, then F7 | S15:715-723, S7:341-344 | Rule tests |
| 2d | F8 updates, match, guards, writeback, collections | S14:332-351, S15:650-689, S13:600-602, S4:570-575 | Rule tests |
| 2e | F9 defaults | S7:84-88; audit-calls row 26 | Rule tests |
| 2f | C5 view, then F6 (S2) | `takeTwo`, `takeAcross`, Drain, Merge; 12 library conformances accepted | Rule tests, library corpus |
| 2g | E4/E5 keys, A6 completion | Derived rules per DIAGNOSTICS | Corpus |
| Exit | R6-check deletion list (§7, §8), test migration | Every family complete, no `Unsupported_Kd` family | Session verify, final review |

## 11. Risks and open questions

- **P3 form (b)** may be a SPEC soundness gap, not a loss: z gives shared access to x while x is written through r. The
  old path rejects it. Ask the owner.
- **Relaxations** (row 8, Downcasts, `clear`) and **V16** need the decisions in §9. If V16 is refused, node reference
  counting adds code.
- **Holder-based exclusion** is confirmed by round 4 (20a–20f rejected, the 60 earlier cases unchanged).
- **Unconfirmed until 0b**: the plain-arguments argument, `Φ_c^dist`, and `destroying` Refs at formation and
  activation.
- **`x = f(x@ref)` with retention.** The validated wording skips the overwritten x, so the write is not checked against
  the Loan x's new value carries. The SPEC's point model, and Rust, reject it. Later Moves and destructions of x are
  still checked, so I see no memory-safety failure. If the owner reads it as an error, add "the destination's new value,
  without P's own issue" to `CS(ℓ)`, after 0b.
- **Base choice**: the root-most base (13a) for multi-layer reborrows; the plan leaves it to S1 as (H).
- **F6 conformance** waits for S2's final text. **Binding views** snapshot after the late mutations. **R5**: builder
  changes are serialized with it (plan §9.1). **A4's depth limit** is plan S5.
