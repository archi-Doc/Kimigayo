# 2b-D design review: A5-F5 effects and static anchors, A4's keys, Transferable

Independent reviewer (wrote neither design), 2026-10-21. Code read at dev `1e4bf722` (2b-S5 merged; `r4-docs` worktree,
read-only) and `origin/dev` for Binding. The designs were written on `851e755b`. No repository edit, no build and no test suite
ran. Both prototype variants were rerun from empty scratch folders (`rv2bd-runA`, `rv2bd-runB`; `dotnet run proto.cs -- -r18`,
.NET SDK 11.0.100-rc.2). Both exit 0, and each output is byte-equal to its designer's `r18.txt`. "(H)" marks a reading that no
run checked. The interim notes are in `review-partial.md`.

## 1. The problem and the general rule

- **SPEC 15.6.4 (with 15.6.2, 15.6.7, 22.4.4).** An effect summary lists the static Fields a call reads, borrows, writes, replaces,
  destroys or initializes on first access. A call is checked as an access of each listed Field as a whole root, against the Loans
  carried by the values the operation uses and takes. A read meets a `uniq` Loan; any other access meets any Loan. Unknown effects
  and erased destruction (16.3.3) meet every mutable-static Loan, and immutable-static Loans are never affected. At its own call a
  reservation is live with the call's effects (15.6.7, W6). A live Loan on a Field proves that Field initialized.
- **SPEC 16.4 with 16.3.1.** A destruction holds an exclusive Loan on the storage it destroys, so the destruction's own static
  effects on that Field conflict with it (R3).
- **SPEC 15.2.4, 15.3.5, 8.11.2, 22.1.** Transferable is a structural judgment over OwnedOrigins' components. It is computed once
  per declaration as a least fixed point and never by expanding instantiations. An immutable static read is free of environment
  effects only when the static's Type is proven Transferable.

The general rule is that an effect key is an access of a whole static root, made by the Call or Drop that runs it, on the set
that operation takes. It goes through the one conflicts relation, `Report`, the hold and the taint. R3 is the same rule with the
destruction's own exclusive hold standing in for the Loan. Transferable is one per-declaration component summary in the
declaration's formals, shared with Owned.

Both designs follow this rule. Neither has a shorter design for ER-effects. Section 3 gives the one generalization I found, a
single layer obligation for Transferable.

## 2. Blocking findings

### Design A

| # | Finding | Evidence | Required fix |
| --- | --- | --- | --- |
| A-1 | **Unsound acceptance (Transferable summary).** No representation covers an object view over a Type formal: `obj/T`, `objref/T`, `objuniq/T` and `arc/T` with `T is ObjectPayload`. In A, "a formal adds nothing" and use is ¬Excluded ∧ Transferable(τ_i) ∧ patterns. So `D<OpenBase>`, with `D<T> { let x: obj/T }`, reduces to Transferable(OpenBase), which can be Proven. SPEC 15.2.4 refutes it: `obj/OpenBase` is a base-object view (3.3.1, 8.7.3: an open struct can be ObjectPayload). | design-A §4.2; SPEC 15.2.4, 8.7.3 ("Object Target"), 3.3.1, 16.3.3 | B's obligation, generalized as a layer obligation (§3, item T1) |
| A-2 | **Unsound acceptance (Transferable anchor).** A4 asks `Transferable(body.Key.Decl, cases.Type(static.Type))` and takes Proven before the generic-key (`Instantiates`) test. Under P5 the MIR static Type is the declaration's: `StaticRoot` stores `symbol.Type` (`MirBuilder.Places.cs:159-162`), which A also states in §4.4. A conditional member of `Cache<T>` with `T is Transferable` (SPEC 7.5) that reads `Cache<rc/Item>.S.x` (`let x: Option<T>`) is therefore judged on `Option<T>` under its own premise. The result is Proven, so the read is no effect, and a `confined` body reads `rc` storage. A's own §4.4 says that outside the container "the verdict may be wrong in either direction", but its rule order does not handle the Proven direction. Decision 11 and §4.4 also disagree on a Refuted generic static (Environment vs Limit). | design-A §0.11, §4.4 | B's anchor: one verdict per static under its container's premises (§3, item T3) |
| A-3 | **Symptom fix and structure limit.** 2b-4 adds Transferable as one more case to the open family of intrinsic `is` chains. They sit at `Binding.Constraints.cs:283` and `:477`, `Binding.ConditionalConformances.cs:209` and `Binding.ContractMatching.cs:225`; only `Register` is restructured. Two of those files are over 5,000 tokens and may not grow (PD21). | origin/dev sources; AGENTS ("one more case of an open family"; "under a canonical table a new form adds a row") | B's 2x-R14 table first |
| A-4 | **Missing census and prototype.** `tests/diagnostics/spec-cases.json` has three F5 cases: `15.6.4-static-anchors`, `15.6.4-widened-effects` (added with S5) and `16.3.3-erased-destruction`. A's 2b-8a records two, and A's 2b-3 has no widened-effects test, although R4-M87 has landed. The summary has no prototype unit, which the Transferable plan's T0 and PD3 require ("the prototype covers regular and non-regular recursion, changing Type arguments, recursive associated obligations, … the exemptions"). | spec-cases.json; Transferable plan §5.1, §6 | Three (e) records and the widened-effects test (B); B's 2x-P12 |

### Design B

| # | Finding | Evidence | Required fix |
| --- | --- | --- | --- |
| B-1 | **Unsound acceptance (Transferable summary).** `arc` is missing from both the object-view leaf rule and `ViewTarget`. B's list "`ref`, `uniq`, `arc`, a Sealed `obj` and `owner` continue into their target" accepts `arc/OpenBase` and `arc/Shape`, and `arc/T` at an open base. SPEC counts every object Semantics (`obj`, `rc`, `arc`, `objref`, `objuniq`) over a non-Sealed or runtime-Contract target as a base-object or runtime-Contract view (3.3.1, 8.7.3, 16.3.3: "an `obj`, `rc` or `arc` handle whose View Target is not Sealed"). `Layer(s)` checks only `rc` and `raw`, so `s/U` with a formal binder `s` and a concrete open `U` is missed in its object cases. | design-B §4.2; SPEC 15.2.4, 3.3.1, 8.1.1's table | One layer obligation for any layer whose Semantics or target is a formal or a projection, judged by the leaf rule after substitution (§3, item T1) |
| B-2 | **Contract violation (R4-M11: "views are never collapsed").** `Keys(body, views, …)` unions rows and classes over the views mask. A body that no view decides runs once with `views` = every view (`MirBorrows.Solve.cs:27-33`), and `Report` records `this.views` (`MirBorrows.Reports.cs:71`). An effect met only by one view's callee keys is therefore recorded under every Semantics case. The DIAGNOSTICS `case` fact then renders cases where no failure exists, and a per-view Limit U is lost under another view's error. | design-B §2.3 "Views", §3.1 | A's per-view query, recording each conflict with the mask of the views whose keys meet it |
| B-3 | **Wrong code (contract).** e1 (`State.count += g()`: an effect meets the writeback's pending reservation, which `g()` does not activate) is coded `ComparisonLoanConflict_Kd`. DIAGNOSTICS' A6 table, row 1, reads "A Loan found in its reservation holder, whatever the access → `CallReservationConflict_Kd`", and A3 E13 (6) keeps R4-M57's precedence for every access. `Code` already implements that rule (`MirBorrows.Witnesses.cs:14`), and the explicit twin is reported as `CallReservationConflict_Kd`. R4-M72's effect row replaced A3 §5's effect code (`CallEffectConflict_Kd`). Its claim that "none arises while … no static Loan exists" overlooked implicit static reservations, which exist on dev (`tests/mir/statics.mir:178`, `_6 = &uniq two-phase static0`). | DIAGNOSTICS MIR Loan table; design-A3 E13 (6), E15 (5) | Report through `Code` (A's reading); DEC clarifies R4-M72's row; listed for the owner (§5, OQ-F5-5 item 4) |

### Non-blocking findings (open issues or text corrections)

- **Reach (A deep, B shallow).** The two are equivalent in safe code. A static-rooted Loan has no Shared or Uniq dereference step:
  E3 forms no Loan past a shared dereference, and SPEC 15.2.3 allows no `uniq` static. `obj`/`rc`/`arc` payload steps are
  `StepClass.Own` (`MirBorrows.Places.cs:28-33`), which the shallow rule passes through. They differ only on raw-step Loans, which
  SPEC 15.6.2 does not compare. A's stated reason ("the callee may write through") is wrong for safe code. Take B's shallow reading
  with SPEC 15.6.2 as its reason.
- **R3 in a Loan-free view.** B runs R3 at the early return "with `held` false", which assumes that a Loan-free view is never held
  (R4-M73), and nothing shows that. A changes the view skip so that the solve runs where a static Drop exists, so the hold stays
  one rule. Take A's placement, or B's with a recorded proof.
- **Drop set.** A uses `TakesValue` (round 18's R2: val(X) at every Drop). B uses the Drop access's own set and argues that both
  agree wherever keys exist. Both are sound (H); A's needs no argued equality.
- **Effect class as a fact.** A records no class and asks U11/H3 for it later. AGENTS (Diagnostics) wants the check to record
  typed facts where it fails, and PD31 wants facts at the first producer. B's three `MirFactKind` rows, sited at the met Field's
  declaration, do that. `MirFactKind` is not an ARCH-closed vocabulary (COMPILER_ARCHITECTURE "Closed vocabularies"), so the
  entries need a DEC record, an ask to R5 for H3's mapping (B lists none), and the planning session's ruling on the due
  `MirProblem.cs` (B's).
- **G136 "names no holder".** Today `Content`/`Refs` of a value that carries only a direct static Loan give ∅ (`Named` < 0, direct,
  no Escaped: `MirBorrows.Values.cs:104-155`). A store through a static borrow therefore goes nowhere: a silent drop. B names E:
  SPEC 15.6 treats static storage as storage the body does not own, the same as a parameter's referent, and check sets do not
  change because σ(E) is in each one. A's lemma reads SPEC 15.6.1 ("no Loan conflict of its own") as excluding the Loan conflict a
  store into E causes. The compiler already reports that conflict for the parameter twin (`p@follow = x@ref; x = 1`), so A's
  reading would treat statics and parameters differently. Take B's representation. Its regression, an Origin-invalid
  `State.list.append(x@ref); x = 2`, is reachable only if Binding leaves that call non-Opaque (H; check first). Both designs
  depart from PD12 (iv)'s "lemma that a prototype case checks": A runs no case, and B replaces the lemma. List it (§5).
- **Projection families (Transferable).** SPEC 8.3.3 says no concrete Type can be proven to conform to a recursive Contract. A
  growing projection chain therefore meets only abstract bases (A's argument), and B's concrete-base branch (`Wrap`-growth,
  induction or U) is vacuous. B's inductive-premise rule for abstract bases is the SPEC verdict if 2x-P12 shows it sound; A's U is
  the fallback. No library or milestone declaration stores a projection recursively (both scans, H).
- **Error verdict.** A's summary has no Error bit for invalid storage; B's `Summary.Error` follows the Transferable plan §5.3
  ("Error … follows its producer failure path").
- **G126.** PLAN G126 gives A4's version reset to 2b-7, and A keeps it there (inside A4, a new `MirProgram.Version`). B moves it to
  2x-R5's driver change (drv hold). Take A's.
- **G157 (9).** Only Equatable is named today, so a failing-before test needs a synthetic catalog row. A says so; B claims a test
  without saying how it fails before.
- **2x-R13 split.** In A, `AccessKind` moves into `.Accesses.cs`. In B, `AccessKind` stays with the conflicts relation and the
  location driver moves into `.Locations.cs`. DEC names `AccessKind`'s entries as the conflicts relation's rows (R4-M68, R4-M70),
  so B's split keeps one owner. B's third `AccessKind` entry, `Effect`, a site row that is not a relation row, is unnecessary; A's
  two rows suffice.
- **V16.** B's shape varies N, M and K. A's varies only N, and the per-site term grows with M as well. Take B's.

## 3. Item-by-item comparison and recommendation

| Item | A | B | Recommendation |
| --- | --- | --- | --- |
| E1 err(effect) placement | Effect access in `Check`, `MirBorrows.Effects.cs`, two `AccessKind` rows, through `Conflict` and `Report` | The same, plus an `Effect` site row | **A's two rows**, in the same new file; B's file-contract text |
| E2 Sets | Call: the last operand's set (E13 (1)(b), no holder left out); Drop: `TakesValue` | The same; Drop: its own access's set | **A** (round 18's R2 letter); DEC records E13 (1)(b)'s reading (both) |
| E3 Reach | Deep | Shallow whole-Field | **B**, with SPEC 15.6.2 as the reason (equivalent in safe code) |
| E4 Classes | Unknown = write on every mutable-static root; Limit → U where a candidate is met by nothing; Derived alone nothing | The same | Equal (round 18's R1) |
| E5 W6 | `umode` for an activated reservation; `Code` treats it as not Reserved at its own call | The mode test reads `reserving` | **A** (the same mode rule, plus the code rule E6 needs) |
| E6 Codes | e1 `CallReservationConflict_Kd` (row 1); otherwise `ComparisonLoanConflict_Kd` | `ComparisonLoanConflict_Kd` everywhere | **A** (B-3) |
| E7 Facts | Anchor, Use/Store, subject; class deferred | The same, plus EffectRead / EffectWrite / EffectUnknown sited at the Field | **B** (typed fact where it fails; PD31) |
| E8 R3 | No Loan; Drop's scope only; via `Check` with the solve | No Loan; Drop's scope only; a pass after the replay | Rule equal; **A's placement** (the hold stays uniform) |
| E9 staticEnsure | Closed key exact, generic U (`R18EnsureClosed`) | The same (`R18EnsureKeyed`) | Equal; both reruns byte-equal: e10a valid, e10b error, e10c valid, e10d U, e10e U |
| E10 Views | Per-view query, per-view masks | Union over the mask | **A** (B-2) |
| E11 G136 names no holder | Lemma | Static Loan names E | **B** (non-blocking finding above); listed departure from PD12 (iv) |
| E12 G136 ending | Q-A1 (a) | OQ-B1 (a) | (a); one owner question |
| K1 Keys interface | Row + `Field(symbol)`, `long view` | Row + `KeyOf`, `ulong views` | **The row (both)**, with **A's per-view signature**. 2b-2's timing confirms the row: Run ≤ 0.48 ms in every shape, while the enumerated output dominates queries (ring F = 1,024: 2.1 M keys, about 2 ns each, 4.74 ms) |
| K2 Key storage | None new | None new | None; G93's storage item closes on 2b-2's allocation and timing series |
| K3 2b-7 | StaticInit, G157 (9), both G126 A4 items | StaticInit, G157 (9), dedup only (reset to 2x-R5) | **A** |
| T1 Summary | (Excluded, Patterns), cyclic pattern U | (Excl, Obl{Transferable(P), ViewTarget, Layer}, Error) | **B's shape**, with ViewTarget and Layer replaced by one layer obligation `Layer(S, X)` for any layer whose Semantics or target is a formal or projection, judged by the leaf rule after substitution (all of `obj`, `rc`, `arc`, `objref`, `objuniq`: A-1, B-1). Use A's 8.3.3 argument (abstract-only families). B's inductive rule if 2x-P12 passes, else A's U |
| T2 Owned (G174) | (b) shared summary, BU | (b) shared summary, 2b-4a, guard deleted after a census | **(b)**, B's unit shape (prototype first, census of `RequestCapability`'s growers) |
| T3 Anchor | Reading body's premises, per view | The static's container premises, one verdict per static | **B** (A-2) |
| T4 Publication | A4 queries a TyCtxt view at the read; no `MirStatic` field | Verdict stored in `MirStatic` | **A's channel with B's anchor**: `TyCtxt.Transferable(BindingSymbol staticField)`, memoized per pass, read by A4 at an immutable `StaticRead` (Proven: nothing; Refuted: Environment; Unknown: Limit, only generic statics, until P42; Error: Derived). Binding facts stay out of MIR storage, as `Copy` and `Sealed` do; `MirStatic` loses `RawHolding`; the dump drops ` raw` |
| T5 Declaration sites | Plan §5.2 and the exempt table; `Register` restructured | 2x-R14 intrinsic table, then one row; exempt table with a `KimiLibraryShapeTest` assertion | **B** (A-3) |
| T6 G170, default pipeline | Proceed; SPEC-conformance fix | Proceed; SPEC-conformance fix with evidence, fallback guard | **B's wording** (§5) |
| U1 Units | 2x-R13 (accesses), 2b-1, 2b-3, 2b-7, G174-S, 2b-4, 2b-5, 2b-8a/2b-8b | 2x-P12, 2x-R13 (locations), MirProblem ruling, 2x-R14, 2b-1, 2b-3, 2b-4a, 2b-4, 2b-5, 2b-6, 2b-7, 2b-8 | **Combined** (§4) |

## 4. Recommended package (a combination)

Take A for ER-effects' code path and views, and for the 2b-8 split and 2b-7. Take B for facts, reach, G136 (i), the Transferable
shape, anchor, prototype and table, with A's TyCtxt publication channel. Corrections B-1 to B-3 and A-1 to A-4 are folded in.

| ID | Change | Prereq | Review | Notes |
| --- | --- | --- | --- | --- |
| 2x-R13 | bp split of the due `MirBorrows.Checks.cs` (B's cut: location driver to `.Locations.cs`; the conflicts relation and `AccessKind` stay) | 2x-R2 | R | bp triple |
| (ruling) | `MirProblem.cs` (due): no split exists, recorded by the planning session (B) | — | — | needed because 2b-3 adds fact kinds |
| 2b-1 | `Keys(body, long view, block, out byte classes)` returns the key row; `Field(symbol)`; classes internal; the `accessed` list deleted (A's signature) | 2b-D | G:A5-F5 | B's per-class tests plus a two-view caller |
| 2b-3 | ER-effects: A's rules E1–E10 above, B's facts (E7) and shallow reach (E3), B's G136 (i), the (a) ending, W6 and e1's code through `Code`. Tests: both designs' lists, plus widened effects (R4-M87 landed), e1 as `CallReservationConflict_Kd`, a two-view caller asserting per-view masks, a static store reaching E, and B's V16 shape | 2x-R13, 2b-1, OQ-F5-1 | G:A5-F5 | drv |
| 2b-7 | A's row (StaticInit, G157 (9), both G126 A4 items) | — | R | adv |
| 2x-P12 | B's prototype, adding: `obj/T`, `objref/T`, `objuniq/T`, `arc/T` at an open base, a runtime Contract and a Sealed struct; `arc/OpenBase`; `s/U` under a formal binder; the anchor case of A-2 (a conditional member reading another instantiation); the concrete-growth case kept as a negative check only | 2b-D | R | Draft |
| 2x-R14 | B's intrinsic Contract table (bp, BU's files) | — | R (BU) | net ≤ 0 tokens in the over-limit files |
| 2b-4a | B's component summary (§3, item T1) for Owned; growth guard deleted after the census | 2x-P12, OQ-F5-2, OQ-F5-3 | D+R (BU implements, R4 reviews) | BU |
| 2b-4 | Declaration, table row, leaves, `TyCtxt.Transferable(staticField)` with B's anchor | 2b-4a, 2x-R14, OQ-F5-3, OQ-F5-4 | D+R | B's matrix plus the T1 cases |
| 2b-5 | A4 reads the view at the read (§3, item T4); deletes `RawStaticRead`, `RawHolding`, `HoldsRaw`/`Raw` and the dump's ` raw` | 2b-4, AR-2 | D+R, outside G | sh, W1 (argument deletion) |
| 2b-6 | BU, as planned (the unqualified in-group form included) | AB-4 | R | BU |
| 2b-8a | F5 records with **three** (e) variants, the a5-old-effects split, PLAN, CODEMAP, STATUS, ARCH (A's split) | 2b-3, 2b-6 | G:A5-F5 | F5's group merge does not wait for lane B |
| 2b-8b | G124 records after 2b-5 | 2b-5 | — (DEC Q6 form 3) | |

Unsupported producers to register for X-2: the effect Limit at a site; the generic ensure's proof; a generic immutable static's
unproven Transferable; a projection family, if 2x-P12 leaves it U.

## 5. Owner questions (consolidated)

| ID | Question | Options | Recommendation | Blocks |
| --- | --- | --- | --- | --- |
| OQ-F5-1 | G136: does a whole write of a static end its Field's Loans (a static Field read as a Move Path: SPEC 15.6 "a Place root like a local", 15.1.3, 15.6.3, 22.4.4)? | (a) yes: one `Ending` line, e16b reports once, a clarifying SPEC sentence in the next SPEC session; (b) no: the lemma, with e16b's second record kept and recorded; (c) a SPEC session first, then (a) | (a): sound under P5 (round 18 (iii): no verdict changes) | 2b-3's ending part |
| OQ-F5-2 | G174: one per-declaration component summary for Owned and Transferable? | (a) Transferable-only, with G174 later under a removal condition; (b) one summary: 2x-P12, then BU's 2b-4a with R4 reviewing, deleting Owned's stored walk and, after a census, the growth guard | (b) | 2b-4a, 2b-4 |
| OQ-F5-3 | Default pipeline (D5/O3): declaring `Kimi.Transferable`, and under (b) Owned's verdict on non-regular recursion | (a) a SPEC-conformance fix on both pipelines (15.2.4, 22.1, 8.7.1, 15.3.5), with evidence: the default-pipeline record census and one explicitly selected O0 fixture; (b) a located `Unsupported_Kd` guard until R6-check; (c) keep `UnresolvedBinding_Kd` (a Language error for a valid form: against AGENTS) | (a), falling back to (b) for any form the evidence shows the old emission does not generate | 2b-4a, 2b-4 |
| OQ-F5-4 | Do 2b-4/2b-5 wait for G170's supply (PlanB C4)? | (a) proceed: a deep projection's spelling dependence rejects valid programs (Proof category), it is never unsound, and C4 repairs every consumer at once; (b) wait for C4; (c) a projection shape gate (rejected) | (a) | 2b-4, 2b-5 |
| OQ-F5-5 | Confirm the package's departures as one list (OD13's pattern) | (a) confirm all; (b) per item | (a) | 2b-1, 2b-3, 2b-4, 2b-5, 2b-8 |

OQ-F5-5's list:

1. staticEnsure is exact for a closed key and a located U for a generic one, refining PD12 (ii).
2. `Keys` returns the key row with a per-view query, replacing R4-M38's `(Field, Write)` list. The classes are already confirmed
   by OQ6 (a).
3. G136's "names no holder" is fixed by a representation (a static Loan names E) instead of PD12 (iv)'s lemma.
4. e1's code follows the A6 table's row-1 precedence: a clarification of R4-M72's effect row, recorded in DEC.
5. Transferable is anchored at the static's container premises (one verdict per static) and read by A4 through TyCtxt, with no
   `MirStatic` verdict. This departs from the Transferable plan's T2 and §5.3 (the reading body's premises). A generic static's
   unproven verdict is Limit, not Environment, until P42.
6. The 2b-8 split (2b-8a/2b-8b).
7. New entries recorded in DEC: `IntrinsicKind.Transferable` and its `KimiDeclarationId`, and three `MirFactKind` rows. None is an
   ARCH-closed vocabulary.
8. 2x-R14 in BU's files.

## 6. What stays unverified

- **Per-view keys.** The prototype has no case views; 2b-3's two-view test decides.
- **e1's code.** The reading comes from DIAGNOSTICS row 1 and A3 E13 (6); the planning session should confirm it before DEC.
- **G136 (i)'s regression.** It is reachable only if Binding leaves an Origin-invalid static store non-Opaque (H).
- **Holds in Loan-free views.** Whether a Loan-free view can be held (relevant only to B's R3 placement).
- **The summary.** The summary, the layer obligation and the projection-family rule wait for 2x-P12. The census for deleting the
  growth guard has not run.
- **Census and sizes.** The census claims (R3: 0 library or milestone writes with erased destruction; no mutable-static Loan held
  across a keyed call; no projection-recursive storage) and all sizes and costs are (H) until 2b-3's and 2b-4's +cen and +V16.
- **A-1, A-2 and B-1.** These are argued from SPEC and code, not run; 2x-P12's added cases decide them.

## 7. Confirmation of the consolidated package (`package.md`, 2026-10-21)

**Verdict: not yet ACCEPT.** The package closes all seven findings of §2. It still carries two blocking defects: P-1, which
my review missed and both designs share, and P-2, introduced by the package's own layer-obligation wording. Both are text
fixes.

### 7.1 Closure of §2's findings

| Finding | Closed by | Status |
| --- | --- | --- |
| A-1 | §3.2's layer obligation (`obj/T`, `objref/T`, `objuniq/T`, `arc/T`) | Closed once P-2 is fixed |
| A-2 | §3.5: container anchor, one verdict per static, no reading-body premises | Closed; its claim "a Refuted one holds for all" needs P-2 |
| A-3 | 2x-R14's table, before 2b-4 | Closed |
| A-4 | Three (e) records in 2b-8a, a widened-effects test in 2b-3, and 2x-P12 with the added cases | Closed |
| B-1 | `arc` in the object-layer leaf and obligation; `s/U` through `Layer(S, X)` | Closed once P-2 is fixed |
| B-2 | Per-view `Keys(…, long view, …)` and per-view masks (§1.2, §2.1) | Closed |
| B-3 | Codes through `Code`; e1 is `CallReservationConflict_Kd` (§1.4) | Closed |

### 7.2 Remaining blocking items

**P-1. Unsound acceptance: the closed-key staticEnsure proof under may-liveness** (§1.2 R4; OQ-F5-5 item 1). This is my
review's miss: §3's E9 row called the ensure rule "equal" in both designs.

- **Cause.** ES(ℓ) is a may-set (SPEC 15.6: liveness is a may-analysis). A Loan on e carried on one path into the ensure proves
  nothing on another path where e was never accessed, and there the initializer runs.
- **Case e10f.** Added to a scratch copy of design A's prototype (`rv2bd-runJ`, `-r18`, exit 0, Round 18 failures 0):
  `var o = 1; var lx = o@ref; if k => (lx = State.x@ref); var ll = State.log@ref; let v = State.x; inspect(lx, ll)`, where x's
  initializer writes State.log. The sound verdict is an error.
- **Results.**
  - R18 (PD12 (ii) as written) gives the located U.
  - `R18EnsureClosed` (the package's rule) and `R18EnsureExact` (P42's key identity) both accept: **UNSOUND**.
  - So P42 alone does not make PD12 (ii) exact either.
- **Fix:**
  - **§1.2 R4.** Replace both sub-bullets with: "each conflict R1 would report at ℓ is instead the located `Unsupported_Kd` at ℓ,
    for every key (PD12 (ii) as written; round 18's R18): ES(ℓ) is a may-set, so a Loan on e carried on one path proves nothing
    on a path where e was never accessed (e10f)". Keep the e10b sentence.
  - **§1.7.** e10a stays U. Add e10f with this run's results.
  - **2b-3's tests.** e10a U; e10f U.
  - **DIAGNOSTICS row.** "an ensure whose proof would decide", without "of a several-key Field".
  - **§4 DEC (2b-3).** "the ensure rule: PD12 (ii) as written, with e10f".
  - **X-2.** "the ensure's proof, every key, until OQ-F5-6's clarification and a must-carry fact; P42 alone does not decide it".
  - **OQ-F5-5.** Drop item 1.
  - **Owner question.** Add OQ-F5-6 (§7.4).
  - **Draft record.** Record e10f in round 18 of `prototype.md` (draft-only).

**P-2. Wrong verdict category: the layer obligation is two-valued** (§3.2: "refute when the substituted target is not proven
Sealed").

- **Cause.** An abstract target without a Sealed premise has an Unknown Sealed proof, but this wording gives it Refuted.
- **Consequence under the container anchor (§3.5).** Take a generic static whose Type stores `obj/T` with `T is ObjectPayload`:
  - it gets Refuted, so A4 adds Environment;
  - a `confined` reader then gets a Language error at instantiations where T is Sealed, which SPEC accepts, and MIR cannot see
    the instantiation before P42 (a conservative Language error, against AGENTS Diagnostics);
  - §3.5's "a Refuted one [holds] for all" is false.
- **Fix.** Replace the object bullet with: "`obj`, `objref`, `objuniq` and `arc` are Refuted when the substituted target's Sealed
  proof is Refuted or the target is a runtime Contract View, require the target when it is Proven, and are Unknown when it is
  Unknown. A Semantics formal is judged on its admitted set (SPEC 8.3.4): Refuted when every admitted Semantics refutes, Proven
  when none does, else Unknown."
- **New cases** for 2x-P12 and 2b-4:
  - `obj/T` over an abstract T, without a Sealed premise (Unknown) and with one (requires T);
  - a confined read of such a generic static (located U, not a Language error).

### 7.3 Units, gates and holds (consistent; non-blocking notes)

- **PD1 and the groups.** Lane C's head is 2x-R2 (landed, with 2x-R1); 2x-R8 has landed; W1 is released (PLAN §3), so 2b-5's
  hold lines replace the plan's AR-1 slot. G:A5-F5 = 2b-1, 2b-3, 2b-8a. 2b-7 (adv) and 2b-5 sit outside the group (PD29);
  2b-3's driver edit stays in it. All consistent.
- **One base for 2b-3.** State a single stack: 2x-R13 on lane C's head, then 2b-1 on 2x-R13's branch (or the reverse), then 2b-3
  on top. As written, 2b-3 has two bases.
- **PD21 counts at 2b-3's start.**
  - `.Loans.cs`: 1c-3 and 1c-7, so 2b-3 is the third change and its next editor splits first. Record this in PLAN §3.7.
  - `.Solve.cs`, `MirBorrows.cs`: 0 since 2x-R1. `.Reports.cs`: 0 since 2x-R2. `.Endings.cs`: 0.
  - `.Values.cs`: 1c-7 and G148. 2b-3 touches it only if W's E step needs a line, and it counts it then.
- **2b-3's file list** omits `.Reports.cs`, where `Report` takes the effect fact (a fifth fact slot).
- **Exempt table.** Also list the library storage declarations that only the standard iterators instantiate. SPEC 15.2.4 and the
  Transferable plan exempt "their internal storage"; otherwise those iterators would be refuted, which is over-strict.

### 7.4 Owner questions

OQ-F5-1 to OQ-F5-4 are complete for what the package decides. Change OQ-F5-5 by dropping item 1 (P-1), and add OQ-F5-6:

| ID | Question | Options | Recommendation | Blocks |
| --- | --- | --- | --- | --- |
| OQ-F5-6 | SPEC 15.6.4's "A live Loan anchored to a Field proves only that Field initialized" reads liveness as a may-property (15.6). A Loan carried on some paths "proves" initialization on paths where the Field was never accessed (e10f). | (a) report the gap to the next SPEC session, proposing "a Loan that every path to the access carries" (or "whose Borrow every path passes"); A5 keeps PD12 (ii)'s located U for every key meanwhile, registered at X-2 with that stage, and a later design adds the must-carry (dominating-Borrow) fact; (b) no SPEC change: the U stays at R4's exit, on OQ9's list; (c) the may reading (accept e10f): unsound, rejected | (a) | X-2's ensure item only (2b-3 ships the U) |

When P-1 and P-2 are applied as written above, I would ACCEPT the package without another round.
