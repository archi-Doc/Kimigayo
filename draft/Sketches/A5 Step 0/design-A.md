# A5 design A: Polonius-style borrow checking on MIR

Base: worktree `bin/worktrees/a5-step0` (`b2edea8b`). Read-only design; nothing was built or run. Paths: `M/` =
`src/Kimi/Compiler/Mir/`, `B/` = `src/Kimi/Compiler/Binding/`, `A/` = `src/Kimi/Compiler/Analysis/`; `S15:n` = line n of
`docs/spec/15-ownership-and-lifetime-analysis.md` (other chapters alike). "Audit-core/-calls/-origins", "census" and
"prototype" are the step-0 scratch files. S1′ is the coordinator's corrected S1; S2–S4 are assumed adopted. (H) marks an
unverified hypothesis.

## 1. Summary

- **Holders are root locals** (variables, temporaries, parameters) plus one escaped pseudo-holder `E` per body, not move
  paths (P1). Statics never hold body Loans (the Origin stratum refutes it).
- **A holder's Loans are one interned, immutable Loan-set node** (`MirLoanSets`): copies share a node id, unions are
  memoized. A forward `MirDataflow` problem stores one node id per flow holder per head.
- **Loans are named by base paths** (S1′). Access Places are compared **syntactically**, because every borrow and Move of a
  base or ancestor is a deep access (S1′ plus P3, Rust's rule). This removes the prototype's provenance-resolved access
  Places and authority sets (its refinement R3).
- **Provenance is needed only for content**: reads and writes through references and liveness through references use the
  holders a carried Loan's direct Place names, materialized parent Loans, and `E` for storage the body does not own.
- **Liveness** is a backward holder bit problem (the restored `MirDataflow.Backward`). **Checks** run in one replay: per
  chain a backward sweep gives liveness transitions, then a forward sweep applies nodes and checks accesses.
- **Origins**: A5 takes over the old Analysis's role (chains with a finite or inferred node, over Binding's published
  obligation rows plus MIR containment edges); Binding keeps its fixed–fixed OJ judgments. No Binding reporter moves.
- **Simplifications** P5 (static keys by Field) and P6 (mutable-static borrows never leave the body) delete A4's key
  substitution, per-result anchors and question c2.
- **Size**: about 1,850 Mir lines and 420 Binding lines. R6-check deletes 15,646 old lines (46 `Ownership*` files,
  `BorrowLiveness`, `PackedAnalysisTable`).
- **Speed**: linear on the V16 shapes, with dense head terms as R4-M20 reads them. Remaining super-linear terms (aliasing of
  distinct overlapping nodes, §6.3) are counted by `MirBorrows.Work`.

## 2. Precision-loss list (SPEC simplifications)

Counts: lib = `src/Kimi/Library/*.kimi`, ms = 43 milestones, tests = xUnit C# literals (grep proxies). "Needed" means the
design depends on it; "optional" means the stated component grows if it is rejected.

| # | SPEC | Simplified rule | Rejects | lib / ms / tests | Class |
| --- | --- | --- | --- | --- | --- |
| P1 | §15.6 (S15:454 "Move Path granularity") | Holders are variables, temporaries and parameters. Writing or Moving part of a holder adds the written Loans and ends none; reading any part reads all of the holder's Loans; a Drop that may observe Origins uses the whole holder. | Partial replacement ending a part's Loans (`a.0 = z@ref; x = 4; inspect(a)`); reading or destructuring one part of a local aggregate built from several borrows, then conflicting with another part's source while the read part is used; `refs[0] = z@ref` ending `refs[0]`'s Loan (prototype 5b, 7). | 0 / 0 / ≤10 tuple literals with ≥2 borrows in 5 files | Needed (simplicity: no subtree ranges, no G7 flow-closure fix in `MirMoveData`) |
| P3 | §15.6.2 "Inline parts exclude referents" for borrows | While a child Loan with base H is live, a shared borrow of H or an ancestor conflicts with an exclusive child, as an exclusive borrow and a Move already do under S1′. | `let z = r@ref` (or a `ref/Self` call on a struct holding `r`) while `c = r@follow@uniq` is live; Rust rejects it too. | 0 / 0 (H) / within the 27 files using `@follow@uniq` (H, not classified) | Needed (simplicity: syntactic access Places, no authority sets) |
| P4 | §15.6.3 Iterator descent, §15.6.4 step 4 | A result that retains a borrowed input's referent content retains every Loan that content holds; a Closure result retains all captured Loans. | An Iterator or Closure holding two independently borrowed sources whose result names one, followed by a conflicting access to the other source while the result is used (audit-calls row 9). | 0 (no multi-slot struct in lib) / 0 (Milestone16's `choose` retains both by its clause) / small (H) | Needed (follows from P1) |
| P5 | §15.6.2 key bullet, §22.4.4 | All keys of one static Field are one root for Loan conflicts. | A live Loan on `Cache<i32>.S.x` against an access or effect on `Cache<string>.S.x`. | 0 / 0 (Milestone42 touches both keys only by whole reads and writes) / 0 by regex | Optional (removes key Substs in `MirStatic` and open-key composition in A4) |
| P6 | §15.6.4 "Mutable-static anchors", §15.6.5 finite kinds, §11.8 | A Borrow of mutable static storage has a finite Origin bounded by the body, so a value carrying its Loan never leaves the body. | §15.6.4's `current` (valid today) becomes an error; `leak`, `let f = current` and `run` keep their verdicts; §11.8's examples are unchanged. | 0 / 0 / 0 returned static borrows by regex | Optional (removes A4 per-result anchors, `fn_use` restrictions and question c2) |
| P9 | §7.2.3, §15.6.4 | A call of a default template (recursive default SCCs only) retains every prepared input into its result. | A template default whose result type holds references but keeps no input, when a reserved input is then accessed while the callee's result lives. | 0 / 0 / corpus ≤6 sources (H) | Optional (removes an inter-body `default_retains` summary) |
| P10 | §15.6 write through a reference | Reads and writes through a reference reach every holder its carried Loans name, including holders of the references it was reborrowed through. | A Loan-carrying write through a two-layer reborrow (`r@follow@follow@uniq`) followed by a conflict on the intermediate holder. | 0 (lib has no `@follow =`) / 0 (the ms writes store owned or Copy values) / small | Needed (layer-blind provenance; prototype "Limitations") |

Assumed with S1′ (not counted as mine): Move, exclusive borrow, `exchange`/`swap` target of a base while its child is live
(prototype 9k, 9l). **`replace` of the base also counts as an exclusive borrow** (prototype S1′ final rules: "The same
applies to … `replace`"), so MIR needs no `replace` fact; this narrows S1′'s text and should be stated in it. Also the
raw-site form of audit-core row 24 (two live borrows from one raw site in a loop share an anchor).

**Soundness of each item.** P1, P4, P9 and P10 only enlarge carried sets or keep Loans the SPEC ends, and live Loans are
monotone in carried sets, so they add errors and remove none. P3 adds conflicts; together with S1′ it is what makes
syntactic access comparison sound (§3). P5 enlarges overlap. P6 makes the Origin stratum Refute every chain from such a
borrow to a fixed Origin, which covers returns, stores through parameters and captures, and statics; no call result then
carries an anchor, so callers need no anchor fact.

## 3. Holders and references

**Chosen representation: provenance with base-named Loans, per-root holders, Loan-set nodes.**

- **Holder**: a root local whose case Type may hold a Loan (`MirTypes.HoldsLoans`: a borrow layer, pair layer, Slice,
  Function or Closure Type, at any inline depth), plus `E`. A root is a flow holder when its `MirMoveData` root path is a
  flow path (`M/Analysis/MirMoveData.cs:64`); chain-local holders live in the scratch only, as in A1.
- **Loan**: one per non-raw `Ref` site per view (S1′ identity), with its Place, its base (the prefix before the first
  referent step), mode (`ref`, `uniq`, or `res` for `TwoPhaseUniq`), issue location and destination root. A Loan is
  **direct** when its Place has no referent step; it names the holder `root(place)`.
- **Steps**: a *referent step* is a Deref of `ref`, `uniq`, `objref`, `objuniq` or a raw pointer, or a PairLayer that the
  view makes a reference. An *owned step* is a Field, Base, ConstIndex, Downcast or Index, a Deref of `obj`/`rc`/`arc`, or
  an owner-case PairLayer. Owned handle payloads are thus part of their holder: writing or destroying `h` conflicts with
  `h@follow…` Loans, which gives SPEC 13.5.5's handle Loan and S16:194 without a second Loan.
- **Pseudo-Loans**, which never conflict: `Φ_E` (may refer to storage the body does not own) is carried by Loan-capable
  `Arg`s at entry, and `τ_s` (Opaque site s) is carried by Opaque values for A6. A *phantom* `Φ_c` (F6) does conflict.
- **Content of a carried set S**: the nodes of the holders S's direct Loans name, plus `σ(E)` when S holds `Φ_E` or no
  direct Loan. Child Loans are skipped, because a reborrow's holder also carries the parent Loans (prototype R2).

**Why syntactic access Places are sound.** Under S1′ + P3, every alias of a referent is either (a) a direct Place, which
meets the parent Loan that every child holder carries; (b) the same base path, compared syntactically; or (c) formed
through a borrow or Move of the base or an ancestor, which conflicts at formation. Reference parameters are disjoint by the
caller's activation rule (S15:614). This handles prototype 9i (rejected at `z = r@ref`) and its open question 2. (H): it
should be confirmed by a prototype variant "deep `sborrow` + `SyntacticAccess`" over all 41 cases before U1a.

**Liveness through references** (prototype R4) is a closure at check time: a live direct Loan naming T makes `σ(T)` live.
Borrowing T is a use of T, so T is live up to every later borrow. Then, I argue (H), no backward feedback is needed, and
prototype 4b passes through the use. T cannot be written or destroyed while the naming Loan is live without an error, so
`σ(T)` is the content a later read would see.

**Rejected:**

- **Origin positions** (R4-M24, plan §4.1): writes through `uniq` reach the referent only with variance-directed or
  backward flow (Polonius alpha's invariant edges). R4-M24's (definition, cell) holders were the code that stalled; P4 is
  the only gain.
- **Move-path holders**: subtree ranges, ancestor reads and part-wise flows (about 150 lines) plus the G7 fix in the
  `MirMoveData` shared with A1/A2. Call results are flat anyway (plan §4.3).
- **Dense holder × Loan bits**: cubic in heads × holders × Loans (plan §4.6's excluded "materialized carries").
- **Per-Loan Polonius-alpha traversal**: reads and writes through references make Loans interdependent, and the alias
  factor sits on every traversal.
- **Provenance-resolved access Places with authority** (prototype R3): a third resolution path that negates a may-set;
  P3 removes the need.

## 4. Fact schema

Inputs come from producers; derived facts come from §5's rules. One Semantics case view at a time (R4-M11).

| Relation | Keys | Producer | Kind |
| --- | --- | --- | --- |
| `edge(ℓ, ℓ′)`, P1/P2 | location | `MirDataflow` chains, `MirReach` | input |
| `issue(L, ℓ, H, X, m)` | site ℓ | `Ref` rvalues under the view (`MirUses.ForEach(MirCases, …)`) | input |
| `holder(H)`, `flowHolder(H)` | local | `MirTypes.HoldsLoans(cases.Type(…))`, `MirMoveData.FlowCount` | input |
| `named(L, T)`, `child(L)`, `base(L)` | Loan | Place projections, step classification | derived (pre-pass) |
| `access(ℓ, X, k)` | location, Place | `MirUses` rows (`M/MirDescriptors.cs`) plus statement kind | input |
| `use(H, ℓ)`, `kill(H, ℓ)` | holder, location | `MirUses` plus statement kind (as `MirInit` does, `M/Analysis/MirInit.cs:112-121`) | input |
| `observes(ℓ)` | Drop | `MirTypes.Observes(case Type)` and `MirDropTable.Group` verdict ≠ Elide (`M/Analysis/MirDrops.cs:88`) | input |
| `retains(c, i, κ, o)`, κ ∈ {Value, Content}, o ∈ {result, store j} | callee, input slot | Binding `TyCtxt.Retains` (C1/C8), `TyCtxt.Closure` (C2); intrinsics: every operand → result (Value) | input |
| `key(ℓ, f, k)`, `unknown(ℓ)` | Call or Drop | A4 `MirEffects.Keys` | input |
| `preserves(c)`, `recv(c)`, `abstract(c)` | call | `TyCtxt.DispatchBounds`/`CallableBounds` (`B/Binding.EffectObligations.cs:83,98`); receiver operand Place; Requirement or abstract Callable callee | input |
| `σ(H, ℓ)`, `σ(E, ℓ)`, `Act(ℓ)`, `Disturbed(ℓ)` | holder, location | forward rules | derived |
| `live(H, ℓ)` | holder, location | backward rules | derived |
| `LS(ℓ)`, `err(…)` | location | check rules | derived |
| `obligation(site, a, b, mask)`, `atom(o, kind)`, `entails(view, a, b)` | definition | Binding `TyCtxt.OriginObligations`, `OriginAtom.Of`, `UniversalRelations.For` | input (F4) |
| `contain(o_k, o_new, site, mask)` | Borrow | MIR Ref Places up to the first shared layer (audit-origins §3.2) | derived (F4) |

### 4.1 MIR fact contracts (step 0)

| Gap | Contract | Change |
| --- | --- | --- |
| G1 | `MirDataflow.Backward` on the same chains; a P1 chain joins runtime successors only, a P2 chain all (R4-M24's reading); P1 phases known from `Begin` (one runtime `MirReach` walk). The arena holds both problems per head (liveness words, then node slots), and the join is the problem's hook (`IMirFlow.Join(stored, incoming, first)`, A1 keeps OR). | `MirDataflow.cs` +90, `MirInit.cs` +4 |
| G2 | `MirTypes.Observes(T)`: `HoldsLoans(T)` and the `Destruction` walk reports a user `drop`, an abstract part, a Function value or a non-Sealed handle. | `MirTypes.cs` +40 with `HoldsLoans` |
| G3 | **Closed by design.** A5 reads only the group verdict; with per-root holders no Path column is needed. A5 runs only on bodies A1/A2 analysed (driver skip, R4-M15a), so Group −1 always means check-only. | none |
| G4 | **Closed by design.** A5 switches on the statement kind, as A1 does; `MirAccess.Kills` stays. | none |
| G5 | Every exclusive borrow that produces an invocation's receiver or argument on the preparation path is `TwoPhaseUniq`: written `@uniq` arguments (`M/Build/MirBuilder.Values.cs:83`), Reborrows at `uniq` parameters (`:175`), synthesized receivers (`MirBuilder.Expansions.cs:396,443`, `MirBuilder.Formatting.cs:70-87`) and collection-literal element borrows (`Values.cs:232`). | builder, `input` threaded through `Value`/`Into` |
| G6 | An invocation whose operand diverges continues in that operand's CheckTarget continuation (remaining operands and the Call), and pending operands' `EndBorrow` goes on the transfer's runtime edge only (`MirBuilder.Control.cs:135-142`, `MirBuilder.Calls.cs:116-120`). Activation is checked in the assumed state (S14:514, S15:614). | builder |
| G7 | **Not needed** (P1). | none |
| Slice | A built-in range selection borrows its backing (`Shared` or `Uniq` by the Slice's mode) into a temp passed as `RawSlice`'s first operand (arity 2→3, `M/MirDescriptors.cs:146`). The raw `Ref` stays for the pointer (`MirBuilder.Expansions.cs:288-315`). This closes the unsound S4:570-575 acceptance. | builder, intrinsic row, R5 ignores the operand |
| Guard | `EndBorrow` of each `GuardCandidate` local at guard cleanup on every path (S14:325-336). | builder |
| Update | Compound assignment, increment and decrement: at locate, a `Shared` Ref of the destination into a protection temp with role `UpdateGuard`, and `EndBorrow` of it just before the store (S13:595, S15:611). Uses no new statement. | builder, one `MirLocalRole` member (owner) |
| C8 | A Value callee's `MirCallee.Arg` is its selected Callable signature index; a Virtual callee's `Arg` is its slot (today never set, `MirBuilder.Calls.cs:131`). | builder, `MirBody.cs` doc |
| E-keys | `MirOpaque` carries its Binding failure-cause keys (min key = A6's representative cause, the E5 option without DIAGNOSTICS:76 shared sets). | Binding + builder |

`EndBorrow`'s meaning (`M/MirVocabulary.cs:21`) is restated as "a use of the local, after which every Loan issued into it
must be dead and its carrying ends". For abandoned comparisons and reservations this holds trivially. For guards it is the
guard-local Origin end, closing audit-calls' S14:336 gap.

### 4.2 A4 publication

`MirEffects.Keys(body, view, block)` gives the Call or Drop at a block `(bool Unknown, ReadOnlySpan<(int Field, bool Write)>)`.
Field is an index into a program-wide table of mutable static Fields (P5: no Subst, no open keys). The set is the
transitive union over A4's existing worklist (`M/Analysis/MirEffects.Solve.cs`), stored as a bitset per summary node.
Drops use the destruction walk A4 already classifies (Unknown for erased destruction). No per-result anchors (P6). A4
reads nothing from A5. **Generic instantiation**: under P5 keys are Field symbols, which `MirBody.Static` already uses
(`M/MirBody.cs:441`). Without P5, `MirStatic` gains the owner's Origin-erased Type arguments, and A4 composes keys through
each edge's Subst with the open-key rule (S15:499), about +120 lines.

### 4.3 Binding views (smallest shapes)

| View | Shape | Notes |
| --- | --- | --- |
| C1+C8 | `TyCtxt.Retains(BindingSymbol function)` and `Retains(Callable signature)` → interned `Retention(int Input, RetainKind Kind, int Output)` (Output −1 = result, j = writable referent of input j) | Computed per declaration, independent of the Subst: reachability in the signature's Origin graph (declared clauses, well-formedness, Type-parameter occurrences as carriers). An input reached through its outer Origin is Value, one reached only inside its referent Type is Content. Callee premises are folded in (audit-calls C1 row). |
| C2 | `TyCtxt.Closure(k)` → per view, receiver mode and `Retention` rows of the internal call contract | Removes `B/Binding.ValueCalls.cs:428-442, 531-543` `Unsupported` and the transient source set (`B/Binding.Closures.cs:138-186`). |
| C5-Binding | `EffectObligation` gains a Preserves kind; `preserves(c)` from the existing `DispatchBounds`/`CallableBounds` | S2 conformance rows. |
| C1-F4 | `TyCtxt.OriginObligations(definition)` → immutable `(SourceRef Site, BoundOrigin Longer, BoundOrigin Shorter, ulong CaseMask)`; `OriginAtom.Of`; `UniversalRelations.For(definition, case)` | A snapshot of today's `BindingObligation` rows (`B/Binding.Origins.Model.cs:154-162`) at the end of Bind, without later compaction or purge. |
| M39-Binding | none new | `TyCtxt.Adaptation`/`CaseType` exist; template calls use `Retains` of the template signature, or P9. |
| E4/E5 | failure keys per Opaque site | E4's bound children of Error nodes are an A6 completeness follow-up; they are not needed for soundness. |
| U8p | none | The Slice contract issues the backing Loan in MIR. |

## 5. Rules

Notation: ℓ is a location; σ is read "at ℓ" and written "after ℓ". `within(A,B)`: B's chain is a prefix of A's, with an
Index step matching any index step. `refBelow(X,A)`: a referent step of X lies below A.

```text
// ---- Shared (F1)
hits(A, X, deep)   :- within(A, X) ; within(X, A), (deep ; !refBelow(X, A)).   // inline parts exclude referents
deep(k)            :- k ∈ {move, sborrow, uborrow}.                              // S1′ + P3
conflicts(k, m)    :- m = uniq ; k ∈ {write, move, uborrow, destroy}.
mode(L, ℓ)         = uniq if m(L)=uniq, or m(L)=res ∧ L ∈ Act(ℓ), or L is a phantom Φ_c;  else ref.
content(S)         = ⋃{σ(T) | L∈S, named(L,T)} ∪ (σ(E) if Φ_E∈S or no L∈S is named)
refs(S)            = {T | L∈S, named(L,T)} ∪ ({E} if Φ_E∈S or no L∈S is named);  refs^k(S) = refs(content^(k-1)(S))
val(Copy|Move|PairAcquire X) = content^k(σ(root(X)))   // k = referent steps of X (P1: whole root, P10)
val(Aggregate|Cast{Erasure,Upcast,Pointer}) = ⋃ val(operands);  val(BinaryOp|UnaryOp|Discriminant|Len|Const) = ∅
val(Opaque s)      = ⋃ σ(roots of s) ∪ {τ_s};  Adapt: as MirCases.Adapt selects (R4-M19)

// ---- F1/F2 forward: carrying
σ(H, entry)        = {Φ_E} if H is a Loan-capable Arg else ∅;  σ(E, entry) = ∅;  Act = ∅
issue(L,ℓ,H,X,m)   ⇒ σ(H) := {L} ∪ ⋃_{j<k} content^j(σ(root(X)));  Act \= {L}   // R2: parents of every followed layer
assign H := rv       (whole root)       ⇒ σ(H) := val(rv)                         // complete replacement
assign D := rv       (direct, D ⊐ H)    ⇒ σ(H) := σ(H) ∪ val(rv)                  // P1
assign D := rv       (k ≥ 1 referent)   ⇒ ∀T ∈ refs^k(σ(root(D))): σ(T) ∪= val(rv) // adds, ends none (S15:454, S4)
move H (whole root)                     ⇒ σ(H) := ∅                               // a Moved Place holds nothing (S15:15)
StorageLive | StorageDead | EndBorrow H ⇒ σ(H) := ∅
kill(L, ℓ)         :- child(L), ℓ assigns, StorageLives or StorageDeads a direct D with D ⊑ base(L).  // S1′, prefix rule
                   ⇒ L leaves every σ after ℓ, including values flowing at ℓ (R1)
join               σ, Act, Disturbed: per-slot union;  P1/P2 as MirDataflow (DEC R4-M20)
prune(head)        σ(H) := ∅ if ¬live_in(H, head) ∧ no Loan names H              // efficiency only

// ---- F1 backward: liveness
use(H,ℓ)           :- ℓ reads, Moves or borrows a Place rooted at H (through referent steps too, and an assignment through
                      a reference rooted at H); FakeRead; SwitchInt or callee operand; EndBorrow H; Return (H=_0);
                      Drop of a Place rooted at H with observes(ℓ) (check-only Drops included: no group, case Type).
kill(H,ℓ)          :- whole assignment or Call destination H; move H; StorageLive/StorageDead H; EndBorrow H (after use).
live_in(ℓ)         = (live_out(ℓ) \ kill(ℓ)) ∪ use(ℓ);   live_out(ℓ) = ⋃ live_in(successors)   // P1 chain: runtime only

// ---- Checks (F1, F2, F8, F10)
closure(S)         = least S′ ⊇ S with σ(T) ⊆ S′ for every L∈S′, named(L,T)        // liveness through references
LS(ℓ)              = closure(⋃{σ(H) after ℓ | H ∈ live_out(ℓ)} ∪ σ(E) after ℓ)
LS_call(ℓ)         = closure(⋃{σ(H) at ℓ | H ∈ live_in(ℓ)} ∪ σ(E))                   // arguments protected through the call
access(ℓ,X,k)      :- Copy|FakeRead|Discriminant|Len → read; Move → move; Ref Shared|TwoPhaseUniq|Raw → sborrow;
                      Ref Uniq → uborrow; destination → write; StorageDead|Drop → destroy (+ read with deep if observes(ℓ)).
err(conflict,L,ℓ)  :- access(ℓ,X,k), L∈LS(ℓ), real(L), L not issued at ℓ, hits(X, place(L), deep(k)),
                      conflicts(k, mode(L,ℓ)), !excused(L,ℓ), !(k=destroy ∧ X body root ∧ L kept only by _0).
err(ended,L,ℓ)     :- EndBorrow H at ℓ, issue(L,_,H,_,_), L ∈ LS(ℓ).               // guard-local Origin end (F8)

// ---- F3 calls: c at ℓ with arguments a_i, destination D
ret(i, Value)      = val(a_i) ∪ (content(val(a_i)) if a_i is a reference);   ret(i, Content) = content(val(a_i))
out                = ⋃{ret(i,κ) | retains(c,i,κ,result)} ∪ ({Φ_c} if abstract(c) ∧ the result descends)
                   ⇒ assign D := out;  ∀ retains(c,i,κ,store j), ∀T∈refs(val(a_j)): σ(T) ∪= ret(i,κ)
                   ⇒ Act ∪= {L ∈ ⋃ val(a_i) | m(L)=res}
retains for an unknown callee (no signature)   :- every argument → result and → store j for every exclusive a_j.
err(activation,L,ℓ):- R∈val(a_i), m(R)=res, L∈LS_call(ℓ), L≠R, real(L), hits(place(R), place(L), true),
                      conflicts(uborrow, mode(L,ℓ)), !excused(L,ℓ).                 // S15:614 "all at once"
// Reservation: a res Loan is ref until activation, so C1 rejects writes, Moves, destruction and uborrows in the window and
// admits reads and shared borrows (S15:614). Abandonment: G6's EndBorrow on the transfer edge. Iterator descent: Content.

// ---- F5 effects
err(effect,L,ℓ)    :- key(ℓ,f,k), L∈LS_call(ℓ), root(place(L)) is a static of Field f (P5), conflicts(k, mode(L,ℓ)).
err(effect,L,ℓ)    :- unknown(ℓ), L∈LS_call(ℓ), root(place(L)) is a mutable static.  // calls and erased Drops (S16:196)

// ---- F6 preserves results (S2)
Disturbed(after ℓ) ∪= {Φ_c | ℓ writes, Moves or destroys a direct D ⊑ recv(c), or writes through a reference at a Place
                      hitting recv(c)};   issue of Φ_c ⇒ Disturbed \= {Φ_c}
excused(L,ℓ)       :- Call c2 at ℓ, preserves(c2), L = Φ_c1, callee(c1)=callee(c2), recv(c1) ≡ recv(c2), L ∉ Disturbed(ℓ).
// Conformance (H, pending S2 text): a body implementing a preserves requirement starts with an escaped phantom Φ_prev on
// (*self) with a fresh anchor (region-split children, audit-calls C5 condition); a self-Field-path call of a preserves
// requirement undisturbed since entry is excused against it; other calls contribute declared effects only.

// ---- F7 closures: formation is an Aggregate(Closure) flow; [x@ref] issues a Loan on the outer slot; a call of a Closure value
// uses retains from TyCtxt.Closure (receiver = input 0); a Consuming call Moves the Closure; erasure is a Cast flow.
// ---- F9 defaults: inline copies are ordinary code in an Inlined scope (inspection Loans end before activation by
// err(activation)); a template call uses Retains of the template signature, or P9.
// ---- F10: forward on P1 + P2 edges, liveness by the chain rule above, checks in every reached chain (S14:514).
```

**A6 mapping** (`MirReport`): conflict with an unactivated `res` Loan → `CallReservationConflict_Kd`; activation →
`CallActivationConflict_Kd`; a Loan issued into an `UpdateGuard` temp → `PlacementActivationConflict_Kd`; effect →
`CallEffectConflict_Kd`; a `PreparedInput` root → `DefaultArgumentAccess_Kd`/`DefaultArgumentBorrow_Kd` (R4-M10); `ended` on
a `GuardCandidate` → `UnsatisfiedOriginRelation_Kd` (owner, §11); otherwise `ComparisonLoanConflict_Kd`. A problem whose
access is an Opaque site's or whose witness holder carries a `τ_s` is derived, with cause s (R4-M10, E5).

**Audit corrections in the rules.**

- `EndBorrow` is a use, then an end (`use` and `kill`).
- Moves end carrying (`move H`).
- Check-only Drops are destroy accesses and observations without an A2 row.
- S1′ names Loans by base and kills on assignments and storage ends of any prefix of the base (audit-core finding 2);
  `exchange`, `swap` and `replace` are deep exclusive borrows.
- Joins are unions (audit-core §14.10.3 verdict).

## 6. Evaluator

### 6.1 Algorithm and data

Per body view, step 4 of `MirAnalysis` (after A4's `Run`). `flow.Begin` and `moves.Build` are redone per body.

1. **Pre-pass**, one `MirUses` walk: holders, Loans (CSR indexes by place root and by base root for kills), named holders,
   per-root summary flags. A view with no real Loan skips the Loan stratum (census: 161 + 266 + 16 Loan-free views in
   fixtures, tests and SPEC). If no access can conflict location-insensitively, by root flags, the stratum skips too:
   bodies that borrow only through parameters and only read.
2. **Liveness solve**: `flow.Backward` over `⌈Hf/64⌉` words.
3. **Carrying solve**: `flow.Forward` over `Hf + 3` slots (holders, `E`, `Act`, `Disturbed`) with the node-union join
   and head pruning.
4. **Replay**, per reached chain:
   - a backward sweep from the tail's live-out (the join of successors' stored live-in) records each holder's death
     locations and next use into a pooled list;
   - a forward sweep from the head's node slots applies the transfer and checks each candidate access against `LS`,
     computed from the live holders (bit scan), node-id dedupe (a stamp per node) and the closure.

**`MirLoanSets`**: sorted `int[]` contents in one pooled pool with offsets; hash interning; memo dictionaries for
`Add(n, L)`, `Union(a, b)` (normalized, with `a ⊆ b` and identity shortcuts) and `Remove(n, K)` for S1′ kills, where K
is the kill's precomputed child-Loan set. Node 0 is ∅. Storage resets per view by counts.

### 6.2 Complexity and work counter

N locations, Hd heads, Hf flow holders, H all holders, L Loans, A★ candidate accesses, ρ head re-visits, ν node creations
weighted by size:

`W = Θ(N + L) + ρ_b·(N + Hd·⌈Hf/64⌉) + ρ_f·(N + Hd·Hf) + ν + Θ(N + Hd·Hf) + Σ_{ℓ∈A★} (⌈H/64⌉ + Σ_{distinct live or closure nodes} |node|)`

`MirBorrows.Work` counts transfer steps, slot joins, node bytes created and check scans (as `MirEffects.Evaluations` does).
Scaling tests at 2× and 4× check W/(input + output):

1. N straight statements with one reference;
2. N references to distinct places, all used at the end;
3. N Copies of one reference, live to the end;
4. N sequential loops with M live references;
5. N writes through one reference;
6. a loop with N borrow/write pairs of one place;
7. N chained locals and N signature Origins for the Origin stratum.

### 6.3 V16 argument and memory

- **Linear terms.** The `N`, `L` and replay terms are linear. `Hd·⌈Hf/64⌉` and `Hd·Hf` are R4-M20's dense reading (linear
  in heads at fixed holders and the reverse); only Loan-capable flow roots count.
- **Node creations (ν).** Each created node is the carried set of a live or named holder at a head or flow, so it is
  charged to the live Loans there.
- **Alias factor.** Copies share node ids, so shape 3 is linear.
- **Remaining super-linear term.** k distinct but overlapping live nodes at one point cost Σ|node| against output |∪|.
  This covers k holders that each received different unions holding the same Loans, a k-way write through a reference
  (audit-core row 5) and a k-operand use. It appears only at candidate accesses and joins, and is counted in W. Removing it
  needs node-level reference counting (R4-M24-style differences), which is more code and the owner's call (§11).
- **Memory.** The arena per head is `⌈Hf/64⌉ + Hf + 3` words, sized once in `MirDataflow.Prepare` against the 64 MiB
  limit (R4-M15a); over the limit the body records `OwnershipStorageLimit_Kd` once. The node pool and the replay lists are
  pooled. The replay lists are O(events of the longest chain).

### 6.4 Integration

- **`MirDataflow`** (G1) owns both solves, the chains, the P1/P2 rule and the arena. A5 supplies two struct transfers
  (`IMirFlow` and `IMirBackFlow`) with join hooks and never walks edges itself. `Replay` gives each reached chain its head
  state; A5's replay struct performs the two sweeps.
- **`MirReach`** stays the one traversal: `Begin` adds one runtime walk for the P1 phases.
- **`MirCases`**: views are selected by the driver; a body without a case-dependent access (`MirMoveData.ViewDependent`)
  runs once for every view, as A1 does.
- **A2**: only `Group` verdicts are read.

### 6.5 Errors as typed facts

Each error adds `MirProblem(code, subject = access syntax, views)` plus fact rows (R4-M3a: added by their first
producer):

- the Loan anchor (issue syntax);
- the witness holder's next use, from the replay's backward sweep (a chain exit when it lies in a successor);
- for kills and activation, the other Loan's anchor.

No check re-runs analysis to explain itself (AGENTS "Diagnostics").

## 7. Origin stratum

**Algorithm (per body, per distinct case-mask class of its rows).**

- Nodes are the atoms of `OriginObligations` rows and of MIR containment edges. They are classified by `OriginAtom.Of`:
  fixed; finite(body), which includes mutable-static Borrows under P6; or inferred, which includes raw anchors and Borrows
  reached through a borrow (c1).
- **Refuted**: one multi-source BFS from finite nodes through inner nodes. Reaching a fixed node is
  `UnsatisfiedOriginRelation_Kd` at the site of the last edge, with one predecessor per node for evidence.
- **Unknown**: fixed-source bitsets (64 per word) are propagated from fixed nodes through inner nodes by a worklist. At an
  edge into a fixed target g carrying source f with `¬entails(f, g)`, the result is `UnprovenOriginRelation_Kd`.
- Bound: Θ(N_o + E_o·⌈F/64⌉·ρ + Ft·Pm) (audit-origins §3.3). The stratum reads no liveness, no CFG edge and no σ. Loan
  flow reads only `Retains`. The two strata are independent, and A6 merges their problems.

**Containment** (row 9c): a Ref through referent steps adds `contain(o_k, o_new)` for each referent layer up to and
including the first shared layer, using `MirCases.Layer` for pair layers. Edges between fixed atoms are entailed by
Binding's meet (`PathReborrowOrigin`), so they report nothing twice.

**Boundary decision.** Binding keeps every relation it forms directly between two fixed Origins (OJ1–OJ7; B1's and B2's
fixed ends, B3, B4, B6's fixed part, B7, B9, B10, B11, B14 Closure results, B15, T1–T4). A5 judges every published
obligation row (a finite or inferred end) and every chain through inner nodes (B5 occurrences, B8, B13, B16, B17, B18).
This is exactly the old Analysis's role (`A/OwnershipAnalysis.Borrows.cs:13-121`, `A/OwnershipAnalysis.cs:228-240`), so no
relation is judged twice.

**Deleted at R6-check:**

- the old reporter, through the deletion of `A/`;
- the `Unsupported_Kd` gates `A/OwnershipAnalysis.Borrows.cs:74-79` and `B/Binding.Diagnostics.cs:985`, whose chains become
  rows;
- the `CallBorrows` discharge skip (`B/Binding.TypeOrigins.cs:182-192`).

**Not deleted:** Binding's `PublishCallRelations`, `RecordMismatch`'s Origin branch, `JudgeCallClauses` and
`CallLocalOrigin` stay OJ judgments, so PLAN10's two omissions (audit-origins §7.3) cannot double-report. Moving fixed–fixed
judgment to A5 (`TyCtxt.Relate`/`Clauses`, fits regenerated from MIR) is an R7 consolidation, not a completeness need.

**Prerequisite Binding fixes** (open issues):

- split `ValidateOriginRelations` by owner (audit-origins §7.2);
- a body variable without a principal candidate is a local region, not `MissingOriginBinding_Kd` (S15:348; §7.7);
- a census of the `preserves results` containment skip (`B/Binding.ArgumentOperations.cs:565`; §7.8).

## 8. Components and files

| File | Contract (entry file starts with it) | Lines |
| --- | --- | ---: |
| `Mir/Analysis/MirBorrows.cs` | Owns A5's Loan stratum (SPEC 15.6–15.8) per body view. Input: validated body, view, move paths, dataflow, A2 groups, A4 keys, Binding `Retains`/`Closure`/bounds. Output: Loan, reservation, activation, effect and default problems through `MirReport`, with facts. Invariants: holders are Loan-capable roots plus `E`; Loans are named by base; access Places are compared syntactically; regions never keep a Loan live. Must not read syntax or HIR, judge Origins, re-derive retention, or copy the body per view. Holds the pre-pass, tables and driver. | 280 |
| `Mir/Analysis/MirBorrows.Flow.cs` | Forward carrying transfer and join | 280 |
| `Mir/Analysis/MirBorrows.Liveness.cs` | Backward holder liveness and the per-chain transitions | 130 |
| `Mir/Analysis/MirBorrows.Checks.cs` | Accesses, `hits`, `LS` closure, conflict, activation, effect, `ended`, S2 exclusion | 280 |
| `Mir/Analysis/MirLoanSets.cs` | Owns interned immutable Loan sets: Add, Union, Remove, Contains; pooled; no other Loan-set form in Mir | 130 |
| `Mir/Analysis/MirOrigins.cs` | Owns A5's Origin stratum (SPEC 15.6.1, 15.6.5, 15.3.6 consumer): judges obligation rows and containment per case-mask class; Refuted/Unknown with witnesses; reads no liveness or Loans | 260 |
| `MirDataflow.cs`, `MirInit.cs` | G1 | +94 |
| `MirTypes.cs` | `HoldsLoans`, `Observes` | +40 |
| `Mir/Build/*` | G5, G6, Slice, Guard, Update, C8 | +120 |
| `MirEffects*.cs` | `Keys` (P5 bitsets) | +110 |
| `MirReport.cs`, `MirAnalysis.cs` | A6 mapping, derived taint, facts; step 4 rows | +135 |
| Binding (`TyCtxt.Retention.cs`, `TyCtxt.Origins.cs`, `Binding.Closures.cs`, `Binding.EffectObligations.cs`, Opaque keys) | §4.3 views | +420 |
| **Total added** | | **≈2,270** |

- **Deleted at R6-check**: `A/Ownership*` (46 files, 15,446 lines), `A/BorrowLiveness.cs` (141), `A/PackedAnalysisTable.cs`
  (59), the two `Unsupported_Kd` gates and the Transitional couplings (`Binding.IsEffectRecord`, the 183
  `.Ownership.Bodies` test reads migrated per family).
- **Net**: about −13,400 lines, or about −11,250 counting the A1–A4 MIR analyses already landed (`Mir/Analysis` 2,127
  lines).
- **Each new file stays under 5,000 tokens.** `MirBorrows.*` splits by concern (`Owner.Concern.cs`).

## 9. Entries needing owner approval

1. **No new MIR statement, rvalue, terminator, projection, operand or callee kind.** The six closed vocabularies are
   unchanged.
2. **`MirLocalRole.UpdateGuard`**, the compound-assignment protection temp, for A6's code. Alternative: map by the
   protection Ref's statement shape in A6, which is a consumer deciding by shape; rejected.
3. **`RawSlice` operand** for the backing borrow (intrinsic row arity 2→3). R5 ignores it.
4. **`EndBorrow`'s restated meaning** (§4.1) and **`MirCallee.Arg`** for Value callees (selected signature).
5. **DEC entries** (in the implementing commits):
   - A5 representation: per-root holders, Loan-set nodes, base-named Loans, syntactic access with deep borrows, `E`;
   - `MirDataflow` join hook and backward direction, superseding R4-M20's word-OR detail and replacing R4-M24's withdrawn
     backward clause;
   - A4 key publication;
   - the Origin boundary of §7;
   - G3, G4 and G7 closed by design.
6. **`PublishedBindingTypes` additions**: `Retention`, `OriginObligation` and the atom classification (R4-M23-style allow
   list).
7. **SPEC simplifications** P1, P3, P4, P10 (needed) and P5, P6, P9 (optional), with S1′ narrowed to include `replace`.
   Each goes through OD7, and DEC O3 applies if a library or milestone program changes verdict (none is expected).

## 10. Implementation units (plan §6.2)

| Unit | Content | Completion condition | Checks |
| --- | --- | --- | --- |
| 0a | G1 `MirDataflow` | A1 behavior-preserving; a backward test problem | `MirDataflow`/A1 tests, `MirBuildTest.BodiesScaleLinearly`, ledger rows unchanged |
| 0b | G2 `HoldsLoans`, `Observes` | Classified over library Types | Unit tests on S15:598-604 `Logger`, Sealed/abstract parts |
| 0c | Builder contracts G5, G6, Slice, Guard, Update, C8 | Dump goldens updated; validator clean on lib/ms | `MirBuildTest`, dump goldens, R5 codegen selector unaffected |
| 0d | A4 `Keys` | Keys per Call/Drop; work linear | `MirEffects` tests, scaling |
| 0e | Binding views (§4.3), C2 `Unsupported` removed | Views immutable after Bind; allow list updated | Binding view tests, `KimiLibraryShapeTest` |
| 0f | Prototype variant (deep `sborrow`, syntactic access, closure without feedback) | All 41 cases pass | `proto.cs` run recorded |
| 1a | `MirLoanSets`, `MirBorrows` F1, F2, F3, F10 | Library and milestones produce no A5 problem (R4-M12) | SPEC-example table (`tests/diagnostics/spec-cases.json`, census §4: about 40 error and 34 accepting variants for these families); prototype cases as MIR-layer tests; scaling shapes 1–6 |
| 1b | A6 mapping and derived taint for 1a's codes | Codes per §5 | Corpus snapshots; ledger re-run |
| 2a | F4 `MirOrigins` | audit-origins §3 tests; old reporter's rows matched | Scaling shape 7; ledger Origin rows |
| 2b | F5 (keys, unknown, P6) | S15:540-558 per P6; S16:199-202 | Rule tests |
| 2c | F7 Closures | S15:715-723, S7:341-344 | Rule tests |
| 2d | F8 updates, match, guards, compound | S14:332-351, S15:650-689, S13:600-602 | Rule tests |
| 2e | F9 defaults | S7:84-88 and audit-calls row 26 | Rule tests |
| 2f | F6 S2 | `takeTwo`, `takeAcross`, Drain, Merge; 12 library conformances accepted | Rule tests, library corpus |
| Exit | R6-check deletion list (§7, §8), test migration | Every family complete, no `Unsupported_Kd` family | Session verify, final review |

## 11. Risks and open questions

- **Row 8 overlap table** (audit-core). Syntactic base comparison treats referents of distinct bases as disjoint, so it
  accepts `p: ref/i32, q: uniq/i32` with a live `p@follow@ref` and a write through q. That is sound, since the caller's
  activation rule makes them disjoint, but it is broader than the literal "both `uniq`" row. Owner: clarify S15:494 as
  "referents of live borrows with distinct anchors are disjoint when one is exclusive".
- **c1**: Recommend the current reading, inferred (Unknown). The text's finite kinds exclude Borrows through a borrow
  (S15:567). Clarification only.
- **c2**: Dissolved by P6. If P6 is rejected: the Loan stratum checks a mutable-static Loan written into `E`, into a static
  or held by `_0` at Return when the result is not bounded by a borrowed input (option (b), about +40 lines), and A4 must
  publish per-result anchors through calls.
- **V16 residue** (§6.3). Accept W with the overlapping-node term, or require node reference counting.
- **Returned local borrow.** The rule `!(destroy ∧ body root ∧ kept only by _0)` gives one report (the Refuted relation,
  S15:575) instead of two. Confirm it as SPEC/DIAGNOSTICS wording.
- **Guard `ended` code**: `UnsatisfiedOriginRelation_Kd` (the SPEC calls it the Origin's end) or a Loan-conflict code.
- **(H) items for 0f.** Syntactic access soundness without authority; closure without backward feedback; P3's counts in the
  27 test files.
- **A4 depth-32 `Limit` → `Unsupported_Kd`** (`M/Analysis/MirEffects.Solve.cs:394-397`) is a family left Unsupported
  (audit-calls finding 6). It needs a SPEC sentence like S22:374's open key, or an owner exception under OD1.
- **F6 conformance** depends on S2's final text (fresh-anchor phantoms; §8.11.5 restated).
- **Binding immutability**: the C1/C2 views must snapshot after late mutations (certificate loop, CheckBound;
  audit-calls inventory). Each view unit names and removes them.
- **R5 conflict**: Slice/Update/G6 builder changes touch shared MIR files; serialize them with R5 (plan §9.1).
