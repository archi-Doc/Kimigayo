# A5 design B: syntactic Loans, deep borrows, value-carried holder sets

Date: 2026-10-16. Base: `bin/worktrees/a5-step0` (`b2edea8b`). Read-only design; nothing built or run.
Citations: `S15:n` is line n of `docs/spec/15-ownership-and-lifetime-analysis.md` (S3, S13, S14 likewise); code paths are
under `src/Kimi/Compiler/`. "Prototype" is `prototype.md` with `A5RulesPrototype.cs`. (H) marks a hypothesis.

## 1. Summary

- **Loans are named by their syntactic MIR Place**, so S1′ base naming falls out (`r@follow@uniq` borrows `(*r)`).
- **Borrows and Moves are deep; reads, writes and storage ends are shallow** (Rust's rules; L2). This removes access
  resolution and authority, implies S1′'s bans and closes a probable SPEC gap (§11 Q4).
- **Holders are Loan-capable move paths** carrying interned Loan sets. Reborrows materialize parents (R2); the S1′ kill
  stops every rule (R1). References are followed only where a value moves through them ("flat provenance").
- **Liveness is plain backward bits over holders**; references are closed at check time.
- **Calls** use contract retention rows; inner retention issues a *descendant Loan*, so "results kept across calls" and
  S2 are one rule. **Origins**: a reachability judge over Binding-published rows plus MIR containment rows.
- **Size**: ≈ 2,900 new lines (A5 1,630, MIR contracts 670, Binding 570); R6-check deletes ≈ 15,600 old lines for
  A1–A6 (A5's share ≈ 9,000, (H)).
- **Speed**: per view Θ(statements + heads × flow holders) set-id operations plus dense word terms; no Datalog engine.

## 2. Precision-loss list (SPEC simplifications)

"Needed" means the design depends on it for size or speed; "optional" means a small cost would remove it.

| # | SPEC | Simplified rule | Rejected forms | lib / ms / tests | Soundness | Class |
| --- | --- | --- | --- | --- | --- | --- |
| L1 | §15.6, §15.6.2, §15.6.3 (S1′, assumed) | A Loan is identified by its Borrow site and case; a child formed through a reference in H is killed by `=` or the storage end of H or a holder on H's path. While it is live, Move, exclusive borrow (including a call reservation's activation and an `exchange`/`swap`/`replace` target) of H or an ancestor conflicts | `let q = r@move`, `let w = r@uniq`, `exchange(r@uniq, …)` while `c = r@follow@uniq` is live; two live borrows from one raw-Place site in a loop (audit-core row 24) | 0 / 0 / ≈10 rough grep hits (`X@follow@uniq` then `X@uniq|@move` within 400 chars), mostly owned `obj` handles that conflict under any reading (`PipelineObjectTest`, `Milestone14.kimi:40,53,55`) | Prototype: S1′ passes all 41 programs; S1 fails 5 | Needed (finite naming) |
| L2 | §15.6.2 "Inline parts exclude referents" (S15:500) | **Loans cover referents.** A Loan on a Place covers the Places reached through references stored in it. A Move or a borrow of a Place reaches every Loan on a Place reached through references stored in it; a read, a write and a destruction do not, and a complete replacement or storage end ends the Loans formed through it (L1) | (a) `let z = r@ref`, or a shared receiver borrow of a holder of `r`, while a conflicting Loan formed through `r` is live; (b) an access through `r` while a conflicting Loan on `r` or a holder of `r` is live: `let z = r@ref; r@follow = 5; inspect(z)` | 0 / 0 (heuristic: no shared borrow of an exclusive-reference variable in lib or ms) / 3 rough hits; the old path already rejects form (b): `OwnedStoredReferenceTest.cs:230-235` ("ASharedSlotBorrowStillFreezesItsReference") | Rust's deep/shallow rules: every access through a reference is covered by that reference's own Loans, which formation made compatible with all live Loans (§5) | Needed (removes access resolution and authority; also a soundness fix, §11 Q4) |
| L3 | §15.6.4 step 4, §15.6.3 descent | A result that names an Origin of a borrowed input's referent Type depends on every Loan stored in that referent, plus one descendant Loan of the referent | A result naming one of several independently borrowed slots of the receiver, then a conflicting access to the other slot's Place while the result is live | 0 (no multi-slot header) / 1 header (`Milestone16.kimi` `Pair {left, right}`; `choose` names the meet of both, so no loss) / 65 multi-slot headers in 19 files (upper bound; `IteratorOriginEffectsTest` is the likely exposure) | Superset of the dependencies | Needed (no Origin positions) |
| L4 | §15.6.2 provenance row | Following a reborrow of a part, or two reference layers, reaches the whole target holder and the intermediate holders | A value read or written through `r@follow.f@uniq` takes or adds the Loans of all of `x`, not only `x.f`; `r@follow@follow@uniq` also adds to the slot holder | 0 / 0 (H) / unknown until the shadow run | Over-approximation of reach | Optional: a capability bit per set element and a recursive resolve, about 40 lines |
| L5 | §15.6.4 "Mutable-static anchors", §15.6.5 static finite Origin, §11.8 | A Borrow of mutable static storage has a body-local finite Origin; it never leaves the body | `func current(anchor: ref/i32) -> ref/string during anchor => State.text@ref` (S15:545-546) | 0 / 0 / 0 (regex `[A-Z]\w*\.[a-z]\w*@(ref\|uniq)` over tests) | Stricter | Optional, recommended: deletes A4 per-result anchors, the `fn_use` restrictions, the value-escape rule and question c2 |

Not adopted, because they save little inside A5 or reject milestone or SPEC programs: removing pair binders (A5 only
loops over views), closed defaults (ms 2, DEC O3), non-generic static keys (ms 1), unreachable items as errors
(MirDataflow already has P2), receiver-independent Closure results (a SPEC example, S15:716).

No loss: §14.10.3 joins by union (audit-core); S4 (writes through references end nothing); S3. One **relaxation** of
the literal text needs confirmation (§11, row 8): Places through distinct roots' references are disjoint.

## 3. Holders and references

**Choice.**

- A *holder* is a move path (DEC R4-M21) whose Type may carry Loans (`MirTypes.MayCarryLoans`: a borrow layer, an
  Origin slot, a Slice, a Function or Closure Type, at any depth). Other paths are not holders.
- A *Loan place* is the borrowed MIR Place, steps included. A Deref step is classified by its base Type:
  - **borrow**: `ref`, `uniq`, `objref`, `objuniq`, or a PairLayer the view makes a reference;
  - **own**: an `obj`, `rc` or `arc` payload, part of the handle's storage;
  - **raw**: a fresh anchor, compared only with Places derived from it (S15:496).
- Overlap is syntactic: prefix, with distinct Fields, Tuple elements and constant indices disjoint, and Index
  overlapping every index of its base. Distinct roots are disjoint, references included (L2 makes this sound).
- A holder carries the Loans of its own value, never its referent's. Following a reference uses the *direct-place*
  Loans the holder carries: their anchors (the holder path of the place up to its first Index or Deref) are the
  referent holders. Materialized parents make this one step per deref (L4).

**Why.**

- Code: no resolution at accesses (the prototype's `Resolve` names and `auth`), no Type-position maps (R4-M24's
  `Relate` per flow), no depth arithmetic. Flat provenance is one loop.
- Speed: overlap tests compare interned projection chains; following a reference costs one set scan.
- Soundness: syntactic naming plus deep borrows is Rust's invariant. It removes the may-authority hazard the audit
  raised for referent naming (audit-core §Core rules) and the prototype's untested risk 1.

**Rejected.**

| Alternative | Reason |
| --- | --- |
| Provenance with access resolution and authority (prototype R3) | More code at every access, authority from may-sets, and the call gap (§11 Q4) stays open |
| Origin positions (per-position Loan sets) | Needs Type-position maps and invariant equalities on every flow (the discarded R4-M24 machinery); gains only L3 |
| Per-root holders | Loses partial replacement (S15:454; prototype cases 5b and 7); MirMoveData already exists |
| Dense holder × Loan bits per head | Θ(heads × H × L/64), refused by plan §4.6 |
| Deep carrying with depth tags and eager write-through updates | Depth shifts in every rule and a holder scan per write; the check-time closure needs neither |
| NLL-style flow-insensitive carriers | Rejects SPEC-valid reassignments (`t = r; t = y@ref; inspect(r); x = 3`) |
| Polonius-alpha per-Loan graph search | Read and write edges depend on carried Loans, so the graph is not static; L × graph work |

## 4. Fact schema

Notation: P a point, H/K/T holders, L a Loan, X a Place. "In" is an input fact, "Der" is derived.

### 4.1 MIR relations

| Relation | Keys | Producer | Kind |
| --- | --- | --- | --- |
| `edge(P,Q)` | runtime and CheckTarget edges | `MirDataflow` P1/P2 (`Mir/Analysis/MirDataflow.cs:7-8`) | In |
| `holder(H)` | move path | `MirMoveData` + `MirTypes.MayCarryLoans` | In |
| `issued(L,X,mode,P,D)` | Ref site, Place, Shared/Uniq/TwoPhaseUniq, destination | `MirUses` under the view (Adapt resolved, R4-M19) | In |
| `entry(L,p)` | reference-typed `Arg` local p, escaped referent ⊤ | MIR locals | In |
| `flow(X,D,P)` | Use, Move, Aggregate operand i → D·i, Cast Erasure/Upcast/Pointer | MIR statements | In |
| `replaced(H,P)`, `storage(H,P)` | whole write of a direct path; StorageLive/StorageDead | `MirDescriptors` rows (G4) | In |
| `moved(H,P)` | Move operand of a direct path | `MirUses` | In |
| `access(P,X,k)` | k ∈ {read, write, move, ref, uniq, activate, destroy} | `MirUses` + statement kind | In |
| `endborrow(t,P)` | use, then end of t | `MirStatementKind.EndBorrow` | In |
| `use(H,P)` | read, Move, borrow, EndBorrow, observed Drop, Return of `_0` | `MirUses`, `MirTypes.Observes` × `MirDropTable` rows | In |
| `live(H,P)` | backward may-liveness | `MirDataflow.Backward` (G1) | Der |
| `carries(H,L,P)` | interned set per holder | A5 forward | Der |
| `reserved(L,P)` | L ∈ R(P) | A5 forward from TwoPhaseUniq | Der |
| `call(P,c)`, `arg(c,i,a)`, `dest(c,D)`, `recv(c,X)` | Call terminator | MIR (`Mir/MirBody.cs:155-162`) | In |

**MIR fact contracts** (plan §6.2 step 0; each closes a verified gap):

| Gap | Contract |
| --- | --- |
| G1 | `MirDataflow.Backward`: bit states per head; a chain P1 reached joins its runtime successors only, a P2 chain every successor (R4-M24's reading) |
| G2 | `MirTypes.Observes(type)`: a visitor of `Destruction`; true for a user `drop` or a part that observes an Origin (S15:596) |
| G3 | `MirDropRow.Path` restored (a part row's move path); `Group` returns −2 for a Drop only the checks reach and −1 for a body not analysed (`Mir/Analysis/MirDrops.cs:40, 86-88`) |
| G4 | Rows, not a new flag: StorageDead = `Kills | DropUse` (a destroy access); EndBorrow = `Reads | Kills` (a use, then an end); StorageLive = `Kills` (`Mir/MirDescriptors.cs:77-79`). A1 already switches on the kind (`MirInit.cs:112-121`) |
| G5 | Every exclusive borrow that produces a receiver or argument is `TwoPhaseUniq`: written `@uniq` arguments (`MirBuilder.Values.cs:84`), Reborrows at `uniq` parameters (`:175`), synthesized receivers and the collection helper (`:232`) |
| G6 | At a transfer, EndBorrow of a pending *reservation* goes on the runtime edge only, of a *comparison* temp before the edge pair (`MirBuilder.Control.cs:135-142`); after a diverging operand the builder still builds the invocation in the check-only continuation, so it activates there (`MirBuilder.Calls.cs:116-120`) |
| G7 | `MirMoveData` adds a child path per Loan-capable operand of an Aggregate destination and closes child paths under whole Use flows between holders (`MirMoveData.cs:157-186` creates paths only for named Places) |
| Slice | The safe Slice expansion forms the element pointer through a backing borrow of the whole sequence, Shared for `Slice` and TwoPhaseUniq for `UniqSlice`, passed as `RawSlice`'s operand; the intrinsic row's `Retains` column makes the result carry it (`MirBuilder.Expansions.cs:288-315` forms a raw Ref today, so S4:570-575 is accepted: unsound) |
| Guard | At guard cleanup, on both outcomes, EndBorrow of each candidate temp (S14:327); A5's end check (§5 F8) is the guard-local Origin's end |
| Writeback | Compound assignment, increment and decrement form `$w = TwoPhaseUniq(dest)` at locate and write through `$w` (S13:595); `MirBuilder.Expansions.cs:127-240` has no protection today |
| TwoPhase meaning | A TwoPhaseUniq Loan activates at the first use of its reference: the Call it is an argument of, or the write through it (`MirVocabulary.cs:113` says "at the Call") |
| Intrinsics | `MirDescriptor` gains `Retains`, the operands whose Loans an intrinsic's result carries (Clone, ReinterpretLoan, ErasedWriter, RawSlice; others none) |
| C8 bits | `MirCallee` sets the Virtual slot `Arg` (`MirBuilder.Calls.cs:131`) and carries the selected value-call signature |

### 4.2 A4 publication (read by A5, never reading A5)

- `MirStatic` gains a key: the symbol plus the Origin-erased Subst of its enclosing Type (`Mir/MirBody.cs:441-450`
  keys by symbol only), so `Cache<i32>` and `Cache<string>` differ (S22:377-381).
- `MirEffects.Summary(callee, subst)`: static keys with access kind (read, write, borrow, destroy, init), an open-key
  flag (S15:531), `Unknown`. Composed through the call's Subst; one table per program version.
- `MirEffects.DropEffects(body, block, view)`: keys or Unknown for a Drop, from the existing per-Drop classification
  (`MirEffects.Solve.cs:277-291`).
- Per-result mutable-static anchors only if L5 is rejected.

### 4.3 Binding views (smallest shapes)

| View | Shape | Replaces |
| --- | --- | --- |
| C1 `TyCtxt.Retains(callee, subst)` | rows `(input slot, own\|inner, output: result \| store(slot))`, from clauses, well-formedness and Type-parameter positions under the Subst; premises folded in | the old analysis's retention (`Analysis/OwnershipAnalysis.Calls.cs:11-89`) |
| C8 | none beyond the two `MirCallee` columns above | `CallPlan` copies |
| C2 `TyCtxt.Closure(k)` | captures `(source, mode)`; per view the call receiver mode; result-source rows `(position, receiver \| capture j \| param i)` | transient `CallLocalOrigin`; removes the `Unsupported` at `Binding.ValueCalls.cs:531-534` |
| C5 (S2) | `Preserves` bit beside `Confined` in `EffectObligation` rows; per call from the existing `DispatchBounds`/`CallableBounds` | — |
| M39 | none: views are runs, and template-call retention is C1 filtered by §7.2.3 (§5 F9) | OD8 and OD9 not needed |
| E4/E5 | a failure-key range on `MirOpaque`; bound children of an Error published | Syntax children (`Binding.Hir.cs:20-31`) |
| U8p | none: MIR forms the backing borrow from the expansion's sequence Place and the Slice Type's Semantics | — |
| F4 `TyCtxt.OriginRows(definition)` | immutable rows `(longer, shorter, eq, site, caseMask, source)` for every relation Binding forms (fits, substituted and local clauses, well-formedness, Closure results), fixed–fixed ones included; `OriginAtom.Of`; `UniversalRelations.For` | Binding's judging reporters (§7) |

## 5. Rules

One view at a time (DEC R4-M11). State at each point: `set(H)` per holder, `E` (escaped Loans), `R` (reserved Loans).

```text
// ---- Places (F1, F2) ----
prefix(X, Y)      :- Y = X·s.
overlap(X, Y)     :- prefix(X, Y) ; prefix(Y, X).          // modulo disjoint Field/Const/Downcast steps; Index meets all
through(Y, X)     :- prefix(X, Y), the steps after X contain a borrow Deref.
desc(L, R)        :- L is a call-result descendant Loan on R; place(L) = R·(*), "through any reference below R".
deep(k)           :- k ∈ {move, ref, uniq, activate}.
hits(k, X, L)     :- overlap(X, place(L)), (deep(k) ; !through(place(L), X)).        // L2
formed_in(L, H)   :- through(place(L), H) ; desc(L, R), prefix(H, R).                // L1 base on H's path
anchor(L)         :- the holder path of place(L) cut at its first Index or Deref; ⊤ for entry(L, _).
follow(H, P)      :- { anchor(L) : L ∈ set(K), overlap(K, H), place(L) has no borrow Deref } ∪ {⊤ if an entry Loan}.
read(X, P)        :- ∪ set(K) for holders K overlapping X                 if X has no borrow Deref
                     ∪ read(T·s, P) for T ∈ follow(h, P)                  if X = h·*·s (first borrow Deref)
caps(X, P)        :- ∪ set(h) for every holder h whose reference X's path follows (all layers).   // R2

// ---- F1: issue, flow, kill ----
loan_killed(L, P) :- (replaced(H, P) ; storage(H, P)), formed_in(L, H).             // R1: no rule propagates it at P
issue at P (Ref X into D, L):     set(D) := ({L} ∪ caps(X, P)) \ killed(P).
flow(X, D, P):                     set(D·s) := read(X·s, P) \ killed(P), part-wise where D has child paths.
replaced(D, P):                    clear set(D subtree) before the new value lands; a flow into D is not killed by it.
moved(H, P) ; storage(H, P):       clear set(H subtree).                            // Moves end carrying
write through D = h·*·s at P:      set(T·s) ∪= V for T ∈ follow(h, P); E ∪= V if ⊤ ∈ follow(h, P).   // ends nothing (S4)
non-path Index or dynamic container: the write adds V to the prefix holder and ends nothing (S15:454).
carries persists:                  set(H) at Q ⊇ set(H) at P for edge(P,Q), unless cleared or killed at P.
joins:                             per holder union; E and R union (S15:455, S14:514 under the guarantee reading).
pruning at a head h:               clear set(K) for K ∉ reach(live_in(h))   (sound: see closure below)

// ---- liveness and live Loans ----
live(H, P)        :- use(H, Q) reachable from P without replaced/moved/storage of H (P1 chains: runtime successors).
reach(S, P)       :- least S' ⊇ S closed under K ∈ S', T ∈ follow(K, P) ⇒ T ∈ S'.   // replaces prototype R4 feedback
live_loans(P)     :- ∪ set(K) for K ∈ reach(live(P), P) ∪ E.
live_destroy(P)   :- the same without local 0 and without E.   // returned/escaped borrows of locals are Origin errors (S15:577, :710)

// ---- checks ----
mode(L, P)        :- ref if L ∈ R(P), else mode(L).
conflicts(k, ref) :- k ∈ {write, move, uniq, activate, destroy}.
conflicts(k, uniq):- every k.
error(L, P, k)    :- access(P, X, k), L ∈ live_loans(P) (live_destroy(P) for destroy), !entry(L), L not excluded,
                     hits(k, X, L), conflicts(k, mode(L, P)).
// issue of Ref X: access(ref, X) for Shared and TwoPhaseUniq, access(uniq, X) for Uniq; Raw: access(ref, X), no Loan (S5:101).

// ---- F1 rows 14, 23, 27, 29 ----
use(H, P)         :- Drop of X at P, H ⊆ X, Observes(case Type of H's part), by the A2 row (Elide: none; Open: own layer, then child rows).
access(P, X, destroy) :- Drop of X (runtime or check-only: check-only Drops are checked, audit-core row 22); StorageDead X.
endborrow(t, P)   ⇒ use(t, P); end check of own(t) = Loans issued into t: error(L, P, ended) :- L ∈ own(t), L ∈ live_loans(P⁺) via a holder ≠ t;
                    then clear set(t), R −= own(t).                                  // comparison Loans, abandonment, guards

// ---- F2: reborrows and splitting ----
// child Loans are ordinary Loans with a borrow Deref in their place; region splitting is overlap on distinct parts;
// a raw-Place child is a fresh anchor. Pair layers resolve per view (MirCases.Layer).

// ---- F3: calls (rows 2, 9, 11, 15, 30) ----
activation at Call c: for each argument temp a with L ∈ own(a) ∩ R: access(activate, place(L)) excluding L; R −= L.
retains(c, i, own,   result):  set(D) ∪= read(a_i) ∪ read(a_i·*).                     // step 4: referent Loans too
retains(c, i, inner, result):  set(D) ∪= {Ld(c,i)} ∪ (read(a_i) \ own(a_i)) ∪ read(a_i·*).
                               // Ld(c,i): desc Loan on R_i = the argument's referent Place, in a_i's mode (L3)
retains(c, i, _, store(j)):    write through a_j·* of read(a_i).                      // adds, ends nothing
argument temps moved into c:   cleared after c.
issue TwoPhaseUniq L into t:   R ∪= {L}; formation is access(ref, X).

// ---- F5: effects and statics (rows 1, 12, 29) ----
access(P, k_static, kind) :- call(P, c), key k ∈ Summary(c) with kind; Drop at P with DropEffects key k.
key_overlap(k1, k2)       :- same Field, (open(k1) ; open(k2) ; k1 = k2).               // S15:499
error(L, P, effect)       :- call or erased Drop at P with Unknown, L ∈ live_loans(P), place(L) in mutable static storage.

// ---- F6: preserves results (S2) ----
undisturbed(s, P)  :- must-lane (stored as its complement, union join): starts at the call site s with Preserves on
                      receiver place p_s; ends at replaced/moved/storage/destroy of an overlap of p_s or a prefix, or a
                      write through a reference whose follow set overlaps p_s.
excluded(Ld(s,0), c2) :- callee(c2) = callee(s), recv(c2) = p_s, undisturbed(s, P_c2).
conformance:          check a Preserves implementation with phantom descendant Loans of the receiver referent in E
                      (region-split, fresh-anchor children as S2 states, audit-calls §10); calls contribute declared
                      effects only.

// ---- F7: Closures and escape (rows 17, 18) ----
formation:  Aggregate(Closure) flows each capture into its environment Field; an `@ref`/`@uniq` entry is a Ref on the outer slot.
value call: retains rows from C2: receiver → own of the callee operand; capture j → read(k·j); param i → C1 of the internal signature.
escape:     writes into ⊤ (parameter or capture referents, statics) go to E; E is live at every later point.

// ---- F8: updates, match subjects, compound assignment, collections (rows 5, 16, 21, 25, 30) ----
replace/exchange/swap: ordinary calls (reservation, activation, C1 rows; the write into the target is store(value → target)).
match subject:  the Subject temp's Loan; bindings derive; the FakeRead keeps it live through guard cleanup (S14:342).
guard end:      endborrow of each candidate temp at guard cleanup (the end check reports a value that keeps g).
compound assignment: TwoPhaseUniq `$w` at locate; the write through `$w` is its activation (PlacementActivationConflict).
collections:    published rows give retains; removed values carry full dependencies; nothing subtracts.

// ---- F9: defaults (row 26) ----
inline default: reads of reserved inputs are ref accesses; a kept inspection Loan conflicts at activation (A6 maps a
                PreparedInput root to DefaultArgument*_Kd).
template call:  retains(c_t, i, own, result) :- i is Copy and the default's Type names i's Origin (§7.2.3 forbids a new
                Borrow or Reborrow, so the template keeps only existing Loans). The template body is checked once.

// ---- F10: unreachable code (row 22) ----
// carries over P1 and P2 edges (MirDataflow); liveness by G1's successor rule; check-only Drops checked;
// reservations survive the assumed continuation and activate at the assumed invocation (G6).

// ---- A6 ----
problem(code, subject, views, fact) :- error(L, P, k); fact = (Loan site, access kind, carrier holder, witness use).
code: CallReservationConflict if L ∈ R(P); CallActivationConflict for a call activation; PlacementActivationConflict
      for a writeback activation; CallEffectConflict for an effect; ComparisonLoanConflict otherwise; MirReport's
      derived rules (Opaque roots, Inlined scopes, PreparedInput, R4-M10).
```

**Consistency with the audits and the prototype.** EndBorrow is a use, then an end. Moves end carrying. Check-only
Drops are checked. Loans are named by base (S1′). Prototype refinements: R1 (kill on every rule), R2
(materialization), R5 (flows read through references) and R6 (reservations) are kept; R3 is replaced by L2; R4 is
replaced by the check-time `reach` closure. A borrow of `slot` is itself a use of `slot`, so 4b holds without
feedback. I traced the prototype's 41 programs through these rules: each keeps its S1′ verdict, and 9i is reported at
`let z = r@ref` instead of at the read.

**Soundness (sketch).**

- Invariant: live Loans are pairwise compatible under `hits`/`conflicts`.
- Every reference value can reach only storage protected by a Loan it carries, an entry Loan, or a raw anchor.
- Formation checks keep the invariant. Deep borrows and Moves stop a second path to storage that a live Loan formed
  through a reference protects. Shallow writes through a reference are covered by that reference's own Loans.
- The S1′ kill is sound by its invariant: after `=` or the storage end of the base, only descendants hold the old
  reference, and they carry the parents (R2).

## 6. Evaluator

**Pre-pass (per body).**

- Holders: Loan-capable paths, with flow holders numbered first, as A1 does.
- Loan table: every Ref, PairAcquire and Adapt site (a view may not issue it), entry Loans, and descendant Loans
  (call × inner-retained input). Each row holds the site, Place, mode, its `formed_in` holder paths (for the kill
  index) and its anchor.
- Kill index: Loans by the holder paths they are formed in.
- View plan:
  - run once when the body has one view, or when no access is case-dependent (`MirMoveData.ViewDependent`) and no
    retention row is case-conditioned;
  - otherwise run per view;
  - skip a view in which no Loan site issues (A4's views still run).

**Data structures.**

- `MirLoanSets`: interned Loan sets as dense bitsets of ⌈L/64⌉ words in one pooled arena. They are hashed by content,
  and union and minus are memoized per id pair. Id 0 is empty, and equality is id equality.
- Forward state per head: one int32 set id per flow holder (two per word), plus `E` and `R`. It lives in
  `MirDataflow`'s arena. Chain-local holders live in the scratch.
- Liveness: bits per head over holders, plus one witness location per (head, live holder), filled during the
  backward Replay.

**Algorithm per view.**

1. `flow.Backward(ref liveness)`: plain uses and definitions; no Loans.
2. `flow.Forward(ref borrows)`: the transfer of §5.
   - The problem supplies the join (per-slot set union through `MirLoanSets`).
   - At each head the transfer clears holders outside `reach(live_in)`.
3. `flow.Replay(ref borrows)`, per chain:
   - compute per-statement liveness backward into a buffer of (statements + 1) × ⌈H/64⌉ words, from the successors'
     live-in;
   - replay forward and check each access;
   - compute `live_loans(P)` lazily, once per statement that has an access, as one closure over the live holders.

**Complexity.** N statements, Hd heads, H holders (H_f flow), L Loan sites, W = ⌈L/64⌉, K distinct sets, A accesses.

```text
Work = Θ(N + paths + L)                                         pre-pass
     + Θ(π_b · (N + Hd·⌈H/64⌉))                                 liveness
     + Θ(π_f · (N·c + Hd·H_f)) set-id operations + Θ(K·W)       forward (memo misses build sets)
     + Θ(N·⌈H/64⌉ + Σ_{statements with accesses}(H_live + W))   checks, closure
```

- c is the operand count per statement.
- π_b and π_f are the solver passes: loop depth + 2 for gen/kill parts, plus the longest in-loop copy chain for set
  flows.
- A work counter `MirBorrows.Work` (as `MirEffects.Evaluations` does) counts statements applied, slots joined, set
  words built, closure steps and liveness words.

**V16.**

- On V16's shapes 1–5 (straight line, N locals, branches, nested loops, one local reassigned N times) every term is
  linear.
- The quadratic terms are the dense ones R4-M20 and R4-M24 already admit: heads × holders, and accesses × holder words.
  Output-sized terms are the live Loans per point.
- The set arena makes aliasing cheap: k copies of a reference share one set id.
- Known excess: an in-loop copy chain of length n (`loop { r1 = r2; …; rn = x@ref }`) takes n passes. Every iterative
  dataflow has this, and it is recorded as risk K1. Scaling tests: 2× and 4× on each shape plus this one, with
  counted work.

**Memory.**

- Hd × (⌈H_f/2⌉ + 1) words of set ids, Hd × ⌈H/64⌉ liveness words, K × W set words, and the memo tables.
- `MirDataflow.Prepare` sizes the head arenas (R4-M15a). Set-arena growth beyond the 64 MiB limit is
  `OwnershipStorageLimit_Kd` for the body, the one Resource code.

**Integration.**

- Driver step 4 (R4-M2a): per body, `flow.Begin`, then `moves.Build` (with G7 paths), then the A5 pre-pass; per view,
  the three steps above and `MirOrigins`.
- `MirReach` provides the order and the reachable blocks, as now. `MirDropTable.Group` gives Drop rows (G3).
- A4's summaries are complete before step 4.

**Errors as typed facts.** A failing check records `MirProblem(Code, Subject, Views)` plus a pooled fact row: Loan site
location, access kind, carrier holder local, witness use, reservation call. No check re-runs analysis. The witness
comes from the liveness witness array. Messages come only from the diagnostics adapter (AGENTS, Diagnostics).

## 7. Origin stratum

**Inputs.**

- `OriginRows`, `OriginAtom.Of` and `UniversalRelations.For` (§4.3), per view by case mask.
- MIR containment rows: for each Ref whose Place passes borrow Derefs, edges from each layer's outer Origin to the new
  reference's Origin, up to and including the first shared layer (S15:513, audit-origins row 9c).

The stratum reads no liveness, edges or carries (S15:571).

**Judgment** (audit-origins §2; Θ(N + E + E·⌈F/64⌉ + Ft·Pm) per view):

```text
inner(x) :- inferred(x) ; finite(x, _).      end(x) :- fixed(x) ; finite(x, _).
r(a,b,s) :- end(a), e(a,b,s).                r(a,c,s) :- r(a,b,_), inner(b), e(b,c,s).
refuted(a,b,s) :- r(a,b,s), finite(a,body), fixed(b).                 // UnsatisfiedOriginRelation_Kd at s
unknown(a,b,s) :- r(a,b,s), fixed(a), fixed(b), !entails_K(a,b).      // UnprovenOriginRelation_Kd at s
```

- Refuted is one multi-source search from finite nodes, keeping one witness predecessor.
- Unknown propagates fixed-source bitsets over the strongly connected components of inner nodes, then makes one OJ2
  query per pair.
- With L5, `finite(o, static)` disappears; without L5, static-finite nodes come from A4 anchors (c2).

**Boundary decisions.**

- Binding keeps OJ1–OJ7: signature completion, the premise prover and OJ5 candidates with S3.
- A5 judges every body chain from rows.
- T1 (Type-argument clauses in applicability) stays Binding's until R7. Binding leaves the clauses it proved or failed
  as Constraints out of the rows.
- Split the `ValidateOriginRelations` loop by owner before F4 (`Binding.OriginProof.cs:394-412`).
- A variable without a candidate is a local region in a body (S15:348). Binding stops recording `MissingOriginBinding_Kd`
  for body locals (`Binding.OriginProof.cs:443-449`).
- The containment skip under `preserves results` (`Binding.ArgumentOperations.cs:565`) is not ported (audit-origins
  §7.8).

**Reporters deleted at R6-check** (each failure reported once):

| Row | Deleted |
| --- | --- |
| B1 | `PublishCallRelations`, `ReportMoreCallRelations` (`Binding.CallRelations.cs:182-193, 391-413`) |
| B2 | the Origin branch of `RecordMismatch` and its `Unsupported_Kd` (`Binding.Diagnostics.cs:939-986`; not in P10:546) |
| B4 | the judging call in `JudgeCallClauses` (`Binding.CallRelations.cs:165`) |
| B6 | the locals part of `ValidateOriginRelations`; records `:447`, `:455` |
| B9 | the judging modes of `RequireInputPremises`/`RequireResultPremises`; the suppression at `Binding.ResultPremises.cs:227` becomes an A6 derived rule |
| B14 | `CallLocalOrigin`'s refutation (`Binding.Closures.cs:674`; not in P10:546) |
| B17 | the old reporter `OwnershipAnalysis.Borrows.cs:13-121` and its `Unsupported_Kd` (`:74-79`) |

## 8. Components and files

All new files are in `Mir/Analysis/`. Entry-file contracts are stated in the format AGENTS requires.

| Component (file) | Contract | Lines |
| --- | --- | --- |
| `MirBorrows` (`MirBorrows.cs`) | **Owns** A5's Loan stratum (SPEC 15.6–15.8) per body and view. **In**: validated body, move paths, dataflow, views, A2 rows, A4 summaries, C1/C2 rows. **Out**: Loan problems through `MirReport`. **Invariants**: one Loan per site and case; syntactic places; sets interned. **Must not**: judge Origins, read syntax or HIR, re-derive Binding facts, copy the body per view | 260 (driver, pre-pass) |
| `MirBorrows.Flow.cs` | Forward transfer: issue, flows, kills, follow, write-through, calls, reservations | 380 |
| `MirBorrows.Checks.cs` | Replay: chain liveness buffer, closure, overlap and conflict table, end checks, effects, problem facts | 330 |
| `MirBorrows.Preserves.cs` | S2 must-lane and conformance phantoms | 110 |
| `MirLiveness` (`MirLiveness.cs`) | Backward holder liveness and witnesses. **Must not**: read Loans | 140 |
| `MirLoanSets` (`MirLoanSets.cs`) | Interned Loan sets with memoized union and minus. **Must not**: know Places | 110 |
| `MirOrigins` (`MirOrigins.cs`) | Origin stratum: rows, containment, judge. **Must not**: read liveness or Loans | 300 |
| MIR contracts | `MirDataflow` (+Backward, problem join) 90; `MirMoveData` (G7) 50; `MirTypes` (Observes, MayCarryLoans) 50; `MirDrops` (G3) 30; `MirDescriptors` (rows, Retains) 45; `MirBuilder` (G5, G6, Slice, Guard, Writeback) 140; `MirEffects` publication 130; `MirReport` A5 rules 90; `MirValidator` checks 40 | 665 |
| Binding views | C1 160; C8 25; C2 110; OriginRows, atoms and premises 180 net; C5 40; E4/E5 50 | 565 |
| **Total new** | | **≈ 2,900** (≈ 19k tokens) |

**Deleted at R6-check:**

- Old ownership: `Analysis/Ownership*.cs` 15,446 lines, `BorrowLiveness.cs` 141 and `PackedAnalysisTable.cs` 59. A1–A6
  share this credit. A5's share is the borrow, region, reservation, call, closure, default, comparison and match files,
  about 9,000 lines (H).
- Binding reporters (§7): about 400 lines (H).
- **Net:** about −13,000 lines for the R6-check switch; about −6,500 for A5 alone (H).

File sizes stay under 5,000 tokens each (ARCH structure limits).

## 9. Entries needing owner approval

No new statement, rvalue, terminator, projection, operand or callee kind, and no Case terminator (OD8 is not needed).

1. **TwoPhaseUniq meaning**: activated at the first use of its reference, a Call or the write through it. The member is
   unchanged; DEC entry; O1 adjacent.
2. **Descriptor rows**: StorageDead gains `DropUse` and EndBorrow gains `Reads`; no new `MirAccess` flag. DEC.
3. **`MirDescriptor.Retains`** column for intrinsics. `RawSlice` takes the backing borrow; its arity and `IntrinsicId`
   are unchanged. DEC.
4. **`MirDataflow`**: a problem-supplied join and set-id states; supersedes R4-M20's bit-only join for A5. Backward
   added (R4-M24's successor rule). DEC.
5. **`MirMoveData`**: flow-closed holder paths (G7); refines R4-M21. DEC.
6. **`MirCallee`**: the selected value-call signature column (R4-M23's open issue). **`MirStatic`**: key with Subst. DEC.
7. **SPEC** (owner, OD7 and DEC O3/O4): L2, L3; optional L4 and L5; S1′ adding the raw-site form; S2 stating
   region-split phantoms and §8.11.5; S3; S4; the c1, c2 and row-8 answers (§11). IMPL: the guard-local Origin's end is
   a Loan end.

## 10. Implementation units (plan §6.2)

Each unit is design → reproducer → implementation → focused tests. Behavior-preserving restructurings commit first.

| Unit | Content | Completion condition | Checks |
| --- | --- | --- | --- |
| **Step 0** | | | |
| U0a | `MirDataflow` problem join + `Backward` | A1 unchanged (ledger equal) | A1 suite; liveness tests on V16 shapes, 2×/4× |
| U0b | G4 rows; G2 `Observes`, `MayCarryLoans`; G3 Path and −2 group | A1, A2, A4 unchanged | dump goldens; A2/A4 suites |
| U0c | G5, TwoPhase meaning, Writeback, G6 per-edge EndBorrow and assumed activation, Guard EndBorrow | MIR dumps show reservations on every input; validator accepts | `MirBuildTest`, dump goldens, validator tests |
| U0d | Slice backing borrow, intrinsic `Retains` | R5 lowering unchanged in output (`.ll` equal) | Slice fixtures, dump goldens |
| U0e | G7 flow-closed paths | A1 unchanged on the corpus | `MirBuildTest.BodiesScaleLinearly` |
| U0f | A4 publication (keys with Subst, summaries, Drop effects) | A4 verdicts unchanged | A4 suite; S22:377-381 keys |
| U0g | Origin boundary split of `ValidateOriginRelations` | Binding behavior unchanged | Binding suites |
| **Step 1 (joint core)** | | | |
| U1a | C1 `Retains`, C8 columns | Rows for library calls; immutable after Bind | view tests vs signatures |
| U1b | `MirLoanSets`, `MirLiveness`, `MirBorrows` F1+F2+F3+F10 | The prototype's 41 programs, re-encoded in Kimi where expressible, give their S1′ verdicts; lib and ms produce no A5 problems (R4-M12) | SPEC-example table (census §4: `tests/diagnostics/spec-cases.json`, F1/F2/F3/F10 blocks); scaling 2×/4× with `Work`; shadow run over fixtures, ledger classified |
| U1c | A6 minimum: codes, facts, derived rules | Each problem has its fact row | `MirReport` tests |
| **Step 2** | | | |
| U2a | F5 effects and statics (A4 keys) | S15:540-558 verdicts (with L5) | SPEC table, `CallEffectConflict` tests |
| U2b | F4: `OriginRows`, atoms, premises; `MirOrigins`; containment | audit-origins rule tests; Binding reporters still on (shadow) | origin suites, judge scaling |
| U2c | F7: C2 view, Value-call rows, Closure bodies | S15:715-723, S7:341-344 | Closure suites |
| U2d | F8: match, guards, writeback, collections, updates | S14:332-351, S13:600-602, S15:660-689, S4:570-575 | SPEC table |
| U2e | F9: inline and template defaults | S7:84-88 | default suites |
| U2f | F6: C5 bit, excl lane, conformance | `takeTwo`, Drain valid; `takeAcross`, Merge invalid; the 12 library conformances accepted | SPEC table, library census |
| U2g | A6 completion: E4/E5 keys, R6 deletion list | No double report per §7 | ledger |

## 11. Risks and open questions

**Questions for the owner.**

1. **Row 8 overlap table (audit-core row 8).** The table proves disjointness only between two live `uniq` referents
   (S15:494), so `func f(p: ref/i32, q: uniq/i32)` with `r = p@follow@ref; q@follow = 5; inspect(r)` is literally
   Unproven. This design treats Places through distinct roots as disjoint (sound under L2 and formation checks).
   Confirm the intended reading: "a live `uniq` referent is disjoint from any other live borrow with a distinct
   anchor".
2. **c1.** Is a Borrow reached through a borrowed input inferred (its failure Unknown, today's verdict,
   `PairFollowTest.cs:358-386`) or finite (Refuted)? Recommend inferred: no test churn.
3. **c2 and L5.** Recommend L5. If rejected, choose option (b): the rule stated on values. A value carrying an anchored
   Loan that reaches E, or `_0` without a result bounded by a borrowed input, is the Refuted relation, reported by the
   Loan stratum with A4's anchors.
4. **SPEC gap (H), also the prototype's question 2.** Under the current text, `let z = r@ref` while `c =
   r@follow@uniq` is live, then a call whose callee reads `z@follow@follow`, is accepted, because the caller sees `r`
   and `x` as disjoint. L2 rejects it at the borrow.
5. **A6 witness precision.** Is a block-level witness acceptable when the use is outside the chain, or must it be the
   exact use location? The design stores one per head and holder.
6. **E5 policy** (DIAGNOSTICS change): recommend one representative cause per tainted fact, O(carries).

**Risks.**

- **K1**: copy chains inside loops cost passes (§6). Measure; if a library body trips it, order flow slots by
  dependency.
- **K2**: L2 rejects test programs the old path accepts. The ledger classifies them (d) by SPEC clause; the old path
  already freezes references (`OwnedStoredReferenceTest.cs:230-235`).
- **K3**: S2 conformance needs region-split phantoms. The 12 library Iterator conformances
  (`Iterator.kimi:6`, audit-calls §10) gate U2f; a library rejection goes to DEC O3.
- **K4**: Binding's late mutations (obligation compaction, purging, CheckBound) must be removed for `OriginRows` and
  `Retains` (audit-calls inventory).
- **K5**: A4's depth-32 atoms are still `Unsupported_Kd` (`MirEffects.Solve.cs:394-397`). This needs A4's own SPEC
  sentence (an open-key-like widening) before the R4 exit.
- **K6** (prototype risk 2): untested cases go to U1b's tests: a `written_through` into the base inside a loop, and a
  write through a child after its base was replaced in a loop.
- **K7**: L3 exposure in `IteratorOriginEffectsTest` and multi-slot tests is unmeasured until U1b's shadow run.
- **K8**: L4 false positives; if the shadow run shows any in lib, ms or the corpus, take the 40-line precise variant.
