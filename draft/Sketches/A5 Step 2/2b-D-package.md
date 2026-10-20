# 2b-D package: A5-F5 effects and static anchors, A4's keys, Transferable

Copied from the R4 session's scratch on 2026-10-21; references to design-A.md, design-B.md, review.md, package.md and scratch probe folders name files kept outside the repository.

Consolidated by designer A on the planning session's instruction (2026-10-21), from design A, design B and the design review
(`review.md`), which read both. It follows the review's recommended combination item by item. The planning session confirmed one
reading: e1's code follows the A6 table's row 1 (§1.5), recorded in DEC as a clarification of R4-M72. Base: `origin/dev`
`e0f24c83` (2b-S5 landed as R4-M87; 2b-2 landed). No repository edit. "(H)" marks a reading or estimate no run checked.

Sources per item: **A** design A, **B** design B, **R** the review's correction.

| Item | Taken from |
| --- | --- |
| ER-effects code path: two `AccessKind` rows, W6, e1's code, per-view keys, R3 through the solve, val(X) at every Drop | A (fixes B-2, B-3) |
| Effect facts (three `MirFactKind` rows), shallow whole-Field reach, a static's Loan naming E (G136 (i)) | B |
| staticEnsure: PD12 (ii) as written, a located U for every key where a live Loan on the Field would decide (e10f) | the review's §7 P-1 (both designs' closed-key exactness withdrawn) |
| `Keys` returns the row, queried per view | B's row, A's signature |
| 2b-7: StaticInit, G157 (9), both G126 A4 items | A |
| Transferable summary: B's shape with one layer obligation; A's SPEC 8.3.3 point | B + R (fixes A-1, B-1) |
| Transferable anchor: one verdict per static under its container's premises, read by A4 through `TyCtxt.Transferable(staticField)`, no `MirStatic` field | B's anchor, A's channel (fixes A-2) |
| G174: one summary shared with Owned; 2x-P12 prototype with the review's added cases; 2x-R14 intrinsic table | B + R (fixes A-3, A-4) |
| Units: 2x-R13's cut and the `MirProblem.cs` ruling (B); the 2b-8 split with three (e) records in 2b-8a (A + B); V16 shape (B) | combined |

## 1. ER-effects (unit 2b-3)

### 1.1 Required meaning

| SPEC rule | What the representation lacks | Owner | Unnecessary afterwards |
| --- | --- | --- | --- |
| 15.6.4: a call or direct static access that conflicts with a live Loan (15.6.2) is an error. Unknown environment effects and an erased destruction (16.3.3) meet every live mutable-static Loan; immutable-static Loans are never affected | A5 reads no effect: e1, e3, e4, e6, e9s, e11 and e15 are accepted (unsound) | A5 (`MirBorrows`), reading A4's keys | DIAGNOSTICS' "not reported on the MIR until unit 2b" |
| 15.6.7: every reservation becomes live at once with the call's effects on static storage and cleanup (W6) | A two-phase Loan is shared in its holder at its own call | the conflicts relation | — |
| 16.4 with 16.3.1, 16.3.3: destruction holds an exclusive Loan on the destroyed storage; an access through a static Field is a conflict (R3) | A static's old value is checked only against Loans the body formed (e8, e9 accepted) | A5 | — |
| 15.6.4 with 22.4.3: a first access initializes; a live Loan anchored to a Field proves only that Field initialized | The ensure's initializer effects need the proof, and the check set is a may-set (SPEC 15.6) | A5 | — |
| 15.6: a reference from static storage reaches storage the body does not own | A static's Loan names no holder, so a store through it goes nowhere (`MirBorrows.Values.cs` `Content`/`Refs`) | A5 (`Loans`, `Holders`) | the "names no holder" lemma |
| 15.6.3 with 15.6 ("a Place root like a local"), 15.1.3, 22.4.4: `=` ends the Loans a Move Path exactly prefixes | A whole static write ends none of its Loans (e16b's second record) | A5 (`Endings`) | e16b's duplicate record (OQ-F5-1 (a)) |
| R4-M10, R4-M70 (Derived), AGENTS Diagnostics (Limit) | A4's one `bool` merges Unknown, Limit and Derived, so e8c and e12a get Language errors and e13a a derived one | A4 (`MirEffects.Keys`) | the `bool` (OQ6 (a)) |

Sites sharing the cause: `MirUses`' effect consumers are A4 (`Uses.Effect`) and A5 (`Accessing.Effect`, empty), with no third.
Every Loan problem goes through `Report`. F6's Φ_c (`CallEffectConflict_Kd`, 2f) is a different rule that shares only `Report`. A1
skips statics, and A2 keeps a static's Drop whole (a Keep row without a path). Static storage as storage the body does not own has
four readers (`Root`, `Store`, `Content`, `Refs`); the last two are the ones G136 (i) corrects.

### 1.2 Rules

Notation: ℓ is a Call or Drop terminator under the view. An **effect root** is a mutable static that a Loan of the view is on;
`Loans` lists the effect roots. `ES(ℓ)` is the effect set (§1.3). `key(ℓ, f, k)` and the classes come from A4 (§2).

- **R1, meeting (SPEC 15.6.4, 15.6.2).** For each effect root f with a candidate in ES(ℓ):
  - a write bit of f, or Unknown, is an `EffectWrite` access of f's whole root;
  - otherwise a read bit is an `EffectRead` access.

  `EffectWrite` meets a Loan in any mode. `EffectRead` meets a Loan that is unique in the holder where it was found, or one that
  ℓ activates (W6, §1.4).
- **Reach (SPEC 15.6.2; B).** Both rows hit as a shallow access of the whole root: as `Read` and `Write` with no steps. A part Loan
  is reached (the prefix rule). A Loan past a raw or shared dereference is not.
  - A safe static-rooted Loan has no shared or exclusive dereference step: a Borrow past a shared dereference forms no Loan (E3),
    and no `uniq` Origin is `static` (15.2.3). Object payload steps are `StepClass.Own`, which the shallow rule passes through.
    Raw-step Loans are not compared (15.6.2's raw row).
  - So this is SPEC 15.6.4's per-Field granularity, not a loss.
- **Unknown** meets every effect root (SPEC 15.6.4, 16.3.3).
- **Limit (AGENTS Diagnostics; R4-M38's Open).** If Limit is present without Unknown, and some effect root has a candidate in
  ES(ℓ) that no key or Unknown meets, ℓ gets a located `Unsupported_Kd`. This is a limit record: it is independent of a Language
  problem at ℓ and is kept inside copies.
- **Derived (R4-M10, R4-M70)** reports nothing by itself. Keys beside it stay checked.
- **R3, own destruction (SPEC 16.4 with 16.3.1, 16.3.3).** Consider a Drop whose Place (whole or part) is rooted at a mutable
  static f.
  - A key of the Drop on f (read or write), or Unknown, is `ComparisonLoanConflict_Kd` at the Drop, anchored at the Drop.
  - Limit without either is the located `Unsupported_Kd`.
  - No Loan is issued: the hold lives only while the Drop runs, so no value carries it. Its only participating scope is the Drop's,
    which 2e-D's copy rule reads (round 20 finding 4).
  - A destroying call on a static target (`replace`, the `clear`s) needs no R3: its receiver reservation is the hold, and W6 meets it.
- **R4, staticEnsure (SPEC 15.6.4, 22.4.3, 22.4.4; PD12 (ii) as written).** Let ℓ be an ensure of static e, the static that
  `MirProgram.StaticInit(body, operand)` names. If ES(ℓ) holds a candidate on e's root, in any mode, each conflict R1 would report
  at ℓ is instead the located `Unsupported_Kd` at ℓ, for every key (PD12 (ii) as written; round 18's R18): ES(ℓ) is a may-set, so
  a Loan on e carried on one path proves nothing on a path where e was never accessed (e10f).

  Without such a candidate, ℓ's keys are ordinary effects (e10b). SPEC 15.6.4's "proves" reads liveness as a may-property; this gap
  goes to the owner as OQ-F5-6.
- **Per view (R4-M11: views are never collapsed; A).** When the run stands for one view, A5 queries `Keys` for that view. When it
  stands for several (a body with no case-dependent access, `views` with several bits), A5 queries each view and records each
  conflict with the mask of the views whose keys meet it; each view's Limit is recorded per view the same way. `Keys` reselects the
  case view, so the rule reselects the run's view afterwards.
- **G136 (i), a static's Loan names E (SPEC 15.6; B).** A direct Loan on a static names E's slot (a sentinel in `Issue`, mapped to
  slot 0 in `Holders`).
  - `Content` gives σ(E), and `Refs` and W reach E; W's step uses `MirWrites.None` for a Loan named E.
  - Check sets do not change, because σ(E) is in each set and E is always reached.
  - A store through a static borrow now lands in E, as a parameter twin's does today (`p@follow = x@ref; x = 1`).
  - A valid program stores only `static`-Origin values in static storage (11.8, 22.4.4).
  - This departs from PD12 (iv)'s lemma (OQ-F5-5, item 2).
- **G136 (ii), the ending (OQ-F5-1).** Under (a), `Ending` of a static Place without a projection returns the static group's range
  (`Subtree`), while a part write ends nothing.
  - Sound under P5: a whole write meets every live Loan of its root before it ends them, so it ends only Loans it reported (as an
    error or a U) or dead ones (round 18 (iii): no verdict changes).

### 1.3 Sets (SPEC 15.6, E13 (1)(b), (d); round 18's R2; A)

- **Call.** The set an access after the last operand takes is `Takes(n − 1)`: Based (the holders live after ℓ that ℓ does not
  overwrite, and E) plus every operand's `Valued` tag. A call without operands uses `TakesStores`, whose result carries no Loan of
  its own.
  - E13 (1)(b)'s "t left out" does not apply to effects; no holder is left out. Round 18's `R18LiveOnly` is unsound on e4, e4m and
    e6.
  - DEC records this reading of E13 (1)(b).
- **Drop.** Every Drop uses `TakesValue`, which is Based plus the `Observed` pass on val(X), as round 18's R2 states. No argued
  equality with the Drop's own access set is needed.
- **Mechanism.** One effect access is added to the location's access list before the gather, so `TakenValues` runs the `Valued` or
  `Observed` pass. This happens at a Call or Drop of a view with effect roots, or at a Drop of a mutable static. After the gather, if
  an effect root has a candidate (`First(root) != None`) or R3 applies, `MirBorrows.Effects.cs` queries the keys and runs R1–R4
  through `Conflict` and `Report`.
- **View skip.** The skip becomes "no Loan, no guard candidate and no Drop of a mutable static". R3 needs no Loan, but it reports
  through `Report`, whose hold needs the forward solve. So the hold stays one rule, and nothing assumes that a Loan-free view is
  never held.

### 1.4 W6, codes and facts

- **W6 (SPEC 15.6.7, A3 E13 (1)).**
  - At ℓ's effect rows, a reservation that ℓ activates counts as unique: the mode test reads ℓ's `reserving`.
  - In `Code`, that reservation is not `Reserved`, because it is activated at its own call.
  - The operands' own accesses at ℓ still see it as shared (E13 (1)).
- **Codes (R4-M72, clarified).** An effect problem is `ComparisonLoanConflict_Kd` at ℓ, except one whose Loan is found only in a
  reservation holder that ℓ does not activate.
  - That exception is `CallReservationConflict_Kd`: the DIAGNOSTICS A6 table's row 1 ("A Loan found in its reservation holder,
    whatever the access") and A3 E13 (6) (R4-M57's precedence for every access), through the existing `Code`.
  - Example: e1, `State.count += g()`, where g's Unknown effects meet the writeback's pending reservation `_6 = &uniq two-phase
    static0`. The explicit twin is already `CallReservationConflict_Kd`.
  - R4-M72's effect row is clarified, not superseded: it replaced A3 §5's `CallEffectConflict_Kd`, and its claim that "none arises"
    overlooked implicit static reservations.
  - An activation conflict and an effect conflict on one Loan stay two problems (e4c).
  - At one Drop, an access problem and an effect problem merge under R4-M71's key (one code, one subject), keeping the first
    producer's facts.
- **Facts (PD31; AGENTS Diagnostics: typed facts where the check fails; B).**

| Problem | Subject | Facts |
| --- | --- | --- |
| R1 | the Call or Drop | `Anchor` (the Loan's borrow); `Use`/`Store` (its witness, as today); one of `EffectWrite`, `EffectUnknown`, `EffectRead`, chosen by what decided it (a write bit, else Unknown, else the read bit) and sited at the met Field's declaration (its StaticInit body's `Source`; every static has one); `Inlined` inside a copy |
| R3 | the Drop | `Anchor` = the Drop (the destruction's lending point); the effect fact on the destroyed Field |
| Limit, ensure whose proof would decide, hold, taint | as today | none (location-only) |

Every effect problem has the requirement `Ownership.ComparisonLoanConflict`. `MirFactKind` gains three rows; it is not an
ARCH-closed vocabulary. DEC records the rows, and the planning session records that `MirProblem.cs`, which is due, has no split
(the ruling unit in §5). R5's H3 maps the three kinds (an ask, §4).

### 1.5 Composition with neighbours

- **R4-M73's body hold.** Effect and R3 problems go through `Report`, so in a held view they are a located `Unsupported_Kd` at
  their access (e17).
- **R4-M70's default taint.** A Loan that only tainted sets hold gives the `Unsupported_Kd` at the omitting call. A clean Loan is
  reported first.
- **R4-M69, G171 and 2e-D.**
  - An effect conflict found in an inline copy carries the Inlined fact and is dropped, as every copy-found conflict is today.
    Round 20's dc20-fx joins G171 (unsound until 2e-D); this is not a regression.
  - Limit and ensure records stay in copies.
  - For 2e-D's scope rule, R1 participates with ℓ's scope, the met Loan's issuing scope and, under W6, the activated reservation's
    issuing scope. R3 participates with the Drop's scope alone, so in a copy it is always the template's.
- **2b-S5 (R4-M87, landed).** A widened atom is Unknown in A4 and meets every mutable-static Loan (SPEC 15.6.4's `visitCopies`).
  The plan's "growing atoms" clause goes, and 2b-3 tests widened effects.
- **2b-6 (BU).** After 2b-6, no explicit static Borrow reaches A5. Binding reports a located `Unsupported_Kd`, which becomes an
  Opaque `{Φ_E}`; this includes the unqualified in-group form (BU confirmed it on 2026-10-21). So 2b-3's tests use implicit static
  Loans only: a writeback reservation, a static receiver (reserved, or shared with a retained result), a `for` or `match` Subject
  borrow, a comparison inspection, an argument adaptation.

### 1.6 Census, cost and memory

- **R3's letter (round 18 finding 2).** Every whole write of a mutable static whose old value's destruction is erased is an error,
  and `exchange` is the way out.
  - Library: no mutable static.
  - Milestone 42: `replaced: Resource`, whose drop writes only Console.
  - Milestone 43: `lastSeen: Option<Weak<rc/Lamp>>`, whose Weak's destruction runs nothing.
  - Count 0 (a scan, H). 2b-3's +cen decides, and a hit triggers OQ7.
- **Cost (V16).** Per checked site:
  - one `Keys` query per view, only where an effect root has a candidate (Θ(terminator inputs + composed views × ⌈2F/64⌉));
  - Θ(effect roots + their candidates) bit tests and `Conflict` scans;
  - × the body's views only where the run stands for several views (at most the case bound, 64).

  A view without effect roots costs Θ(Loans) to list them. `Work` adds the row's words and the bit tests, and A4 counts its query's
  composed words.
- **Memory.** No per-head state, so there is no `MirDataflow.Prepare` term. A per-view Field-number array over the body's statics,
  pooled. Warm: 0 B.
- **+V16 "F5 effects" (B's shape).** One body with M implicit Loans on M mutable statics, live across N calls, each call to a
  function writing one of K statics. Runs: N = 32, 64, 128 at M = 4, K = 8; and M = 8, 16, 32 at N = 32. `MirBorrows.Work` must
  stay within 2× and 4×. VERIFICATION gains its row.
- **Corpus work (H).** Milestones 42 and 43 and their library are the only bodies with mutable-static Loans.

### 1.7 Prototype evidence

- **Round 18 (`prototype.md`).**
  - R1–R4 on 34 cases, 0 failures.
  - W6, R2's operand values and R3 are load-bearing: `R18NoW6`, `R18LiveOnly` and `R18NoR3` are unsound.
  - G93's classes are needed: `R18Merged` is over-strict on e8c, e12a and e13a.
  - The static ending changes only e16b's records.
- **The ensure (PD12 (ii) as written, R18).** e10a U, e10b error, e10c valid, e10d U, e10e U.
  - Case e10f (the reviewer's scratch run `rv2bd-runJ`, `-r18`, exit 0, round 18 failures 0):
    `var o = 1; var lx = o@ref; if k => (lx = State.x@ref); var ll = State.log@ref; let v = State.x; inspect(…)`, where x's
    initializer writes State.log. The sound verdict is an error.
    - R18 gives the located U at the ensure.
    - The designs' `R18EnsureClosed`/`R18EnsureKeyed`, and `R18EnsureExact` (P42's key identity), accept it: UNSOUND.
    - `R18EnsureByField` also accepts it (UNSOUND); `R18EnsureIgnored` gives the error.
    - So the closed-key exactness is withdrawn, and P42 alone does not make PD12 (ii) exact.
  - Round 18's record in `prototype.md` gains e10f with the 2b-D records commit (draft only).
- **Not run:**
  - per-view keys (the model has no case views; 2b-3's two-view test decides);
  - the e1 code (the model has no codes);
  - G136 (i) (no case stores a Loan-carrying value in a static);
  - V16's cost.

## 2. A4's keys and inputs (units 2b-1, 2b-7)

### 2.1 2b-1: the row, queried per view (G93; R4-M38's Open)

```text
ReadOnlySpan<ulong> Keys(MirBody body, long view, int block, out byte classes)  // the site's key row; bit 2f reads, 2f + 1 writes
int Field(BindingSymbol field)                                                   // A4's number of a mutable static Field, −1: none
internal const byte Unknown, Limit, Derived                                     // the classes, now internal
```

- `classes` is `found & (Unknown | Limit | Derived)`. A read bit beside its write bit is the write (R4-M38's rule). The row is
  `keyScratch`, valid until the next query.
- A borrowed mutable static always has a number, because its Ref is a static effect that `Local` recorded. One accessor reads a
  Field's bits, so the layout stays A4's.
- **Deleted:**
  - the `accessed` list, its conversion loop and its result buffer;
  - the Θ(keys reached) output: 2,098,176 keys over 4,826 sites at ring N = 2,048, F = 1,024. 2b-2's timing confirms the row: Run
    ≤ 0.48 ms in every shape, while the enumerated output dominates the queries (4.74 ms at that size).
- **Storage.** None new. G93's storage item closes on 2b-2's series: nodes × ⌈2F/64⌉ words, linear in nodes, quadratic where F grows
  with the program, 0 B warm.
- **Rejected:**
  - the list, because A5 would pay Θ(keys reached) per site;
  - a stored per-site record, rejected by R4-M38 (+36–60 % cold A4 allocation);
  - a union over a views mask, which collapses views (B-2).

### 2.2 2b-7: A4's inputs through their one helper (A)

- A `staticEnsure`'s initializer goes through `MirProgram.StaticInit` (`MirEffects.cs:155–159` deleted; AR-5 met).
- **G157 (9).** `TyCtxt.Published` names a row's requirement by `OwnRequirement` instead of `Requirements[0]`
  (`TyCtxt.Effects.cs:33`). A search finds no other reader. Only Equatable is named today, so a failing-before test needs a
  synthetic catalog row; the commit adds one if the catalog admits it, else states the latent cause and the search.
- **G126's A4 items (PLAN G126).**
  - `Local` starts a version on a new `MirProgram.Version`, not on "ordinal 0, view 0". This is explicit and stays inside A4, so
    there is no driver edit.
  - `Compose` records each (reader, node) dependency once per `Run`: a node's recorded reads are stamped when its evaluation
    starts. Its test counts dependencies on an alternating-readers shape and fails before.

## 3. Transferable (units 2x-P12, 2x-R14, 2b-4a, 2b-4, 2b-5; G124, G174)

### 3.1 Required meaning

- **SPEC 15.2.4.** Transferable visits OwnedOrigins' components (15.2.3), including concrete object payloads.
  - Refuting: `rc`, Weak of `rc` and raw pointers, even behind `ref` or `arc`; common Function Types; runtime-Contract and
    base-object views.
  - Exempt: only the Kimi collections' and standard iterators' own raw storage.
  - Abstract Types need premises.
- **SPEC 15.3.5.** Transferable and OwnedOrigins are computed once per declaration, in its Type parameters, as the least fixed point
  from "no exclusion", never by expanding instantiations.
- **SPEC 3.3.1, 8.7.3, 16.3.3.** An `obj`, `objref`, `objuniq`, `rc` or `arc` layer whose target is not Sealed, or is a runtime
  Contract, is a view.
- **SPEC 8.11.2.** An immutable static read is no environment effect only when its Type is proven Transferable.
- **SPEC 8.7.1, 22.1.** `Kimi.Transferable` is declared and compiler-intrinsic; `Self is Transferable` in a struct or enum is an
  error.
- **What the representation lacks and wrongly allows:**
  - no producer exists, and a written `T is Transferable` is `UnresolvedBinding_Kd` (a Language error for a valid form);
  - MIR approximates by `HoldsRaw`: g1–g4 and g10 are accepted (unsound) and g5 is rejected;
  - Owned's engine instantiates storage and stops growth with Unknown (G174).
- **Owner.** Binding's capability engine decides the verdict, a pure TyCtxt view publishes it, and A4 classifies the read.
- **Unnecessary afterwards:** `HoldsRaw` and `Raw`, `MirStatic.RawHolding` and `MirEffect.RawStaticRead`; and, with the shared
  summary, Owned's stored-Field walk and, after the census, the growth guard.

### 3.2 The per-declaration component summary (B's shape, the review's layer obligation; 2b-4a)

OwnedOrigins(D⟨A, o⟩) includes every instantiated Type and Origin argument, unused and inherited ones included (SPEC 15.2.3, 6.1.3).
A declaration's storage can name only its formals, its Origin slots, `static` and per-call Origins, which are excluded. So for a
struct or enum D:

```text
Owned(D⟨A,o⟩)        = ¬Excl_Owned(D)        ∧  ∧ (oⱼ = static)  ∧  ∧ Owned(Aᵢ)         ∧  ∧_{P ∈ Proj(D)} Owned(P[A])
Transferable(D⟨A⟩)   = ¬Excl_Transferable(D)                     ∧  ∧ Transferable(Aᵢ)  ∧  ∧_{g ∈ Obl(D)} g[A]
both Error where D's storage is invalid (Summary.Error; the Transferable plan §5.3: Error follows its producer's failure path)
```

`Summary(D) = (Excl per intrinsic, Obl, Error)` comes from one walk of D's storage components, in D's formals and never
instantiated. The components are bases, Fields, every Case payload, Tuple components, elements, concrete object payloads, Closure
captures and Function Item bound arguments; a callable signature alone contributes nothing.

- **Formals.** A Type formal contributes nothing: it is already required as a Type argument.
- **Projections (SPEC 15.2.4's "abstract Types need premises").** A projection P of a Type formal joins `Obl` as `Transferable(P)`.
  This set is `Proj(D)`, the only obligation kind Owned reads.
- **The layer obligation `Layer(S, X)` (R; SPEC 15.2.4, 3.3.1, 8.7.3).** It is recorded for any layer whose Semantics S is a
  Semantics formal (a pair binder), and for any object layer (`obj`, `objref`, `objuniq`, `rc`, Weak of `rc`, `arc`, or Weak of
  one) whose target X is a Type formal or a projection.
  - At use it is judged by 15.2.4's leaf rule after substitution [formals ↦ A]:
    - `rc`, Weak of `rc`, and `raw` outside an exempt identity refute;
    - `obj`, `objref`, `objuniq` and `arc` are Refuted when the substituted target's Sealed proof is Refuted or the target is a
      runtime Contract View, require the target when it is Proven, and are Unknown when it is Unknown. A Semantics formal is judged
      on its admitted set (SPEC 8.3.4): Refuted when every admitted Semantics refutes, Proven when none does, else Unknown.
    - `ref`, `uniq` and `owner` require the target.
  - So an abstract target without a Sealed premise gives Unknown, never Refuted. Under the container anchor (§3.5), a generic
    static storing `obj/T` with `T is ObjectPayload` is Unknown, which A4 maps to Limit: a `confined` reader gets a located U,
    never a Language error where an instantiation with a Sealed T is valid (the review's §7 P-2).
  - Where the verdict does not depend on the substitution, the walk discharges it at once: `rc` and Weak of `rc` refute whatever
    their target, so they set Excl.
  - This one rule replaces B's `ViewTarget` and `Layer` and covers A-1 (`obj/T`, `objref/T`, `objuniq/T` over an open base) and
    B-1 (`arc`, `s/U` under a formal binder).
- **Concrete leaves.** These set `Excl_Transferable`: `rc/X`, Weak of `rc/X` (Weak counts as storing its target, 15.3.5), a raw
  pointer outside the exempt identities, a common Function Type, and an object layer over a concrete target that is not Sealed or is
  a runtime Contract.
  - `Excl_Owned` comes from the engine's existing Owned leaf refutations (the verified raw-storage Types of `TryLeafCapability`).
  - `ref`, `uniq`, `owner`, and an object layer over a Sealed concrete target continue into the target.
- **Nominal components.** A nominal E⟨B⟩ contributes Excl(E) per intrinsic and walks each Bⱼ as a component in D's formals.
  - It also maps Obl(E) through [E's formals ↦ B]. An obligation whose subject becomes a formal or projection of D joins Obl(D). A
    concrete one is decided at once: the leaf rule, or the projection resolved by its conformance and the result walked.
- **The exempt identities (SPEC 15.2.4).** Array, Dictionary, Slice, UniqSlice, §22.2.2's standard iterators, and the library
  storage declarations that only the standard iterators instantiate ("their internal storage"; without them those iterators would
  be refuted, which is over-strict), keyed by `KimiDeclarationId` in one table.
  - Their own raw storage does not exclude; their Type arguments are still components.
  - `KimiLibraryShapeTest` asserts that their storage reaches no `rc`, Function or view leaf. That is the condition under which
    the exemption is the implementation's to keep.
- **Fixed point and finiteness.**
  - The fixed point is least over the declaration graph from (no, ∅, no), each declaration once per Binding pass, immutable after
    Bind, with pooled storage (warm 0 B).
  - Recursion through Type arguments (`Nest<(T, T)>`, G174's `Chain` with `obj/Chain<(T, T)>`) adds nothing, because Type
    arguments are components: `Nest` is (no, ∅), `Linked` (`Option<rc/Linked<T>>`) is excluded, and Owned of `Chain<i32>` is
    Proven.
  - `Obl` grows only when a declaration cycle maps a projection of a formal onto a longer projection of the same formal (D⟨T⟩
    storing D⟨T.(C).A⟩).
- **Projection families (A's SPEC 8.3.3 point; R).** Such growth needs `T.(C).A` to conform to C again, so C is a recursive
  Contract.
  - SPEC 8.3.3 says no concrete Type can be proven to conform to one, so a family meets only abstract bases. There is no
    concrete-growth branch.
  - A family is kept as one pattern (Tᵢ, p, q*): "Tᵢ.p.qᵏ for every k".
  - B's inductive rule, if 2x-P12 shows it sound: the pattern is Proven when the premise supply proves that Tᵢ.p conforms to a
    Contract that refines Transferable and supplies `Self.q` conforming to itself. `TyCtxt.RecursiveBase` (R4-M87) recognizes the
    recursive base. Otherwise it is Unknown, SPEC's "unproven" under Finality.
  - If 2x-P12 does not show it sound, the fallback is A's: a located `Unsupported_Kd` at the declaration, whose answers are Error.
  - No library or milestone declaration stores a projection recursively (both designs' scans, H).

### 3.3 Owned shares the summary (G174; OQ-F5-2 (b))

- Owned reads `Excl_Owned`, `Proj` and `Error`. `StoredCapability`'s stored-Field walk for Owned goes, so Owned of a non-regular
  recursive Type is SPEC 15.3.5's verdict.
- With no capability instantiating storage, the growth guard (`capabilityTypeDepths`, `CapabilityTypeDepth`, the `growing` loop) is
  deleted after a census of `RequestCapability`'s recursions shows no other grower.
  - Copy recurses through declarations and conditional premises on formals.
  - If the census finds a grower, the guard stays for it with a removal condition, reported by the census.
- `IntrinsicCapabilityTest.ExpandingGenericRecursionRemainsUnknownWithoutUnboundedExpansion` changes to Proven, citing SPEC 15.3.5
  and OQ-F5-2 as the reason.

### 3.4 Declaration and Binding sites (2x-R14, 2b-4; B)

**2x-R14 (bp, BU's files).** An intrinsic Contract table in `KimiLibraryKinds.cs` has one row per `IntrinsicKind` and these columns:

| Column | Rows today |
| --- | --- |
| Judged by the capability engine | Copy, Owned, Sealed, ObjectPayload, PrimitiveInteger |
| Declarable by a struct or enum | None, Copy, Owned; every other intrinsic keeps its current failure code |
| A Contract's `Self is K` is a requirement and premise | ObjectPayload, Copy, Owned |
| An ancestor conformance is decided by the judgment | Copy, Owned, Sealed |

- The table replaces five `is` chains: `Binding.Constraints.cs:283` and `:477`, `Binding.Contracts.cs` (`Register`),
  `Binding.ConditionalConformances.cs:209` and `Binding.ContractMatching.cs:225`. Those chains are one open family (AGENTS: a new
  form adds a row, A-3).
- The over-limit `Binding.Constraints.cs` and `Binding.Contracts.cs` stay net ≤ 0 tokens (PD21).

**2b-4** adds:

- `public contract Transferable` in `Core.kimi`; `IntrinsicKind.Transferable`, its `KimiDeclarationId`, the catalog entry and
  accessor.
- One table row: judged, not declarable, Self-requirement, ancestor by judgment. `Self is Transferable` in a struct or enum is
  `InvalidConstraint`, as Sealed's is (8.7.1), and `contract C: Transferable` is decided by the judgment, never by member matching.
- The engine's leaves (15.2.4):
  - Primitive (`string` included), Unit and Never are Proven; `rc`, raw and common Function are Refuted.
  - Slice and UniqSlice are structural.
  - `ComputeCapability`'s outer-Origin test stays Owned's, because Transferable requires no Owned.
  - Declarations go through §3.2's summary, and abstract Types through `SymbolicCapability`.
- `TyCtxt.Transferable(BindingSymbol staticField)` in a new `TyCtxt.Transferable.cs` (§3.5), memoized per pass, warm 0 B.
- The old effect checker's `ContainsRawPointer` (`Binding.EffectBounds.cs:478`) keeps its behavior: D5, deleted at R6-check.

### 3.5 Anchor and publication (B's anchor, A's channel; fixes A-2)

- **Anchor.** One verdict per static, judged at the static's declaration:
  `ProveConstraint(Transferable(field.Type), ConstraintScope(field's container))`.
  - A static's Type names only its container's formals (22.4.4), and under P5 MIR keeps no access Type arguments
    (`MirBuilder.Places.cs:159–162` stores `symbol.Type`).
  - A verdict Proven under the container's premises holds for every instantiation Binding admits, and a Refuted one for all.
    Unknown means the instantiation decides, which MIR cannot see before P42.
  - A non-generic group's static has a closed Type, so its verdict is never Unknown and is exactly SPEC 8.11.2's.
  - A container's pair binder is judged over all its admitted cases by the engine.
  - The reading body's premises, cases and inherited contexts never enter. So A-2's conditional member (`T is Transferable`)
    reading `Cache<rc/Item>.S.x` gets the container's Unknown, not its own Proven.
  - This departs from the Transferable plan's §5.3 (the reading body's premises; OQ-F5-5, item 4).
- **Channel.** No `MirStatic` field. Binding facts stay out of MIR storage, as `Copy` and `Sealed` do. A4 reads
  `types.Transferable(static.Symbol)` at a `StaticRead` of an immutable static (site = its index):

| Verdict | A4 adds |
| --- | --- |
| Proven | nothing (its initializer enters through the ensure's edge, 8.11.2) |
| Refuted | Environment |
| Unknown | Limit, which occurs only for generic statics: the confined witness gets the located `Unsupported_Kd` at its obligation, until P42 (round 18's g4 gives U, not SPEC's error, never an acceptance) |
| Error | Derived |

- Side effect, recorded: a generic static's Limit reaches A5's key classes. A call to such a reader while a mutable-static Loan is
  live and met by nothing gives a located U (§1.2's Limit), until P42 (rare: no immutable generic static in the library or the
  milestones).

### 3.6 MIR consumption (2b-5)

- `MirDescriptors`: an immutable read is `StaticRead` only. `MirEffect.RawStaticRead` and its branch are deleted.
- A4's fold of `RawStaticRead` is replaced by §3.5's table. `Environment`'s comment widens to SPEC's environment authority.
- `MirStatic(Symbol, Type, Mutable)`: `RawHolding` is deleted (`MirForms.cs:214`, the builder's argument in `MirBuilder.Places.cs`
  and `MirBuilder.Opaque.cs`). `MirDump` drops ` raw`; no golden prints it.
- `MirTypes.HoldsRaw` and its `Raw` walk are deleted, along with their approximation tests (`MirTypesTest.cs:102`, `:131`'s
  HoldsRaw half, `MirValidatorTest`'s `StaticRead, RawStaticRead` row). The verdict tests live at the producer. `HoldsLoans`,
  `MirWrites`, the G148/P13 guard and the destruction walk stay.

### 3.7 Default pipeline and G170

- **OQ-F5-3.** Declaring `Kimi.Transferable` makes `T is Transferable` premises, call constraints, `Self is Transferable`
  requirements and `contract C: Transferable` resolvable on both pipelines.
  - None adds a runtime form: workers are deferred, and the old emission monomorphizes constrained generics as before.
  - Under OQ-F5-2 (b), Owned's change makes non-regular recursive Types usable where Owned is required. Such a Type is already
    usable as a local, so the old emission's glue for it is pre-existing (H).
  - The evidence is the default-pipeline record census and one explicitly selected O0 fixture.
- **OQ-F5-4.** G170's spelling dependence reaches a written `T is Transferable` on a deep projection: a Proof-category rejection, as
  Copy's is today, recorded under G170. A static whose container premise needs it gets Unknown, which MIR maps to Limit; never a
  Language error.

## 4. Records and asks

- **DEC**, each entry written by its first implementing unit:

| Unit | Entry |
| --- | --- |
| 2b-1 | The key row and the per-view query, superseding R4-M38's list and `bool` |
| 2b-3 | ER-effects: R1 with the shallow whole-Field reach (15.6.2's reason); the sets and E13 (1)(b)'s reading; val(X) at every Drop; W6; the codes, as a clarification of R4-M72 (e1 by the A6 table's row 1, the planning session's confirmed reading); the three `MirFactKind` rows; Limit and Derived; R3 without a Loan, through the solve with the view skip extended; the ensure rule: PD12 (ii) as written, with e10f; per-view masks; G136 (i), a static's Loan naming E (replacing PD12 (iv)'s lemma); G136 (ii) per OQ-F5-1 |
| ruling | The planning session's ruling that `MirProblem.cs` has no split |
| 2x-R13 | The split (behavior-preserving) |
| 2x-R14 | The intrinsic Contract table as a canonical table |
| 2b-4a | The component summary shared by Owned and Transferable; the growth guard's census and deletion; Owned's acceptance per OQ-F5-3 |
| 2b-4 | The Transferable view, its intrinsic identity, the exempt table, the layer obligation, the anchor, the default-pipeline disposition |
| 2b-5 | A4's classification through the TyCtxt view; the deletions |
| 2b-7 | A4's one-helper inputs |

- **DIAGNOSTICS (2b-3).**
  - The err(effect) row loses "not reported on the MIR until unit 2b", names implicit static Loans, W6 and row 1's precedence, and
    lists the three facts.
  - A new R3 row: `ComparisonLoanConflict_Kd` at the assignment, anchored at it.
  - Two located `Unsupported_Kd` rows: A4's Limit at a site with a live mutable-static Loan; and an ensure whose proof would
    decide.
  - 2b-5 adds the confined-read Limit of a generic static.
- **ARCH:**
  - the Loan-stratum row (reads A4's keys; effect rows; a static's Loan names E);
  - the Effect-summaries row (the row and classes);
  - the Effect-facts row (`TyCtxt.Transferable`);
  - the Type-facts row (`HoldsRaw` gone);
  - the MIR-storage row (`MirStatic` without `RawHolding`);
  - a new canonical row for the component summaries (Binding, 2b-4a);
  - the intrinsic Contract table (2x-R14).
- **STATUS, PLAN, CODEMAP** through the records units (2b-8a, 2b-8b): G93, G124, G136's static items, G157 (9), G126's A4 items,
  G174, Transferable's verified support; PLAN §3.7: `.Loans.cs` (and `.Values.cs` if 2b-3 touches it) due before its next editor.
- **Draft record.** Round 18's record in `prototype.md` gains e10f (the reviewer's case, `rv2bd-runJ`) with the 2b-D records
  commit, draft only.
- **Asks.**
  - To R5: H3 maps `EffectRead`, `EffectWrite` and `EffectUnknown`; AR-2's review of 2b-5.
  - To BU: 2x-R14 and 2b-4a (BU implements, R4 reviews); a reviewer for 2b-4; 2b-6 with the unqualified in-group form.
- **SPEC gaps to report, not rewrite:**
  - a static Field as a Move Path, a sentence in the next SPEC session if OQ-F5-1 (a);
  - 15.6.4's "a live Loan anchored to a Field proves only that Field initialized" read under may-liveness (e10f; OQ-F5-6);
  - 15.3.5's fixed point for projection families, which SPEC states as terminating only for Type-argument recursion.
- **Unsupported producers to register for X-2 (PD28, OQ9):**
  - the effect Limit at a site;
  - the ensure's proof, every key, until OQ-F5-6's clarification and a must-carry fact; P42 alone does not decide it;
  - a generic immutable static's unproven Transferable (P42);
  - a projection family, if 2x-P12 leaves it U.

## 5. Units

Lanes follow the step 2 plan. Every code unit adds a regression that fails before it and passes after it. PD30 covers SPEC text not
yet integrated, and PD28 registers every remaining `Unsupported_Kd` producer.

- **G:A5-F5** = 2b-1, 2b-3, 2b-8a. Each unit is still verified and pushed. The group merges after 2b-8a, so G:A5-F9's merge never
  waits for Binding's Transferable lane.
- **Order:**
  - lane C, one linear stack: 2x-R13 on lane C's head (2x-R2), then 2b-1 on 2x-R13's branch, then 2b-3 on 2b-1's (the ruling
    before 2b-3);
  - 2b-7 runs beside lane C;
  - lane B: 2x-P12 → 2x-R14 → 2b-4a → 2b-4 → 2b-5 → 2b-8b;
  - 2b-8a after 2b-3 and 2b-6.
- **BU timing.** BU is paused for about four hours (2026-10-21), which delays 2x-R14, 2b-4a and 2b-6.

| ID | Change | Prerequisites | Verification (each new test failing before) | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- |
| 2x-R13 | Behavior-preserving split of the due `MirBorrows.Checks.cs` (B's cut): the location driver (`Check`, `Accessing`, `Accessed`, `Dropping`, `Destruction`, `Borrowing`) moves to `MirBorrows.Locations.cs`; the conflicts relation (`AccessKind`, `Meets`, `Conflict`, `Hits`, `Reserved`, `Ended`) stays | 2x-R2 (landed) | bp triple; `MirBorrowsTest`, `MirAnalysisTest` | move +150/−145 | R | no |
| (ruling) | `MirProblem.cs` (due: 1c-6, 1c-8, group 1c's review fixes) has no split: two records, 50 lines, one responsibility | — | Doc | — | — (planning session) | no |
| 2b-1 | §2.1: the row; `Field`; classes internal; `accessed` deleted; DEC | 2b-D (OQ-F5-5), 2x-R13 (stacked on its branch) | `MirEffectsTest`: a site per class (a Function value Unknown, a several-signature Callable input Limit, a Binding-rejected callee Derived), a plain read and write key through `Field`; a two-view caller whose callee views differ (each view's row); `KeysAreTheStaticFieldsASiteRuns`, `KeysKeepTheEvaluationsLinear` on the row; warm 0 B | +16/−24, t+55 | G:A5-F5 | no |
| 2b-3 | §1: `MirBorrows.Effects.cs` (contract first); `EffectRead`/`EffectWrite`; W6 in the mode test and `Code`; the effect row in the location driver; effect roots and the static sentinel in `.Loans.cs`/`Holders`; `Report` taking the effect fact (a fifth fact slot) in `.Reports.cs`; W's step for a Loan named E in `.Values.cs` if it needs a line; the view skip; `Ending` (OQ-F5-1); `MirBorrows` takes `MirEffects` (driver); three `MirFactKind` rows; DEC, DIAGNOSTICS, ARCH. PD21 at its start: `.Loans.cs` reaches its third change (1c-3, 1c-7, 2b-3), so its next editor splits first (for PLAN §3.7); `.Values.cs` (1c-7, G148) is counted if touched; `.Solve.cs`, `MirBorrows.cs`, `.Reports.cs`, `.Endings.cs` 0 | 2b-1 (stacked on its branch), (ruling), OQ-F5-1 | new `T/MirBorrowsTest.Effects.cs` on implicit static Loans: e1 (`CallReservationConflict_Kd` at `g()`), e2, e3, e4 (W6) and e5, e4c (two codes), e6 (a value keeping a receiver Loan, its drop writing the static; and reading it, valid), e8, e8b, e8c (U), e9, e9b, e11 and e11b, e12a–c, e13a–b, e14, e15, e15b, e10a (U), e10b (error), e10c (valid), e10d (U), e10e (U), e10f (U), e16a–c (one record under (a)), e17 (held), a `for` over a static calling a writer (error) and a reader (valid), widened effects (R4-M87), a default-tainted effect (U at the omitting call), a static store reaching E (G136 (i), if Binding leaves the call non-Opaque; checked first), facts per kind (PD31), a two-view caller asserting per-view masks; `MirAnalysisTest`; +V16 "F5 effects" (§1.6) with its VERIFICATION row; +warm with a static; +cen (Milestones 42, 43, the library); +led | +190/−10, t+340 | G:A5-F5 | drv |
| 2b-7 | §2.2: StaticInit; G157 (9); G126's version start and dependency dedup | 2x-R6, AR-5 (met) | `MirEffectsTest` (dependency count; explicit version start; the ensure edge unchanged); `TyCtxtTest` (a synthetic catalog row if admitted); warm 0 B | +16/−12, t+35 | R | adv (`TyCtxt.Effects.cs`) |
| 2x-P12 | B's prototype `S2/ComponentSummaryPrototype.cs`: the §3.2 summary over small declaration graphs, Owned and Transferable verdicts. Cases: the Transferable plan's §7.2 matrix, `Nest`, `Linked`, `Chain` (G174), exempt collections with `rc` elements, a projection family with and without the inductive premise, a two-declaration cycle through projections, an invalid Field. Added by the review: `obj/T`, `objref/T`, `objuniq/T` and `arc/T` at an open base, at a runtime Contract and at a Sealed struct; `arc/OpenBase`; `s/U` under a formal binder (A-1, B-1); A-2's anchor case (a conditional member of `Cache<T>` with `T is Transferable` reading `Cache<rc/Item>.S.x`); added by the review's §7 P-2: `obj/T` over an abstract T without a Sealed premise (Unknown) and with one (requires T), and a confined read of such a generic static (a located U, not a Language error); the concrete-growth case kept as a negative check only. A finding that changes §3.2's rules returns to the owner | 2b-D | Draft: every case gets its SPEC verdict; finiteness shown by iteration counts | ~+450 draft | R | no |
| 2x-R14 | Behavior-preserving: the intrinsic Contract table (§3.4) replaces the five `is` chains | — | bp triple (diagnostic and HIR snapshots); `IntrinsicCapabilityTest`, `ArchitectureRulesTest`; token counts net ≤ 0 in over-limit files | +30/−20 | R (BU) | BU |
| 2b-4a | §3.2–§3.3: `Binding.ComponentSummaries.cs` (contract first); Owned reads it; Owned's stored-Field walk and, by census, the growth guard deleted; DEC; ARCH row | 2x-P12, OQ-F5-2, OQ-F5-3 | `IntrinsicCapabilityTest`: Owned of `Chain<i32>` Proven (pinned Unknown changes, SPEC 15.3.5 stated), `Nest`, an Origin argument, an invalid Field (Error), the census result; warm 0 B; +cold; snap; +cen; OQ-F5-3's evidence | +130/−70, t+100 | D+R (BU implements, R4 reviews) | BU |
| 2b-4 | §3.4–§3.5: the declaration, the table row, the leaves and the layer obligation, `TyCtxt.Transferable(staticField)` with the container anchor; DEC; STATUS | 2b-4a, 2x-R14, OQ-F5-3, OQ-F5-4 | `IntrinsicCapabilityTest`/`TyCtxtTest`: the §7.2 matrix (each refuting leaf through `ref`/`arc` with its valid neighbour; exempt collections with `rc` elements; unused and inherited arguments; Case payloads; `Nest`/`Linked`; abstract `T` with and without premises; opt-in rejected; `contract C: Transferable` with an `rc` conformer) plus 2x-P12's added cases (`obj/T`, `objref/T`, `objuniq/T`, `arc/T`, `arc/OpenBase`, `s/U`, the A-2 anchor), and P-2's: `obj/T` over an abstract T without a Sealed premise (Unknown) and with one (requires T), and a generic static storing it, whose verdict is Unknown, so a confined read of it is a located U, not a Language error (the read itself asserted in 2b-5); `KimiLibraryShapeTest` (the exemption's condition); warm 0 B; +cold; +cen; the O0 fixture | +150, t+170 | D+R (BU reviewer) | adv (`KimiLibraryCatalog.cs`) |
| 2b-5 | §3.5–§3.6: A4 reads the view at an immutable `StaticRead`; `RawStaticRead`, `RawHolding`, `HoldsRaw`/`Raw` and the dump's ` raw` deleted | 2b-4, G148 (landed), AR-2, 2x-E2's hold on `MirBuilder.Places.cs` released | `MirEffectsTest` g1–g11: g1–g3 and g10 err (accepted before), g4 U until P42 (accepted before), g5 valid (rejected before), g6–g9 and g11 unchanged; a confined read of a generic static storing `obj/T` without a Sealed premise is a located U, not a Language error (P-2); the raw-static row stays failed; `MirValidatorTest`, `MirTypesTest`, `MirDumpTest`, `MirBuildTest`; goldens unchanged; sel; +cen; OQ7's trigger | +14/−70, t+60 | D+R, outside G (PD29) (+R5 reviewer) | sh (`MirDescriptors.cs`, `MirForms.cs`, `MirTypes.Holding.cs`, `MirDump.cs`), `MirBuilder.Places.cs`, `.Opaque.cs` |
| 2b-6 | BU, as planned: Binding's written static Borrows, qualified and the unqualified in-group form, become a located `Unsupported_Kd` | AB-4 | as planned; snap; STATUS row | +10/−5, t+20 | R | BU |
| 2b-8a | F5 records: `Landed += F5` with three (e) variants (`15.6.4-static-anchors`, `15.6.4-widened-effects`, `16.3.3-erased-destruction`), each expecting 2b-6's U and naming P42 and R5-M13 (b); `classification.tsv` (a5-old-effects split per PD7; MIR-only effect rejections, which the old path's G78 limit accepts, as (a) with SPEC 15.6.4); PLAN (G93, G136's static items, G157 (9), G126), CODEMAP, STATUS statics row, ARCH R4 row | 2b-3, 2b-6 merged | `VerifiedSpecExampleTest`; +led; Milestone 42 (73204fa40e52) without an A5 problem | doc +45, json ±15 | G:A5-F5 | no (merge dev before editing `classification.tsv`) |
| 2b-8b | G124 records: u-static and u-confined-static re-checked against 2b-5; PLAN G124 and G174; STATUS's Transferable support | 2b-5 merged | Doc + Comp; +led | doc +15 | — (DEC Q6 form 3) | no |

Volume (H): Mir about +260/−140; Binding about +310/−90; tests about +780; drafts about +450.

Ledger (step 2 plan §5.1, unchanged). 2b closes no blocking row (PD7). F5's two signatures (18 non-blocking (b) rows) pair or are
classified by 2b-3's +led and 2b-8a. u-static and u-confined-static are re-checked against 2b-3 and 2b-5. S1015 coverage by unit:

| SPEC section | Covered by |
| --- | --- |
| §8.11.2, §15.2.4 | 2b-4's matrix and 2b-5's g1–g11 |
| §15.6.4 | 2b-3's e-rows and its widened-effects row |
| §16.3.3 | e9, e9s (implicit spelling) and e15 |

## 6. Owner questions

The wording is the review's (§5, with §7.4's changes: OQ-F5-5 drops its former item 1, the closed-key ensure, and OQ-F5-6 is
added).

| ID | Question | Options | Recommendation | Blocks |
| --- | --- | --- | --- | --- |
| OQ-F5-1 | G136: does a whole write of a static end its Field's Loans (a static Field read as a Move Path: SPEC 15.6 "a Place root like a local", 15.1.3, 15.6.3, 22.4.4)? | (a) yes: one `Ending` line, e16b reports once, a clarifying SPEC sentence in the next SPEC session; (b) no: the lemma, with e16b's second record kept and recorded; (c) a SPEC session first, then (a) | (a): sound under P5 (round 18 (iii): no verdict changes) | 2b-3's ending part |
| OQ-F5-2 | G174: one per-declaration component summary for Owned and Transferable? | (a) Transferable-only, with G174 later under a removal condition; (b) one summary: 2x-P12, then BU's 2b-4a with R4 reviewing, deleting Owned's stored walk and, after a census, the growth guard | (b) | 2b-4a, 2b-4 |
| OQ-F5-3 | Default pipeline (D5/O3): declaring `Kimi.Transferable`, and under (b) Owned's verdict on non-regular recursion | (a) a SPEC-conformance fix on both pipelines (15.2.4, 22.1, 8.7.1, 15.3.5), with evidence: the default-pipeline record census and one explicitly selected O0 fixture; (b) a located `Unsupported_Kd` guard until R6-check; (c) keep `UnresolvedBinding_Kd` (a Language error for a valid form: against AGENTS) | (a), falling back to (b) for any form the evidence shows the old emission does not generate | 2b-4a, 2b-4 |
| OQ-F5-4 | Do 2b-4/2b-5 wait for G170's supply (PlanB C4)? | (a) proceed: a deep projection's spelling dependence rejects valid programs (Proof category), it is never unsound, and C4 repairs every consumer at once; (b) wait for C4; (c) a projection shape gate (rejected) | (a) | 2b-4, 2b-5 |
| OQ-F5-5 | Confirm the package's departures as one list (OD13's pattern) | (a) confirm all; (b) per item | (a) | 2b-1, 2b-3, 2b-4, 2b-5, 2b-8 |
| OQ-F5-6 | SPEC 15.6.4's "A live Loan anchored to a Field proves only that Field initialized" reads liveness as a may-property (15.6). A Loan carried on some paths "proves" initialization on paths where the Field was never accessed (e10f). | (a) report the gap to the next SPEC session, proposing "a Loan that every path to the access carries" (or "whose Borrow every path passes"); A5 keeps PD12 (ii)'s located U for every key meanwhile, registered at X-2 with that stage, and a later design adds the must-carry (dominating-Borrow) fact; (b) no SPEC change: the U stays at R4's exit, on OQ9's list; (c) the may reading (accept e10f): unsound, rejected | (a) | X-2's ensure item only (2b-3 ships the U) |

OQ-F5-5's list:

1. `Keys` returns the key row with a per-view query, replacing R4-M38's `(Field, Write)` list. The classes are already confirmed
   by OQ6 (a).
2. G136's "names no holder" is fixed by a representation (a static Loan names E) instead of PD12 (iv)'s lemma.
3. e1's code follows the A6 table's row-1 precedence: a clarification of R4-M72's effect row, recorded in DEC.
4. Transferable is anchored at the static's container premises (one verdict per static) and read by A4 through TyCtxt, with no
   `MirStatic` verdict. This departs from the Transferable plan's T2 and §5.3 (the reading body's premises). A generic static's
   unproven verdict is Limit, not Environment, until P42.
5. The 2b-8 split (2b-8a/2b-8b).
6. New entries recorded in DEC: `IntrinsicKind.Transferable` and its `KimiDeclarationId`, and three `MirFactKind` rows. None is an
   ARCH-closed vocabulary.
7. 2x-R14 in BU's files.

## 7. Still unverified

- **Per-view keys.** The prototype has no case views; 2b-1's and 2b-3's two-view tests decide.
- **The ensure.** e10f ran only in the reviewer's scratch copy (`rv2bd-runJ`). The must-carry (dominating-Borrow) fact that would
  make the proof exact is a later design, under OQ-F5-6 (a).
- **P-2's category fix.** The Unknown for `obj/T` over an abstract T without a Sealed premise, and the located U for its confined
  read, are argued, not run. 2x-P12 and 2b-4 decide them.
- **e1's code.** The reading (DIAGNOSTICS row 1, A3 E13 (6)) is confirmed by the planning session but has not run. 2b-3's test
  decides it.
- **G136 (i)'s regression.** The test case is reachable only if Binding leaves an Origin-invalid static store non-Opaque (H). It is
  checked first; if unreachable, the commit states the representation change without a failing-before verdict test, citing the
  parameter twin.
- **The summary.**
  - The layer obligation, the projection-family rule (inductive vs U) and A-1, A-2 and B-1 are argued from SPEC and code, not run.
    2x-P12 decides them.
  - The census of `RequestCapability`'s growers, before deleting the growth guard, has not run.
- **Default pipeline.** OQ-F5-3 (a)'s claim that the old emission handles the newly Owned non-regular recursive Types (H).
- **Census and cost (H) until 2b-3's and 2b-4's +cen, +led and +V16:**
  - R3's 0 library or milestone writes with an erased destruction;
  - no mutable-static Loan held across a keyed call;
  - no store through a static borrow;
  - no projection-recursive storage;
  - all sizes and costs.
- **Implicit Loans.** Binding's acceptance of each implicit static Loan form the tests use (only the writeback is seen on dev, in
  `tests/mir/statics.mir`). 2b-3's first failing tests confirm each.
