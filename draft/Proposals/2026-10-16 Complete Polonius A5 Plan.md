# Complete Polonius-Style A5: Plan for R4's Remaining Scope

Date: 2026-10-16

Status: Proposal (plan only). The owner chose the direction on 2026-10-16 and refined it the same day: a simple, fast
implementation before exact precision (plan §1). On 2026-10-17 the owner decided OD8–OD11 (OD8 and OD9 not adopted,
OD10 adopted, OD11 confirmed) and approved OD13's first list; plan §10 records the outcomes. This document changes no
SPEC, compiler or test.

Base: `b2edea8b` (`dev`).

Conventions:

- "§n" is a SPEC section; "plan §n" is a section of this document.
- Short names:
  - PLAN10: [2026-10-10 Compiler Size Reduction and Single MIR Plan](2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md)
  - OBS13: [2026-10-13 R4 Completion Plan and Budget](../Obsolete/2026-10-13%20R4%20Completion%20Plan%20and%20Budget.md) (withdrawn)
  - DEC: [DECISIONS](../../docs/dev/DECISIONS.md); ARCH: [COMPILER_ARCHITECTURE](../../docs/dev/COMPILER_ARCHITECTURE.md); PLAN: [PLAN](../../docs/dev/PLAN.md)
- C1–C8, M39, U8–U12 and E2–E9 are OBS13's names. PLAN10 Appendix B's C1–C6 (cited by DEC O3) are not used here.
- "D5" is PLAN10's migration freeze. The owner decisions of this plan are OD1–OD13.
- S1–S5 are this plan's SPEC change items (plan §5.2). S1015 is the 2026-10-15 SPEC rewrite.
- R6-gen and R6-check are the two stages of R6 (plan §9.1).
- (H) marks an unverified hypothesis. Code counts are grep counts at the base commit.

## 1. Summary

The owner's direction (2026-10-16):

- A5 implements SPEC §15 borrow checking in **Polonius style**: facts plus a fixed set of rules.
- **Simple, then fast, then precise.** The owner accepts some loss of precision for a simple, fast implementation.
  - **Soundness is never traded.** No program that breaks memory safety is accepted.
  - **A precision loss becomes a SPEC simplification.** Where a simpler rule rejects a program the SPEC accepts, the
    rule is written into the A5 rules and into the SPEC, so SPEC and implementation agree (plan §5.3, OD12, OD13). A
    conservative Language error against the SPEC is never the answer (AGENTS "Diagnostics").
- The result is **complete** against the rules as simplified: every A5 rule is implemented before R6-check, with no
  whitelist and no family left `Unsupported_Kd`.
- A5's inputs come **first**: Binding publishes every fact A5 reads before A5 uses it.

This plan turns that direction into steps:

1. **Write the rules down.** Express each rule A5 decides as finite facts and rules, choosing the simplest sound
   form (plan §4). A rule that cannot be written that way, or whose simple form rejects valid programs, is resolved
   by a SPEC change, decided before its family is implemented (plan §5).
2. **Publish facts, then rules.** Each family is a Binding fact unit (if it needs one) followed by an A5 rule unit
   (plan §6, §7).
3. **Verify against the rules,** not against the old path. Replace P9 with metrics that move (plan §8).
4. **Shorten the schedule.** R5 starts in parallel, SPEC changes go to MIR only, and the review process is lighter
   where it is safe (plan §9).
5. **Exit.** R4 exits when every family is complete. R6 switches in two stages: R6-gen (code generation) after R5,
   then R6-check (acceptance) after R4 and R6-gen (plan §9.1).

## 2. Background

### 2.1 Why R4 stalled

Sources: DEC R4-M33, OBS13 and the commit messages. The causes below are our reading of that record (H).

- **Timeline.**
  - R4 started at 11:00 on 10-12. A1–A4 and A5-1 landed by 00:40 on 10-13, about 14 hours later.
  - On 10-13 there were 9 R4 commits: 8 plans or records and one code change of +20 Binding tokens (C1a).
  - No R4 commit followed until 10-16, when the owner discarded the work after A4: A5-1 with U7a/U7a2/U7k/U7c, C1a,
    the MIR probe and OBS13 (`0d989e68`, DEC R4-M33).
- **Causes.**
  - **The metric could not move.** A5-1 reports no problems, so the unclassified count (P9) stayed at 143, and O5 was
    bound to fire.
  - **Parity with the old path.** The R4 exit required no unclassified and no (b) rows. Every difference had to be
    classified with a SPEC clause against an old path that, for example, judges Origins during selection (PLAN10
    Appendix B T1–T4).
  - **Missing inputs.** U8 (Loan conflicts) waited for complete reference provenance and source-publication contracts
    (OBS13 §1, `ddcab448`): the common contracts C1–C8, M39 and E4/E5. Of these, C1, C2, C5, C8, M39 and E4/E5 add
    Binding facts; C3, C4, C6 and C7 are MIR-side analysis work. They were designed under D5 and the size caps; DEC Q5
    later removed the caps.
  - **A bespoke solver.** The first U7 was rejected for cubic holder rescans (5.8 s at N = 1,024) and a `Release` that
    ended Loans other holders kept (`75dc5100`).
  - **Open algorithms.** OBS13's wave 0 left the C1, C3, C5, C8 and E5 algorithms open. C3 lacked a finite sparse
    representation and C5 a finite result/effect domain.

### 2.2 What this plan changes

| Problem | Response |
| --- | --- |
| A bespoke solver | A known structure: facts and rules (plan §4) |
| Rules without a finite algorithm | Audit first; resolve with a SPEC change if needed (plan §5) |
| Missing Binding inputs | Publish facts before the rules that read them (plan §7) |
| Parity with the old path | Verify against the written rules; block only on soundness (plan §8) |
| A metric that cannot move | Families completed, rules expressed, rule tests passing (plan §8) |
| Heavy process for every unit | Lighter review where the risk is shadow-only (plan §9.3) |
| Owner direction: simple and fast before exact precision | Simpler rules, each a decided SPEC simplification (plan §5.3) |

## 3. Scope

- **In scope:** A5 (every rule in Appendix A), A6 (reporting), the Binding facts they need, and the R4 exit tasks
  (Appendix B). Because R5 runs in parallel, also the facts R5 shares with A5 and R5's start conditions (plan §7,
  §9.1).
- **Unchanged:**
  - A1–A4 and their components (DEC R4-M2a, R4-M6/R4-M6a/R4-M6b, R4-M7a, R4-M19, R4-M20–R4-M23, R4-M28), except as
    OD8, plan §4.6 and step 0 of plan §6.2 state: the MIR fact contracts restore A2's drop-row path, and A4
    publishes its static keys and anchors.
  - Binding's Origin judgments OJ1–OJ7 (PLAN10 §5.5): signature-level completion, the premise-only prover and the
    principal substitution stay in Binding. A5 judges the chains inside bodies.
  - Shadow mode until R6-check: MIR problems stay unpublished (DEC R4-M1).
- **Not in scope:**
  - T1–T4, Origin judgment during overload selection (R7, PLAN10 §16-3).
  - Language changes themselves. Each is its own proposal (plan §5.2).

## 4. Approach: borrow checking as facts and rules

### 4.1 Model

SPEC §15.6 uses a value-carried model, which fits Polonius:

- **Holders.** A Loan is carried by values. A **holder** is a move path (DEC R4-M21) or a referent holder (below). A
  move path's subtree is a range.
  - Replacing a holder wholly ends the Loans of its subtree. A partial replacement ends only the replaced part's.
  - Reading a holder collects the Loans its subtree carries and those its ancestors carry for the whole value.
  - `EndBorrow` ends a holder's carrying, never the Loan itself (the U7 F2 defect).
- **References.** A move path ends at a Deref or an Index step (DEC R4-M21), so referents get **referent holders**.
  Step 0 (plan §6.2) chooses one representation:
  - **Provenance:** the referent holders are the targets a reference's Loans name (§15.6.2, "Other followed
    references"), or one escaped holder per reference-typed parameter or capture. A read, write or use through a
    reference reaches them.
  - **Origin positions:** a holder carries Loans per Origin position of its Type, as a Polonius origin does. A read
    through a reference takes the referent's positions, and flow is position-wise, with equality at invariant positions.

  Either way:
  - A Copy read through a reference carries the Loans stored in the referent, not the reference's own Loan (§15.7.3).
  - A Loan stays live while a live reference can reach a value that carries it (§15.6).
  - A referent the body does not own (a parameter's or capture's referent, a static) is an **escaped** holder: Loans
    written into it stay live at every later point.
  - Loan places through references are compared by provenance, or a Loan whose place runs through a replaced holder is
    killed (Polonius `loan_killed_at`). The kill option is sound only because a reborrow's holder carries its parent
    Loans from the reborrow on (plan §4.3). This decides the repeated-site parent/child item (plan §5.1). The SPEC
    does not name Loans finitely yet; S1 makes the kill option the specified rule (plan §5.2). Under S1 only an
    in-place destruction of the base or an ancestor, with no borrow Deref step between them, kills: `=`, `replace`
    (a whole write of its target, §15.7) or the end of its storage. While a child is live, the base or an ancestor
    cannot be Moved, exchanged, swapped or exclusively borrowed other than as a `replace` target. P3 (plan §4.3)
    also makes a shared borrow of a Place on the child's path conflict with it, in either order, and the own-site
    check excludes a holder's own Loans by holder, never by Loan id.
  - Under design A3, conflicts compare access paths syntactically (P3, plan §4.3); referent holders and provenance
    serve content only: flows and liveness.
- **Origins (regions) never keep a Loan live.** They are judged in a separate stratum (plan §4.5).

A5 computes typed facts from published inputs with a fixed set of rules. The rules are the specification of A5; the
code is a fast evaluator of those rules, not a second definition.

### 4.2 Fact schema (initial)

The names are illustrative. Step 0 fixes the representation, which will be C# tables, not a Datalog engine. A relation
marked *derived* is computed by the rules, not read from MIR.

| Relation | Meaning | Producer |
| --- | --- | --- |
| `edge(P, Q)` | Control-flow edge; runtime and assumed lanes kept apart | MIR (`MirDataflow` P1/P2) |
| `issued(L, place, mode, P, H)` | A borrow at P creates Loan L, carried by holder H | MIR (`Borrows`) |
| `flow(H1, H2, P)` | At P the value read from H1 reaches H2 (copy, move, assign, aggregate, call result) | MIR; *derived* through a reference (from `carries` under provenance, position-wise under Origin positions); call facts from Binding |
| `replaced(H, P)` | A whole write at P of a Place that is its own move path (no Deref or Index step), StorageLive/StorageDead of its root, or EndBorrow of H (ends only H's carrying, never the Loan). It ends the carrying of H's subtree. A write through Deref or Index is `written_through` and ends nothing | MIR (`Writes`, `Kills`) |
| `written_through(place, H, P)` | A write through a reference, a non-path index or a dynamic container adds H's Loans to every Place it may reach and ends none | MIR |
| `reaches(place, T, P)` | The holders T a write through `place` may reach: the move-path prefix for a non-path index or a dynamic container; the provenance targets or the escaped holder for a reference | MIR; *derived* under provenance |
| `names(L, T)` | T is the holder of L's place | MIR; *derived* through references |
| `escaped(H)` | H is a static, or a referent holder of storage the body does not own (a parameter's or capture's referent) | MIR, Binding (parameters, captures) |
| `live(H, P)` | A value in H may be used after P, including observation by its destruction | MIR liveness (to be rebuilt: the backward pass and `MirTypes.Observes` left with U7), A2 `MirDropTable`, `MirTypes.Destruction`; closed over live references by the rules |
| `access(P, place, kind)` | Read, write, move, borrow or destruction (Drop, StorageDead) of `place` at P | MIR (`MirAccess`); `Kills` is split first, because it covers both StorageLive/StorageDead and EndBorrow (plan §6.2) |
| `child(L2, L1, P)` | Reborrow L2 through L1 (§15.6.3) | MIR; *derived* when the parent reference's Loans come from provenance |
| `base(L, B)` | Child Loan L was formed through the reference stored at Place B, its base (S1) | MIR, one per Borrow site |
| `destroyed_in_place(K, P)` | At P, the old value of move path K is destroyed in place: `=`, `replace`, or the end of K's storage (S1) | MIR |
| `base_op(P, K)` | At P, the Place K is Moved, exchanged, swapped, or exclusively borrowed other than as a `replace` target, or K's old value is destroyed by a `drop` that may reach a base below K, also at `=` and `replace` (S1). K is a Place, including one reached through a reference (`*a`), not a holder; any exclusive borrow counts | MIR, A4 (`drop` effects) |
| `reserved(L, P1, P2)` | Two-phase borrow reserved at P1 and activated at P2 (§15.6.7) | MIR (`TwoPhaseUniq`); (H) some acquisitions of call inputs pass `input: false` and form a plain `Uniq` (plan §6.2) |
| `call_effect(P, summary)` | Effects of the call at P | A4 summaries |
| `retains(call, input, output)` | A call's result or writable storage may retain an input's Loans (§15.6.4) | Binding (C1, C8) |
| `outlives(o1, o2, P)` | Origin relation from a fit, clause, well-formedness, annotation, borrow formation or containment | Binding, MIR |

Every relation is computed per Semantics case view, as A1 already is (DEC R4-M11).

### 4.3 Rules (example)

Core Loan flow, in Datalog-like notation:

```text
within(K, H)        :- K is H or a descendant of H.          // a subtree is a range (R4-M21)
overlap(K, H)       :- within(K, H) ; within(H, K).
killed(K, P)        :- replaced(H, P), within(K, H).

// S1. A child Loan ends where its base B, or a move-path ancestor of B with no borrow Deref step between them, is
// destroyed in place (`within`; an Index step counts only for identical constant indexes). Every rule that carries
// a Loan from P to Q takes the step, so the kill holds on every propagating rule.
ended(L, P)         :- base(L, B), destroyed_in_place(K, P), within(B, K).
step(L, P, Q)       :- edge(P, Q), !ended(L, P).

// Issue. A reborrow's holder also carries every ancestor Loan, from the reborrow on.
carries(H, L, Q)    :- issued(L, _, _, P, H), step(L, P, Q).
carries(H, L1, Q)   :- issued(L2, _, _, P, H), child+(L2, L1, P), step(L1, P, Q).

// Keep, unless replaced.
carries(H, L, Q)    :- carries(H, L, P), step(L, P, Q), !killed(H, P).

// Flow. A part keeps its path s below H1; Loans an ancestor carries for the whole value go to H2.
carries(H2·s, L, Q) :- flow(H1, H2, P), carries(H1·s, L, P), step(L, P, Q).
carries(H2, L, Q)   :- flow(H1, H2, P), carries(K, L, P), within(H1, K), K ≠ H1, step(L, P, Q).

// Write through a reference, a non-path index or a dynamic container: add, end nothing.
carries(T, L, Q)    :- written_through(place, H, P), carries(K, L, P), overlap(K, H),
                       reaches(place, T, P), step(L, P, Q).

// Liveness reaches through live references.
live(T, P)          :- live(R, P), carries(R, L, P), names(L, T).
live_loan(L, P)     :- carries(K, L, P), live(H, P), overlap(K, H).
live_loan(L, P)     :- carries(H, L, P), escaped(H).

// The drop and the store at one point: a value P stores counts at P when its destination is live after P.
stored(L, P, T)     :- flow(H1, T, P), carries(K, L, P), overlap(K, H1).
stored(L, P, T)     :- written_through(place, H, P), carries(K, L, P), overlap(K, H), reaches(place, T, P).
live_loan(L, P)     :- stored(L, P, T), live(H, P), overlap(T, H).
live_loan(L, P)     :- stored(L, P, T), escaped(T).

error(L, P)         :- live_loan(L, P), access(P, place, kind),
                       overlaps(place, loan_place(L)), conflicts(kind, loan_mode(L)).

// P3 (owner, 2026-10-17). loan_path(L) is the path L was formed through, across Deref steps.
// (a) A Move or borrow of a Place reaches the Loans formed through references stored in it; in this prefix
//     direction, reads, writes and destructions do not. The target borrow of a call that destroys its target in
//     place (`replace`) is exempt; base_op checks that destruction.
error(L, P)         :- live_loan(L, P), access(P, place, kind), kind ∈ {move, borrow(mode)},
                       !destroying_target(P, place), runs_through(loan_path(L), place),
                       conflicts(kind, loan_mode(L)).
// (b) An access of any kind whose path extends a live Loan's path, across Deref steps, reaches that Loan.
error(L, P)         :- live_loan(L, P), access(P, place, kind), runs_through(place, loan_path(L)),
                       conflicts(kind, loan_mode(L)).

// S1: the base of a live child, or an ancestor, cannot be Moved, exchanged, swapped or exclusively borrowed.
// runs_through(B, K): K is B or a prefix of B, stepping across Deref steps (no provenance resolution); an Index
// step matches any index unless §15.6.2 proves the two distinct.
error(L, P)         :- live_loan(L, P), base(L, B), base_op(P, K), runs_through(B, K).
```

- **Ban more, kill less.** `ended` kills only on a definite in-place destruction of B or a move-path ancestor with no
  borrow Deref step between them (`within`; the own payload Derefs of `obj`, `rc` and `arc` are storage). The ban
  matches any Place that may be B or an ancestor (`runs_through`), including possibly equal indexes. Erring toward
  the ban is sound; erring toward the kill would not be.
- **P3 is needed for soundness, not only S1.** Without it, the rules accept the first program below even with the
  `within` kill: `w`'s own Loan (base `q`) ends at `q = o@uniq`, and then `w.p@follow` meets nothing (prototype round
  6, case 29a). They also miss an alias made inside a call (second program). P3 rejects each where the second of the
  two borrows is formed, in either order: form (a) when the alias comes second, form (b) when the child does (third
  program). (H) The current SPEC ("Inline parts exclude referents", §15.6.2) may accept the call case, a SPEC gap.
- **Own site.** An access through a reference held in X is checked against Loans that holders other than X keep
  live; X's own carrying is excluded by holder, never by Loan id. Exclusion by Loan id is unsound when another holder
  carries the same Loan (round 6, case 30c).
- **No kill across a Deref** (owner, 2026-10-17). `q = …` does not end a Loan based at `(*q).p`, so a valid write
  through a replaced outer holder (23a) and a traversal through stored `uniq` links (30d) stay S1 precision losses.
  With P3, killing across the Deref (the root-most kill) was sound on all 92 round-6 cases, but the owner did not
  adopt it (`draft/Sketches/A5 Step 0/design-A3.md` §9).

```kimi
// S has `var p: uniq/i32`.
var s = S(p: t@uniq)
var o = S(p: u@uniq)
var q = s@uniq             // L_q
let c = q.p@follow@uniq    // L_c, base (*q).p: c carries L_c, L_p and L_q.
let w = q@follow@ref       // Error (P3): borrowing *q reaches L_c, formed through the reference in s.p.
q = o@uniq                 // Without P3, w's own Loan (base q) ends here ...
let a = w.p@follow         // ... and w reads t through the old s.p while c can write it.
c@follow = 5
inspect(w.p@follow)
```

```kimi
// Slot {o} has `var data: uniq/Array<i32> during o` and
//   `func first(self: ref/Self) -> ref/i32 during self => self.data[0]@ref`.
var items = [1, 2, 3]
var slot = Slot(data: items@uniq)
let c = slot.data@follow@uniq   // base slot.data
let x = slot.first()            // Error (P3 a): borrowing slot reaches L_c, formed through slot.data.
c@follow = [9]                  // Without P3: destroys the array x points into ...
inspect(x@follow)               // ... and x reads it after free.
```

```kimi
// The same Slot, in the other order.
var items = [1, 2, 3]
var slot = Slot(data: items@uniq)
let x = slot.first()            // L_x on slot
let c = slot.data@follow@uniq   // Error (P3 b): the path slot.data.* extends L_x's path slot.
c@follow = [9]
inspect(x@follow)
```

- **Values in transit.** When a drop and a store happen at one point, the stored value has left its dying Temp but
  has not reached its destination. `stored` keeps its Loans live at that point (prototype round 5, case 24c), for a
  destination that is live, overlaps a live holder, or has escaped.
- **Temps.** MIR evaluates the right side of `=` into a Temp first (`MirBuilder.Replace`), so
  `cursor = cursor.children[0]@uniq` issues the child at one point and destroys `cursor` at the next. The kill
  therefore has to hold on the flow from the Temp, which `step` gives it.

The block is illustrative; design A3 (`draft/Sketches/A5 Step 0/design-A3.md`) states the exact rules, which compare
access paths syntactically under P3 and resolve no accessed Place. The step-0 prototype needed refinements to match
the SPEC. Five are in the block: parent Loans carried from the reborrow on, liveness fed back through references,
S1's kill on every propagating rule (`step`), values in transit (`stored`), and P3. The own-site check is stated
above. The implementation must also carry the other two: flows are read through references, and reservations are
modeled.

- A flow into H2 at P is not killed by `replaced(H2, P)`: the flowed value is H2's new content, and the replacement ends
  only H2's earlier Loans. `ended` still applies. `H2·s` falls back to H2's nearest tracked ancestor.
- A call's `flow` (F3) moves the whole retained input's Loans to the result's root: parts do not correspond.
- Loans flow on runtime edges (P1) and, for heads no runtime path reaches, on assumed edges (P2), as in A1 (DEC R4-M20),
  so conflicts in unreachable code are found (§14.10.3). Only Drops the runtime edges reach observe Loans (DEC R4-M6a).
- `child+` is the transitive parent relation. Materializing the parent Loans at the reborrow keeps them when the child
  Loan is killed through its replaced parent holder:

```kimi
var x = 1
var y = 2
var r = x@uniq            // L1
let c = r@follow@uniq     // L2, a child of L1: c carries L2 and L1.
r = y@uniq                // Valid: c does not depend on r (§15.6.3). L2 ends: its base r is destroyed in place.
// x = 3                  // Error: c still carries L1 and is used below.
inspect(c)
```

The SPEC §15.6 example, with its facts:

```kimi
var a = 1
var b = 2
var r = a@ref   // issued(L1, a, ref, P1, r)
r = b@ref       // replaced(r, P2); issued(L2, b, ref, P2, r)
a = 3           // access(P3, a, write): r no longer carries L1, so no error.
inspect(r)
```

Joins are may-facts (§15.6; example after §14.9.2): a joined result carries every incoming Loan.

```kimi
var x = 1
var y = 2
let r = if ready => x@ref else => y@ref   // carries(r, Lx) and carries(r, Ly)
x = 3                                      // error(Lx): Lx is live because r is used below.
use(r)
```

Reads through references take the referent's Loans (plan §4.1). A flat `flow(r, copy)` would be over-strict here:
`copy` would carry the Loan on `slot` (L_slot), so the write to `slot` would conflict although `copy` does not depend
on it.

```kimi
var data = makeData()
var other = makeData()
var slot: ref/Data = data@ref   // slot carries L_data
let r = slot@ref                // r carries L_slot, the Loan on slot
let copy = r@follow             // copy carries L_data, not L_slot
slot = other@ref                // Valid: copy does not depend on slot, and r is dead.
consume(data@move)              // Error: copy is used below.
inspect(copy)
```

Two-phase reservations (§15.6.7) are `reserved` facts:

- Until activation, a reservation admits shared reads and shared borrows of its Place but conflicts with writes, Moves,
  destruction, reallocation and other exclusive operations.
- At activation, every reservation of the call must be able to become live at once.
- A transfer that abandons the call ends its reservations without activation.

```kimi
var p: i32 = 1
Kimi.Intrinsics.replace(p@uniq, with: p + 1)  // Valid: p is read during the reservation.
```

### 4.4 Calls and callable values

- **Retention.** A call's retention comes from its instantiated public contract only (§15.6.4). Binding publishes the
  per-call facts: retained inputs, writable storage, selected signature and effective contract (C1, C8).
- **Effects.** `call_effect` and per-result mutable-static anchors come from A4's summaries, which A4 computes from
  bodies. Unknown effects conflict with live mutable-static Loans (§15.6.4).
- **Closures.** The capture, receiver and result contracts (C2) become facts of the Closure's formation and calls
  (§15.8.2).

### 4.5 Origin relations

- **Constraint generation** produces `outlives` facts from:
  - Relate by variance; substituted declared clauses; well-formedness per case; annotations (§15.6.1);
  - borrow formation, and Reborrow containment up to the first shared layer (§15.6.3, §15.5);
  - Closure results inferred from the body (§15.8.2).
- **Judgment** is a closure over chains by reachability (§15.6.5):
  - Refuted only from a finite Origin to a fixed Origin outside its bound;
  - Unknown only between fixed Origins;
  - fixed Origins equated through one fresh Origin must be equal (§15.6.1).
- **Strata.** Judgment uses no liveness and no reachability of program points, so it is a stratum separate from Loan
  flow. Loan flow reads only contract-level relations (`retains`), which Binding builds from the instantiated contract
  with Relate and the substituted Clauses. Origin classification reads A4's per-result anchors.
- A failed relation adds no constraint (§15.6.1), so no Loan conflict follows from it.

### 4.6 Bounded work

V16 applies (PLAN §3): work beyond input size plus output size is a defect.

- **Evaluator.** The evaluator is exact against the rules as simplified (plan §5.3); simplicity comes from simpler
  rules, never from an approximation the rules do not state. One candidate for step 0's designs is Polonius
  alpha-style reachability over (Origin position, point) or (holder, point) nodes after the location-insensitive
  pre-pass below. Its bound formula must show that the holder factor below is avoided, as the counts by differences
  of the superseded DEC R4-M24 did; otherwise the owner restates V16's output.
- **Output.** The output is the live Loans per point plus the error facts. A materialized `carries(H, L, P)` would
  exceed input plus output by a holder factor, so the evaluator does not materialize it per holder and point.
- **Representation.** `MirDataflow` uses a dense arena (DEC R4-M20). Keep it unless the design shows a need for sparse
  sets. A switch needs a superseding DEC entry.
- **Pruning.** Propagate only inside live ranges (PLAN10 §13.3). A location-insensitive first pass finds the Loans
  that can conflict at all; only those get location-sensitive propagation (the Polonius hybrid).
- **Proof.** State the bound as a formula, count it with a work counter (as `MirEffects.Evaluations` does), and check
  it with 2× and 4× scaling tests (PLAN10 §14-10).
- **Limits.** Size every arena in `MirDataflow.Prepare` (DEC R4-M15a). The workspace limit stays the Resource
  diagnostic `OwnershipStorageLimit_Kd`.

## 5. Rule-expressibility audit

### 5.1 Procedure

The audit comes first. It decides what the implementation must do and which language changes are needed.

1. **List the rules** of Appendix A.
2. **Write each rule** as facts and rules (plan §4).
3. **Classify each rule:**
   - **Expressible:** facts from MIR or existing views only.
   - **Expressible with a new fact:** name the Binding fact (plan §7) and its owner.
   - **Not finitely expressible:** the rule needs an unbounded domain, value identity across replacements, or a search
     without a finite bound. It needs a SPEC change (plan §5.2).
   - **Simplified:** expressible, but a simpler rule is proposed that rejects some forms the rule accepts. It is a
     precision loss (plan §5.3). The audit marks candidates; step 0's designs confirm them.
4. **Start with the known hard items** (Appendix B):
   - C3: retained transfers and the repeated-site parent/child Loans, decided with the reference representation
     (plan §4.1); positional prepared-input transfer (with F9).
   - C5: the correlated result/effect domain of `preserves results`.
   - E5: the per-`require` unavailable frontier in reporting.
   - A5-closure: the Closure receiver and capture contracts.
   - The assumed-path join of §14.10.3 ("keeping only what holds on all of them") against the may-facts of §15.6.
     (H) It reads as union: a may-fact holds on a path when it covers that path's Loans, so what holds on all assumed
     paths is their union. This follows §15.6's liveness join and the example after §14.9.2; §15.1.1 supports it only
     by analogy. If it holds, this is not a (c) ambiguity; the prototype keeps a check.
5. **Prototype.** Executable rules for the hard items go to `draft/Sketches` and run on small programs before the
   design is fixed.

**Findings so far** (a read-only investigation on 2026-10-16; the audit confirms them):

- **Not finitely expressible as written:** three rules. S1, S2 and S5 (plan §5.2) resolve them.
  - Reborrow Loan identity at a repeated site (§15.6, §15.6.2, §15.6.3). Places reached through references are named by
    the Loans the references carry, so a reborrow in a loop names `root.children[0].children[0]…` without bound. One
    static ID per site lets the loop-head may-fact make `cursor` carry the site's own Loan, and treating it as authority
    accepts access through the parent (the failure OBS13 reproduced).
  - `preserves results` (§8.11.3, §8.11.4). "The same value `v`" across replacements is value identity with no static
    rule. Conformance needs a per-return-path result/effect domain over Field roots, which is unbounded in recursive
    Types with borrowed Fields.
  - (H) Effect summaries (§15.6.4). `Drop` or `Call` atoms on associated projections under polymorphic recursion
    (`T.Child.Child…`) can grow without bound. §15.6.4 widens only static keys, through the open key. Widening these
    atoms without a SPEC rule would be a conservative Language error (a conflict under `confined`, §8.11.2).
- **Expressible as written:**
  - C3's other parts: retained transfers, positional prepared-input transfer (`default_retains`, published once per
    default), phantom slots per (holder, slot), escape to caller storage;
  - A5-closure: finite sources per Semantics case, cut by the fixed result Origin; needs C2 only;
  - the other §15 rules. (H) Writes through references cost about writes × reached holders × carried Loans; step 0
    checks this against V16.
- **Clarification only:**
  - Origin inference bounds (S3).
  - Q1-ref (S4).
  - E5 needs a DIAGNOSTICS change, not a SPEC change.
- **Already bounded:** Semantics cases are exponential in pair binders, but exceeding the case limit is already a
  Resource failure (§8.15.2).

Q1-ref is not a rule. Since the 2026-10-15 rewrite, §15.6 ends no Loans on a write through a reference, so keeping the
referent's earlier Loans is the specified behavior. The old path's strong update gives (a) rows. Changing this needs a
SPEC change; S4 only confirms the current text (plan §5.2).

The audit's output is a table of rule → class → facts → family, the input to plan §6 and §7.

### 5.2 SPEC change items and simplification candidates

Use counts are approximate grep counts for the embedded library (lib), the 43 milestone programs (ms) and, where
noted, the xUnit sources.

**Required.** Without these, the rule cannot be written as finite facts and rules (OD7). S1, S2 and S5 each get their
own proposal; S1 and S2 are being drafted in a separate session.

| Item | Change | SPEC | Removes | Lost | Use |
| --- | --- | --- | --- | --- | --- |
| S1 Loan identity by base | A Loan is identified by its Borrow site and Semantics case. A child Loan formed through a reference stored at Place B has base B. While it is live, access through B to an overlapping Place is suspended, and B and its ancestors cannot be Moved, transferred by `exchange` or `swap`, or exclusively borrowed except as a `replace` target. A definite whole write of B or an ancestor, with no borrow Deref step between them, that destroys the old value in place (`=`, `replace`), or the end of B's storage, ends it; its values keep their parent Loans. A write through a reference or a non-path index ends nothing. The provenance row names Places from bases. With P3, a borrow or Move of a Place on the child's path conflicts with it, in either order | §3.4, §3.5.1, §13.5.5, §13.5.6, §15.6, §15.6.2 (with "Inline parts exclude referents"), §15.6.3, §15.7, §15.7.3 | Unbounded Place names; OBS13's repeated-site failure. The kill option of plan §4.1 becomes the specified rule | Moving, exchanging, swapping or exclusively borrowing a base or an ancestor while its child is live, for example `let c = p@follow.x@uniq; let q = p@move; use(c)` or `let w = r@uniq` with a live child of a `var r`. Rust also rejects these. Also a write through a replaced outer holder while a child based past its Deref is live (23a), and a traversal through stored `uniq` links (30d) (plan §4.3). S1 needs P3 and the own-site check (design A3 §9) | Self-reborrow loops: lib 0, ms 0, tests 0. Step-0 census (design A3): 23a lib 0, ms 0, at most 8 reborrows in 3 test files; 30d lib 0, ms 0, 3 test files. Base operations with a live child: counted by the census |
| S2 `preserves results` by receiver Place | An earlier call counts when it is on the same receiver Place and no path between the two calls replaces, Moves or destroys that Place or an ancestor, or writes through a reference that may reach it. Conformance checks the body as an ordinary body in which the Loans an earlier result may keep are live throughout. Calls contribute their declared effects, never callee bodies, and a delegated call may rely on the bound being verified | §8.11.3–§8.11.6, §15.6.3 | C5's value identity and its transitive result/effect domain | The same value reached through two Places; helpers whose signatures demand more access than their bodies use | lib 1 declaration (`Iterator.kimi:6`) governing 12 conformances; ms 3 conformances (Milestone13, Milestone29); xUnit 18 files |
| S5 Effect atoms widen | An effect atom on an associated projection that grows under polymorphic recursion widens to an unclassifiable effect, as §15.6.4's open key widens static keys. The alternative is a depth limit whose excess is a Resource failure | §15.6.4, §8.11.2 | The unbounded atom domain of effect summaries | Callers whose concrete instances would classify the effect: it conflicts under `confined` and `preserves results` bounds (§8.11.2) and with live mutable-static Loans (§15.6.4) | (H) lib 0, ms 0; counted by the census |

- **S1's soundness (H).** The step-0 prototype (`draft/Sketches/A5 Step 0/`) checked S1 in rounds 1, 2, 5 and 6.
  - Round 1 (39 programs) found S1 as first written, which forbade only Moving the base, unsound in 5 programs:
    `exchange` and `swap` of the base, also as a call or inside a callee.
  - Round 2 (49 programs) found two defects in plan §4.3. Both are fixed, together with two more found in review:
    - the base-operation rule must range over Places, not holders, or an exchange of a base reached through a
      reference (`*a` for `a: uniq/uniq/T`, passed to a call) is accepted. Review added that an Index step must
      match any possibly equal index (`exchange(arr[j]@uniq, …)` with a live child of `arr[i]`);
    - the loop step `cursor = cursor.children[0]@uniq` must not conflict with its own child. Review added that MIR
      goes through a Temp, so the kill must hold on every propagating rule (`step`), not only at issue.
  - Round 5 (75 programs) checked these fixes. The Temp-shape loop (21a valid, 21b rejected) and the unknown-index
    exchange (22a rejected, constant indexes 22b valid) behave as stated. It found two more points:
    - unsound: when a drop and a store happen at one point, the value in transit from a dying Temp was missed
      (24c). The `stored` rule of plan §4.3 fixes it; review added escaped and overlapping destinations;
    - over-strict: not killing across a Deref rejects a valid write through a replaced outer holder (23a). Killing
      across the Deref passed the 75 programs, but review found it unsound without P3 (the counterexample in plan
      §4.3).
  - Round 6 (92 programs) found S1 unsound without P3 even under the `within` kill (29a), and unsound with exclusion
    by Loan id (30c). S1 with P3 and the own-site check passed every case. With P3, the root-most kill across a Deref
    was also sound, but the owner kept the `within` kill (2026-10-17), so 23a and 30d stay S1 precision losses.
  - The load-bearing clauses are P3 and the ban on exclusively borrowing the base or an ancestor. An exchange inside a
    call is visible only as that borrow, and P3 catches the shared aliases the ban cannot see (29a). The prototype's
    cases 15a and 15b show that the `replace` exemption is needed and sound.
  - The Move ban is also required. Without it, a base Moved on one path (`if c => stash(r@move)`) and then written
    could end L2 while the stashed old reference keeps L1.
  - The invariant: after the kill, no value outside the child's descendants that outlives the kill can reach B's old
    reference in a conflicting mode. P3 keeps shared aliases of a Place on the child's path from being formed while an
    exclusive child is live (29a), and the own-site check keeps another holder's copy of the same Loan in force (30c).
    The `ref`-base cases of round 5 (9j, 13a–e, 16e, 23b) are sound under the kill rule.
  - The rejected alternative identifies a Loan by (site, base) and renames the base on Move. It costs sites × holders
    and still cannot follow an exchange inside a callee, so it is not loss-free either.
- **S1's proposal also defines** the base when the parent is reached through another reference or a non-path index,
  and which sites are Borrow sites: implicit borrows, Loans created by calls, comparison inspection and inlined
  defaults. It states that an object-handle base keeps §13.5.5's handle Loan, so `=` on it stays an error. It checks
  two points against §3.5.1 and §13.5.6 (H): whether some Lost forms are already rejected today, and whether
  exclusively borrowing an ancestor for a call is a current gap that S1 closes. It also states that destroying an old
  value whose `drop` may reach the base counts as an exclusive use of the base (round 5: 24a rejected, 24b valid).
  `=` and `replace` destroy the old value while the new child is already live, as in `s = S(r: s.r@follow@uniq)`
  with a `drop` that writes through `self.r` (§15.6.6; Rust rejects this, E0713).
- **S2's soundness (H)** is prototyped or argued in its proposal. A receiver Place reached through a borrowed Field is
  named by S1's bases.
- **S2's scope:** narrower than making `preserves results` a signature rule, which is rejected because it loses the
  Iterator guarantee. "Completed transitive summaries" then applies to `confined` only. The census checks that the 15
  conformances still verify when calls contribute only their declared effects; a failure goes to DEC O3.
- **S5:** growing atoms stay `Unsupported_Kd` until it is decided.

S1 keeps every verdict except the base operations above, and static site IDs now decide them. In the loop below, the
Loan with base `cursor` is the receiver Loan of the `indexUniq` call; `next`'s own `@uniq` Loan has the call's result
as its base.

```kimi
// Tree has `var value: i32` and `var children: Array<Tree>`.
func deepest(root: uniq/Tree) -> uniq/Tree during root
    var cursor = root
    while cursor.children.length > 0
        let next = cursor.children[0]@uniq
        // cursor.children.clear()   // Error: next is used below.
        cursor = next                // Ends the Loans with base cursor; root's Loan stays.
    return cursor
```

S1 ends a child Loan only where the base's old value is destroyed in place:

```kimi
var x = 1
var y = 2
var r = x@uniq            // L1
let c = r@follow@uniq     // L2, base r: c carries L2 and L1.
// Error: r is the base of a live child, so it cannot be exchanged.
// let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq)
r = y@uniq                // Valid: r's old value is destroyed in place, so L2 ends.
inspect(c)                // c still carries L1.
```

S2 keeps the SPEC's `takeTwo` (§8.11.3) and rejects a call across a replacement:

```kimi
func takeAcross<S>(source: uniq/S, fresh: S) -> (Option<S.Item>, Option<S.Item>)
    S is StableTake
    let first = source.take()
    source@follow = fresh@move    // Replaces the receiver's value.
    let second = source.take()    // Error: first's Loans are no longer excluded.
    return (first@move, second@move)
```

**Clarifications.** The meaning stays the same; the text becomes decidable. The owner may request them directly as a
SPEC revision.

- **S3. Origin inference bounds** (§15.3.6). Add: "A variable's bounds are followed through other variables to fixed
  or finite Origins and `static`. If none is reached, the variable has no candidate."
  - (H) Without this, a variable whose bound is another variable, such as a covariant variable bounded by a
    contravariant one, has no defined candidate.
  - It overlaps the LR part of the 2026-10-06 Callable and Local Region Implementation Plan.
- **S4. Q1-ref confirmation** (§15.6). S1015 (`72fb4b79`) narrowed complete replacement. Completely replacing a holder
  still ends the Loans it carried, but a complete write through a reference no longer ends the referent's earlier
  Loans.
  - The old §15.6.4 sentence "Complete replacement ends the replaced contents' dependencies" was dropped, and S1015's
    list of acceptance changes does not mention it. The old Q1-ref row (`slot@follow = .None`) relied on it.
  - The owner confirms the narrowing (OD11), and the SPEC gains an example:

    ```kimi
    var a = 1
    var b = 2
    var slot: ref/i32 = a@ref
    let r = slot@uniq
    r@follow = b@ref    // A write through r: slot may now carry b's Loan, and a's Loan is not ended.
    // a = 3            // Error: slot may still carry a's Loan and is used below.
    inspect(slot)
    ```

  - Census: lib has no `@follow` writes. ms has 5 plain and 2 compound ones (Milestone33, Milestone7), which (H)
    store owned or Copy values. Writes through `uniq/Self` Fields and through `indexUniq` are not counted yet.
- **E5, in DIAGNOSTICS only.** Exact cuts can produce output of size requirements × rejected nodes. Either memoize the
  cut per node with shared sets, or name only the immediate unavailable inputs. Both satisfy DIAGNOSTICS §4.3.

**Optional.** Each removes work but loses forms, and each is its own owner decision. A proposal argues from the
Principles. Narrowing the SPEC for implementation reasons needs the owner's explicit request (AGENTS: never narrow the
specification to fit missing implementation); a reopened SETTLED entry names the reason that no longer holds.

| Candidate | SPEC | Removes | Use | SETTLED / DEC |
| --- | --- | --- | --- | --- |
| Remove pair binders and Semantics cases | §8.1.1, §8.14, §8.15.2 | The largest saving. A5: per-view runs and the M39 case graph. R5: per-instance case vectors, the lowering of Adapt, PairLayer and PairAcquire, and per-view drop groups | lib 1 (bodyless `clone<s/T>`), ms 1 (Milestone28, 3 functions), xUnit about 235 sites in 39 files | "Pair-slot premises in every Semantics case" rules out merging cases, so removal is the only simplification; "A borrow-only uniq pair follow…"; DEC R4-M18, R4-M19 |
| Defaults are closed expressions | §7.2.2–7.2.3 | Default templates, including M39's template calls (OD9) | ms 2 (Milestone21, Milestone22) | Avoids the reopening of "Instance-evaluated generic defaults" that OD9 would cause |
| Static storage keys are never generic | §22.4.4 | Per-instance slots and open keys in A4 and A5 | lib 0, ms 1 (Milestone42 `Cache<T>.Statistics`) | — |
| Static storage needs no shutdown cleanup | §22.4.3 | The shutdown registry and its ordering (R5) | lib 0, ms 1 (Milestone42 tests the order) | DEC O3 |
| (H) Instance layout obligations are checked at the declaration, or are Resource failures | §8.15.3, §23.2.2 | Running the instance collector before acceptance (plan §9.1) | lib 2 (`#Layout("C")`), ms 0 | — |
| Closure results never depend on the call receiver | §15.8.2 | The receiver-dependent part of A5-closure (already `Unsupported_Kd` since R1) | lib 0, ms 0; about 15 Closure forms in xUnit, rejected or `Unsupported_Kd` today | — |
| Mutable-static borrows never leave the body | §11.8, §15.6.4 | Static-anchor propagation | lib 0; ms whole reads and writes only | — |
| One Origin slot per Type; no declared `outlives` or meets | §15.2.2, §15.3, §15.4.3 | Clause substitution, set resolution | `outlives`: lib 0, ms 0; multi-slot: ms 1 (Milestone16 `Pair {left, right}`); implicit meets from result elision not counted | — |
| Unreachable items are errors | §14.10.3 | The assumed-path lane (P2) for A1 and A5 | lib 0, ms 0 | Reverses the §14.10.3 rewrite approved in PLAN10 §16-4 |

Not recommended: changing Closure erasure. §7.7.4 already leaves its allocation count to the implementation, so plan
§9.1 records the inline rule for small environments as a P2 baseline instead.

A SPEC change needs the owner's explicit request (AGENTS). It states its rationale and how the Principles hold, and it
updates the affected chapters, examples, tests and milestone programs together. Rejecting a library or milestone
program needs the owner (DEC O3).

### 5.3 Precision losses

A **precision loss** is a form that the SPEC, as amended by S1, S2, S5 and any adopted Optional candidate, accepts
and that a chosen design rejects through a simpler rule.

- **Only as a rule clause.** A loss is a decidable clause of the A5 rules (plan §4.3) with its own fact, written as
  S1's base operations are. The evaluator is exact against the rules with these clauses (plan §4.6). An
  approximation that cannot be stated as such a clause is not allowed, so A5 never reports a conservative Language
  error.
- **When.** A loss is decided before the family that needs it is implemented (plan §1). Step 0's designs list the
  losses known then. A rule unit that finds a new one stops, reports it, and lands only after the owner's decision.
- **Each row** gives:
  - the SPEC rule, the clause and the simpler rule text;
  - an example of a rejected form;
  - lib, ms and xUnit counts;
  - its own argument from the Principles.

  The step-0 prototype runs each clause on adversarial programs for soundness.
- **Decision.**
  - OD12 sets the policy at adoption. It is the owner's request for these SPEC revisions, which AGENTS requires
    because the trigger is implementation simplicity.
  - Each list of rows is then approved as one decision (OD13), and the approved rows become one SPEC revision.
  - S1, S2 and S5 stay separate proposals (OD7), and each Optional candidate of plan §5.2 is its own owner decision.
- **Between approval and the SPEC revision,** a violation of an approved clause is a located `Unsupported_Kd`, which
  becomes the Language code when the SPEC adopts the clause. The same holds for S1, S2 and S5 between their decision
  and their SPEC integration. Tests of the affected SPEC examples expect `Unsupported_Kd` until then. Nothing is
  published before R6-check (shadow mode), and every revision lands before the R4 exit, so no family stays
  `Unsupported_Kd` (plan §1).
- **Rejected rows.** If the owner rejects a row, precision is added instead: the design drops the clause and implements
  the exact rule.
- **Ledger.** This list is the record. A ledger row caused by a loss carries the row's id; a loss the old path also
  rejects produces no ledger row.
- **Limits.**
  - Soundness is never traded. A design that accepts a program breaking memory safety is wrong, whatever it saves.
  - A loss that rejects a library or milestone program goes to DEC O3. For a rule clause of this section, precision is
    added instead if the owner keeps the program. For S1, S2 and S5, which have no exact finite form, DEC O3 and O4
    apply.

## 6. Families and order

### 6.1 Families

Each family groups rules that share facts. Binding facts are prerequisites (plan §7).

| Family | Rules (Appendix A) | Prerequisite facts |
| --- | --- | --- |
| F1 Core Loan flow | 1 (program points), 2–6, 8, 14, 20, 23, 24, 27, 29 | MIR, `MirDropTable`, `MirTypes.Destruction`; the reference representation (plan §4.1) |
| F2 Reborrows and splitting | 9 (child Loans, region splitting), 28 | None |
| F3 Calls | 2, 9 (Iterator descent), 11, 15, 30 | C1, C8; the `TyCtxt.Relate` and `Clauses` views |
| F4 Origin relations | 7, 9 (containment), 13, 19, 20 | C1; substituted clauses and well-formedness (OJ outputs) |
| F5 Effects and static anchors | 1 (static keys), 12, 29 | A4 summaries, plus static keys, open keys and per-result mutable-static anchors (DEC R4-M22 left static keys to their first reader). MIR has static roots (`MirPlace.Root < 0`, `MirStatic`) but no closed or open storage keys yet |
| F6 `preserves results` | 10 | C5's Binding part (finite domain) |
| F7 Closures and escape | 17, 18 | C2 |
| F8 Updates and match subjects | 5, 16, 21, 25, 30 | Provenance; U8p |
| F9 Defaults and cases | 26 | M39; C3's positional prepared-input transfer |
| F10 Unreachable code | 22 | None |
| A6 Reporting | All problem rows | E4/E5's Binding part |

### 6.2 Order

1. **Step 0: preparation.**
   - The audit (plan §5).
   - The fact schema, the reference representation and the evaluator: two independent designs, a design review, and
     the owner's decision on any vocabulary entry (DEC O1). Among sound designs whose losses are rule clauses, the
     ranking is simple, then fast, then precise. Each design lists its precision losses for OD13 (plan §5.3).
   - A census over the library, milestones, corpus and fixture sources. It sizes each family and counts:
     - the bodies with more than one Semantics case view, and the views without Loans, which Loan flow skips (Origin
       judgment still runs per view, DEC R4-M11);
     - the forms S1–S5 and the precision losses would reject: base operations with a live child, P3's borrows and
       Moves of a Place on a live Loan's path, the 15 `preserves results` conformances under S2, growing effect
       atoms, and writes through references. Step 0's census (`draft/Sketches/A5 Step 0/census.md`) has the counts.
     VERIFICATION records the V16 shapes.
   - The prototype in `draft/Sketches`: the rules of plan §4.3, S1's soundness and each design's loss clauses, run on
     adversarial programs (plan §5.2, §5.3). A rule unit that reports a new loss later adds its clause to the
     prototype and runs it before the owner decides.
   - **MIR fact contracts,** one unit before F1 and before the R5 split (plan §9.1):
     - rebuild the backward liveness pass and destruction observation (`MirTypes.Observes` left with U7);
     - split `Kills`, which covers StorageLive/StorageDead and EndBorrow alike (`MirDescriptors`);
     - produce `TwoPhaseUniq` for every reserved call input. (H) Some acquisitions pass `input: false`
       (`MirBuilder.Values`), so the plan §4.3 example `replace(p@uniq, with: p + 1)` would be rejected;
     - restore `MirDropRow`'s Path column (removed with U7) and freeze the row shape;
     - publish "not analysed" apart from check-only Drops (`Group` returns −1 for both).
   - **A4 publication:** static keys, open keys and per-result mutable-static anchors as a view A5 reads. A4 never
     reads A5, so the two do not depend on each other.
   - **Origin boundary table:** for each Origin judgment, whether Binding (OJ1–OJ7) or A5 decides it. The sites are
     `Binding.OriginProof`, `LocalRegions` and `CallRelations`. After R6-check each OriginRelation failure is
     reported once.
   - **Ledger re-baseline** at the step-0 base.
2. **Step 1: the joint core.** The C1 and C8 fact units and the `TyCtxt.Relate` and `Clauses` views come first, then F1,
   F2, F3 and F10 together.
   - Loans after calls need retention, Relate and Clauses: the work reverted in `0d989e68`. A call-free F1 would be
     redone when F3 lands.
   - Until the joint core lands, the library and milestones may report A5 problems. Its units are checked by their
     rule tests only. DEC R4-M12 and the corpus condition of plan §8 apply from the end of step 1 (OD3). Nothing is
     published in shadow mode.
3. **Step 2: the remaining families,** each as "Binding fact unit(s) → rule unit": F4, F5, F7, F8, F9, then F6. The
   audit may change the order.
4. **A6** grows with each family that reports and finishes last.
5. **Exit.** R4 exits when every family and every exit task (Appendix B) is complete and the final end-to-end review
   passes (plan §9.3). R6-gen switches code generation to MIR after R5; R6-check switches acceptance after R4 and R6-gen
   (plan §9.1).

## 7. Facts and MIR-side work

- **Rule.** "Binding publishes a fact the MIR side lacks as a small view; Mir never re-derives it." (ARCH, migration
  rules)
- **Shape.** Each fact is built in R7's target shape: SyntaxId columns or `TyCtxt` views, one owner, immutable after
  Bind, checked by its producer.
- **D5.** New views leave the old Analysis and Emission readers unchanged.
- **Starting points** for the step-1 and F4 fact units:
  - the reverted R4-M9b views `TyCtxt.Relate`, `ComponentVariance`, `InvariantTarget`, `Clauses` and `Callable`;
  - `OriginAtom.Of` and `UniversalRelations.For`, which R4-M9b deferred to U9 and which were never built;
  - the existing `TyCtxt.Implementation`, `TyCtxt.Witnesses`, `TyCtxt.Copy` and `TyCtxt.Sealed`.
- **One table for both consumers.** R5 also reads Binding facts, so A5 and R5 share this table. Each fact has one
  producer and is built before its first consumer, A5 or R5.

**Binding facts** (definitions from OBS13, then the facts R5 needs):

| Fact | Definition | A5 family | R5 consumer |
| --- | --- | --- | --- |
| C1 | Requirements, contextual inputs, genuine premises, immutable publication | Step 1, F4 | — |
| C2 | Closure capture, result, receiver and erasure contract | F7 | Closure erasure shims |
| C5 (Binding part) | Boundary results and correlated completion | F6 | — |
| C8 | Selected Callable signature; exact witness and effective-contract transport | Step 1 | Function-item shims |
| M39 (Binding part) | One acquired-Place outcome per case; template calls for omitted defaults | F9 | Case and default lowering |
| E4/E5 (Binding part) | Independently bound parts; causes of rejected or undecided nodes | A6 | — |
| U8p | Place and backing dependency | F8 | — |
| `VTableDef` | Slot order per virtual root; today a sort by source path and span (`VirtualGenerationPlan`) | — | Virtual dispatch |
| Override applicability | Which override applies per instance (DEC R4-M28); today `IsInapplicableVirtualBody` | — | Virtual dispatch |
| Copy and Sealed proofs | `TyCtxt.Copy` and `TyCtxt.Sealed` exist; Emission still calls `ProveCopy` and `ProveSealed` | — | Layout, drops |
| Specialization by Subst | Today `SelectSpecialization` and `InstantiateForwardedCall`, keyed by call | — | Instance collection |
| Comparison and formatting plans by Type | Today `ComparisonPlan(site)`, keyed by syntax | — | Body lowering |
| Destruction calls | Today `DestructionCall` | — | Drop elaboration |

Today Emission calls about 36 Binding methods at about 69 sites. The rows above are the known ones; R5's step 0
lists the rest (plan §9.1).

**MIR-side items the rule evaluator replaces:**

| Item | Content | Family |
| --- | --- | --- |
| C3 | The general transfer engine: retained transfers, kill and join rules, a finite sparse representation | F1–F3, F9 |
| C4 | Source-chain eligibility and judgment | F4, F6 |
| C6 | Witnesses; caller and hidden-region checks | F3, F6 |
| C7 | A5 lifecycle, driver and inspection | Step 0 |
| C5 (MIR part) | Result and effect correlation inside bodies | F6 |
| M39 (MIR part) | Case graph and default-call integration | F9 (OD8) |
| E4/E5 (MIR part) | Reporting frontier inside bodies | A6 |

**Stage ledger.** The facts built here move out of R7's scope; R7 consolidates what remains.

## 8. Verification and metrics

- **Rule tests.**
  - Each rule gets positive and negative cases at the MIR layer.
  - Every SPEC example in Appendix A becomes a test with an expected code. This needs an expectation mechanism, because
    the tier-1 check of DEC R4-M17 compares the two paths.
  - Each family adds a scaling check.
- **Corpus.**
  - From the end of step 1, the library and the milestones produce no A5 problems (DEC R4-M12, except an (a) row whose
    library fix lands in the same unit).
  - The diagnostic corpus and the SPEC examples produce exactly their expected codes.
  - A5 runs in shadow over every fixture source (1,180 `EmitFixture` and 321 `WriteFixture` calls), and the results
    are classified as each family lands. Rejections caused by the MIR-only SPEC changes (plan §9.2) or by PLAN10's X1
    and X2 then show early.
- **Test migration.** Each family migrates the tests that read old ownership internals as it lands (183
  `.Ownership.Bodies` reads in 78 files), so R6-check changes no test unannounced.
- **Difference ledger,** kept as information. Until classified (a), (c) or (d) with a SPEC clause, these rows block
  the R4 exit:
  - the old path rejects and MIR accepts (possible unsoundness), except the MIR-only relaxations of plan §9.2, which
    are (d);
  - rejections of library or milestone programs.

  Differences of location or code choice do not block. A5 differences fall under PLAN10 Appendix B X3 (D4), so DEC O3
  fires only for library or milestone rejections and SETTLED reopenings.
- **Metrics replacing P9:** families completed; rules expressed (audit table); rule tests passing; open blocking rows;
  precision losses listed and decided (plan §5.3).
- **Stop rule replacing O5.** The owner is consulted after two consecutive working sessions on this plan that land no
  unit (fact or rule). A planning- or record-only session lands nothing.
- **Performance.** The bound of plan §4.6 holds. The R4 exit records "MIR build + A1–A6" time against the old analyses
  (PLAN10 P3).

## 9. Process changes

### 9.1 R5 in parallel

- **Basis.** R5 (monomorphization, shims, layout, code generation) reads A1 and A2 (`MirDropTable`, DEC R4-M6), not
  A5's problems; A5 only checks. ARCH's dependency rules already keep the backend from reading borrow-checker state.
  R5 shares Binding facts with A5 (C2, C8 and the R5 rows of plan §7).
- **R5 is a rewrite of generation, not a port.**
  - Emission is 95 files and 23,252 lines, including 2,335 lines of `.ll.in` runtimes. It lowers the old ownership IR:
    about 850 `Ownership*` references, with `OwnershipBody` on 144 lines in 39 files.
  - (H) About 9,100 lines do not couple to the old IR, Syntax or Binding: the module writer, the runtimes, the
    toolchain, `FunctionAbi` and the constant pool. Even these consume `EmissionModule`.
  - 53 `CompilerFunctionKind`s with a Body disposition are lowered inline in `BodyLowering.*` until R8.
  - Every generic instance is re-analysed through `AnalyzeInstance`.
- **R5 step 0:**
  - an audit table mapping each function kind, `IntrinsicId` and shim to its old lowering site and its new owner,
    and each Binding method Emission calls to the published fact that replaces it (plan §7);
  - two codegen designs, both measured: MIR into the existing `EmissionModule` with a named removal stage, or a
    direct `FunctionCodegen` with the helper templates moved verbatim.
- **Start conditions:**
  - the pre-R5 timing baselines are taken (ARCH R0 row; see "Timing" below);
  - the MIR fact contracts have landed (plan §6.2);
  - the behavior-preserving restructuring of `MirAnalysis` (DEC R4-M2a) and `MirDump` has landed. It moves from the
    R4 exit tasks (Appendix B) to before the split;
  - with their first consumers in R5, not as stubs (DEC R4-M2a):
    - the shim and instance key: `MirDefKey` with an Origin-erased Subst, with `ClosureContext` normalized through
      `TyCtxt.SubstOf` (this pulls an R7 item forward; `MirBodyKind` has no Shim yet);
    - A3b's slot in the pass order;
    - a codegen selector, removed at R6-gen;
  - file ownership is split: A5 owns its `Mir/Analysis` files, R5 owns its own;
  - changes to shared MIR files (`MirProgram`, `MirBuilder`, `MirVocabulary`, `MirDescriptors`, `MirValidator`,
    `MirDump`) are serialized between the two streams.
- **Instance collection is part of acceptance.**
  - §8.15.3 checks layout obligations for the whole checked set. By §23.2.2, an accepted program never fails a build
    with a Language or Proof error from an instance.
  - So one collector runs over the checked set before acceptance, with a "generated" column. A3b and the
    instance-stage capability check are its passes.
  - This moves a responsibility between phases, so it gets a DEC entry (OD4), and ARCH's pipeline table is corrected
    to match.
- **Generated-code requirements,** each with a test:
  - Closure environments of 8 bytes or less stay inline. §7.7.4 leaves the count to the implementation, so only P2
    protects it.
  - The interpolation stack buffer is used up to 1,024 bytes (`FormattingStackLimit` in `BodyLowering.Interpolation`).
  - Zero-sized values, ABI result slots and per-layout Array helpers.
  - The rc/arc release order of [SHARED_OBJECT_RUNTIME](../../docs/dev/SHARED_OBJECT_RUNTIME.md), checked by
    `SharedObjectRuntimeTest`.
  - Allocation audits count in the runtime adapter and fail closed. Today `NativeAllocationAudit` reads IR text and
    skips a missing transfer anchor.
- **Evidence.**
  - First, the IR assertions are classified (1,180 `EmitFixture` calls check naming, `select` and `store i1`; about 189
    other IR assertions):
    - required by SPEC or IMPL: kept, and both pipelines satisfy them;
    - behavior proxies: moved to native observation or the MIR dump;
    - incidental to the old Emission: deleted by owner decision.
  - R5's evidence has five parts:
    1. the legacy `.ll` is unchanged (D5);
    2. the MIR `.ll` is equal across R5's restructuring commits;
    3. per fixture, stdout, stderr and the exit code are equal across pipelines;
    4. the audit counts;
    5. per-function metrics after `opt -O2`, used to pick the fixtures to time.
  - Worktree paths are normalized in compared `.ll`.
- **Timing.**
  - A fixed O2 corpus of scaled kernels: Array, Dictionary, interpolation, iterators, erased calls, virtual dispatch
    and rc/arc. Runs alternate A/B/A, and their variance is recorded (P4).
  - Generic-heavy compiler inputs, and a collect-plus-codegen stage with 2× and 4× scaling.
  - No milestone uses `virtual` or `override`, so one is added.
- **R6 in two stages** (OD10):
  - **R6-gen** right after R5: MIR code generation becomes the default, and the old Emission is deleted.
  - **R6-check** after R4 and R6-gen: MIR decides acceptance, and the old Analysis is deleted. It must follow R6-gen,
    because the old Emission reads `OwnershipBody` and calls `AnalyzeInstance` (`LlvmEmitter`), and because MIR
    acceptance needs R5's instance collector.
  - Until R6-check, `AnalyzeInstance` stays as a check-only step. (H) The old acceptance relies on instance
    re-analysis for abstract destruction (`OwnershipAnalysis.Cleanup`).
  - The removals ARCH assigns to R6 stay in R6: Emission-only ones at R6-gen, the rest at R6-check. The Koto
    forwarding properties ("Temporary structures"; the `[Transitional("R6")]` marks) go at R6-check, including
    Binding's own reads of them (about 1,000).
- This supersedes PLAN10 §12.1 ("R3–R6 run alone"), §12.2's R5 start condition and the single R6 switch.

### 9.2 SPEC changes go to MIR only

- Rules changed by the 2026-10-15 rewrite (S1015) and by later SPEC changes, including the simplifications of plan
  §5.2 and §5.3, are implemented on MIR only. Place Result Removal already landed on both paths (DEC PR-1).
- An old-path acceptance that can produce wrong code is a miscompile and is still fixed under D5, for example S1015's
  §15.6.4 item 1 (`leak()`, a dangling reference).
- Their ledger rows are classified (d) by SPEC clause. This needs a SPEC-clause key in the classification table
  (DEC R4-M16), a tooling change.
- Tests of the old behavior are tagged. STATUS states that the default pipeline lags the SPEC until R6-check.
- **Departure from AGENTS "Diagnostics":** until R6-check the default pipeline may report an old-path Language error
  for a form the SPEC now accepts. This is part of OD5.

### 9.3 Review weight

| Unit | AGENTS (DEC Q5) | This plan |
| --- | --- | --- |
| Fact schema, reference representation, evaluator | Two independent designs, design review, final review | Same |
| A Binding fact view (crosses phases) | Two independent designs, design review, final review | One design and one review when the view's contract is fixed by the approved schema |
| A rule unit inside the schema | One review per change | One review per family, covering its rule units |
| A documentation-only checkpoint | One review | No independent review |
| The R4 exit, before R6-check | — | A final end-to-end review of A5 and A6 (plan §6.2) |

- Records stay short: commit messages of at most about 1,000–2,000 characters. AGENTS already gives PLAN_HISTORY no
  per-session entry, so this is not an exception.
- Every entry in the "This plan" column that differs from AGENTS is an exception and needs the owner's approval (OD6).
- After two review rounds on one family of findings, the owner chooses a representation or a hold; the whitelist option
  is excluded by OD1.

## 10. Owner decisions

1. **OD1.** Adopt the complete approach (no whitelist) against the rules as simplified, and the order of plan §6. If
   a needed SPEC change is rejected, leaving that rule Unsupported is an owner exception to OD1 (DEC O4).
2. **OD2.** Publish Binding facts before A5, moving them out of R7's scope (plan §7).
3. **OD3.** Replace the R4 exit, P9, O5 and G-Diff's blocking rule with plan §8, and suspend DEC R4-M12 until the
   joint core lands (plan §6.2).
4. **OD4.** Start R5 in parallel under the conditions of plan §9.1: its step 0, the restructuring before the split,
   the shared fact table of plan §7, and instance collection before acceptance (a phase move).
5. **OD5.** Implement SPEC changes on MIR only, including the Diagnostics departure until R6-check (plan §9.2).
6. **OD6.** Lighten review and commit records as in plan §9.3.
7. **OD7.** Standing rule: each rule the audit marks "not finitely expressible" gets its own SPEC proposal; DEC O3 and
   O4 apply.
8. **OD8.** If M39 keeps a compile-time Case terminator: a new MIR terminator (DEC O1) and a scoped change of R4-M19.
   **Not adopted (2026-10-17):** no Case terminator; case dependence stays per view (R4-M19).
9. **OD9.** If M39 keeps template calls for every omitted default: this replaces PLAN10 §5.6.2's inline fold
   (`MirBuilder.Defaults`) and reopens the SETTLED entry "Instance-evaluated generic defaults" (DEC O3). The
   "closed defaults" candidate of plan §5.2 is the alternative. **Not adopted (2026-10-17):** the inline fold and the
   SETTLED entry stay, and closed defaults are not taken.
10. **OD10.** Split R6 into R6-gen (after R5) and R6-check (after R4 and R6-gen), and keep `AnalyzeInstance` as a
    check-only step until R6-check (plan §9.1). **Adopted (2026-10-17).**
11. **OD11.** Confirm that a complete write through a reference ends none of the referent's earlier Loans, as S1015
    narrowed §15.6 (S4, plan §5.2). **Confirmed (2026-10-17).**
12. **OD12.** Policy, at adoption: simple, then fast, then precise, never trading soundness. Precision losses are
    allowed only as rule clauses decided as plan §5.3 states. This is the owner's request for the resulting SPEC
    revisions.
13. **OD13.** Approve each list of precision losses as one decision: first after step 0's designs, then whenever a
    rule unit reports a new loss (plan §5.3). The first list was approved on 2026-10-17
    (`draft/Sketches/A5 Step 0/design-A3.md` §9): P1, P3, P4, P10 and P11, the optional P5, P6 and P9, and S1's
    `within` kill; the root-most kill and relaxation (c) were not adopted.

## 11. Risks

- **A needed SPEC change is rejected.** That family cannot complete without an owner exception (OD1).
- **The audit itself stalls.** It is time-boxed per hard item, and the prototypes in `draft/Sketches` show progress.
- **A long fact phase shows little A5 progress.** The metrics and stop rule of plan §8 make it visible.
- **Fact relations grow beyond V16.** The design gate of plan §4.6 requires the bound before units start.
- **"Immutable after Bind" conflicts with Binding's late mutations** (certificates, CheckBound; OBS13 §2, DEC R4-M32).
  Each fact unit must name and remove such mutations for its fact.
- **Library or milestone programs are rejected,** because the old path had unsound acceptances (`bfb049c4`). These go
  to DEC O3.
- **Facts built before R7 need rework in R7.** They are built in R7's target shape.
- **R5 in parallel** risks conflicts in shared MIR files, and M39 may change MIR's shape while R5 is being written.
- **R5 is scoped as a port** and stalls as R4 did. Its step 0 audit table and two measured designs make the size
  visible first (plan §9.1).
- **One fact gets two producers,** or the backend reads Binding internals. The shared fact table of plan §7 prevents
  this.
- **`check` and `build` disagree** if instance collection runs after acceptance (plan §9.1).
- **IR-shape assertions hide behavior differences** or get weakened. They are classified before codegen (plan §9.1).
- **Allocation regressions go unnoticed** while the audits fail open. The audits fail closed first (plan §9.1).
- **Tests diverge per pipeline,** and old-reject/MIR-accept rows add triage load.
- **The default pipeline lags the SPEC until R6-check** (plan §9.2). STATUS says so.
- **The precision-loss list grows** until it rejects too much, or a "simple" design turns out unsound. The census
  counts each loss, OD13 approves each list, and the prototype tests soundness first (plan §5.3).

## 12. Records on adoption

- **DEC:** one owner entry for OD1–OD7, OD10 and OD12. It names the superseded parts of PLAN10 (§12.1, §12.2's R4 exit
  and R5 start condition, §12.3, §13.1 G-Diff, §18 P9, the single R6 switch) and of DEC R4-M4, R4-M5, R4-M12
  (suspended until the joint core lands), R4-M16, R4-M17 and O5. It states the default for every other "R6" in DEC
  and ARCH: an Emission-only item means R6-gen, any other means R6-check (for example DEC R4-M1, R4-M3a, R4-M13a and
  R4-M17, "R6 unit a"). The fact schema and the instance-collection phase move get their entries in the commits that
  first implement them.
- **ARCH:** the R4, R5, R6 (two stages) and R7 rows; the pipeline table (instance collection before acceptance); the
  difference-ledger rule; the D5 freeze ("until R6 completes": Emission until R6-gen, Analysis until R6-check) and the
  Analysis records row; the A5 canonical row when it lands.
- **SPEC proposals:** S1, S2 and S5 each get a proposal under OD7. S3 and S4 go to the owner as clarifications. Each
  approved list of precision losses becomes one revision (OD13).
- **PLAN §3:** one line pointing to this plan.
- **INTEGRATED:** the adoption record.
- **VERIFICATION:** if the ledger or metric tooling changes.
- **AGENTS:** the Review-weight text, if OD6 is adopted.
- **PLAN10:** unchanged; its integrated scope is frozen.

## Appendix A. SPEC rules A5 decides

| # | Rule | SPEC | Family |
| --- | --- | --- | --- |
| 1 | Static storage keys are Place roots; program points | §15.6 | F1, F5 |
| 2 | Loans are carried by values; after a call, by results and writable storage per the public contract | §15.6 | F1, F3 |
| 3 | Liveness is a may-analysis with a least fixed point and includes destruction; regions never keep a Loan live | §15.6, §15.6.1 | F1 |
| 4 | Replacing a holder ends its Loans; a partial replacement ends that part's; Move Path granularity | §15.6 | F1 |
| 5 | A write through a reference, non-path index or dynamic container adds Loans and ends none | §15.6 | F1, F8 |
| 6 | A joined result carries every Loan (may-analysis) | §15.6 (example after §14.9.2) | F1 |
| 7 | Origin relations by variance; declared and substituted relations; well-formedness per case | §15.6.1 | F4 |
| 8 | Place overlap and conflict tables; P3's path overlap across Derefs; Loan provenance for followed references, used for content (flows and liveness) | §15.6.2, §3.4 | F1 |
| 9 | Reborrow child Loans; containment up to the first shared layer; region splitting; Iterator descent | §15.6.3, §15.5 | F2, F3, F4 |
| 10 | Results kept across calls on the same receiver; `preserves results` (S2) | §15.6.3, §8.11.3–§8.11.6 | F6 |
| 11 | Call steps 1–5; retention by the instantiated public contract | §15.6.4 | F3 |
| 12 | Effect summaries against live Loans; mutable-static anchors | §15.6.4 | F5 |
| 13 | Judging relations: fixed, finite and inferred Origins; chains; Refuted and Unknown | §15.6.5 | F4 |
| 14 | Destruction observation | §15.6.6 | F1 |
| 15 | Call reservations: two-phase, activation, abandonment | §15.6.7 | F3 |
| 16 | Whole-value updates: swap non-overlap; storage update dependencies | §15.7.2, §15.7.3 | F8 |
| 17 | Closure dependencies; an inferred result is a fixed Origin | §15.8.2 | F7 |
| 18 | Escape and retention | §15.8.3 | F7 |
| 19 | Origin inference; no candidate means a local region | §15.3.6 | F4 |
| 20 | Nested functions' fixed Origins; a Loan ends at last use | §15.3.4 | F1, F4 |
| 21 | A match subject's Loan follows its bindings; guard subject protection | §15.1.6, §14.8.3 | F8 |
| 22 | Unreachable code: conflicts in the assumed state; abandoned comparisons end their Loans | §14.10.3 | F10 |
| 23 | Temporaries end at their full expression; a lent reference-holding temporary is a Loan root | §3.4.2, §3.6 | F1 |
| 24 | `@raw` checks like an immediately ending `@ref`; a raw borrow has a fresh anchor | §5.2.2, §5.4 | F1 |
| 25 | Compound assignment and increment protect their destination | §13.7.2 | F8 |
| 26 | Default inspection of reserved inputs | §7.2.3, §15.6.7 | F9 |
| 27 | Comparison inspection Loans: a built-in comparison of a Non-Copy owned Place forms a shared Loan, live while the right operand is evaluated | §13.4 | F1 |
| 28 | Follow and pair layers: a borrow reached through a handle holds a Loan on that handle; a pair layer depends on the path `p` in the `owner` case and on `o` and the parent Loan, not the slot, in the `ref` and `uniq` cases | §13.5.5, §13.5.6 | F2 |
| 29 | Handle destruction compares only the handle Place; erased destruction has unknown effects | §16.3.3 | F1, F5 |
| 30 | Collection operations: the receiver is reserved; dependencies join and are never subtracted; `tryGet` adds the storage Loan | §4.9.6 | F3, F8 |

## Appendix B. Items from the withdrawn plan

**Hard items:**

| Item | Content | Status on 2026-10-16 |
| --- | --- | --- |
| C3 | Retained transfers; positional prepared-input transfer; repeated-site parent/child Loans | Held. The repeated-site part needs S1; the rest is expressible as written (plan §5.1) |
| C5 | Boundary results, correlated completion, rooted recursive effects | Held. Needs S2, and S5 for growing effect atoms (plan §5.2) |
| E5 | Exact per-`require` unavailable frontier | Held; terminal-cut policy reviewed. Expressible; the bounded producer needs a DIAGNOSTICS change only (plan §5.2) |
| C2 / A5-closure | Closure capture, receiver and result contract | Held; finding removed with U7 (DEC R4-M33). Expressible with C2 (plan §5.1) |
| Q1-ref | Complete replacement through a reference kept the referent's earlier Loans | Specified behavior since §15.6 (2026-10-15); not an A5 rule. S4 confirms it (OD11) |
| M39 | One acquired-Place outcome per case; template calls for defaults | Design reviewed; not implemented (OD8, OD9) |
| C1, C8 | Contextual inputs and premises; selected Callable signature | C1a reverted; C1b unlanded |

**Other items and their disposition:**

| Item | Disposition |
| --- | --- |
| U8a (secured results, cleanup order) | F3 |
| ReferenceRead | F1, with the reference representation |
| U8s (safe range acquisition) | F8 |
| U8p (Place and backing dependency) | Binding fact (plan §7) |
| Abstract and tuple comparison limits | F1 (comparison inspection Loans) |
| U10 (test migration, DEC R4-M13a ratchets) | Each family migrates its tests |
| U11 (A6 facts, derived and default policy, adapter) | A6 |
| U12 (driver structure, measurement, exit census) | Step 0 and the R4 exit |
| E9 (non-A5 ledger causes: the known MIR fixed-array Aggregate failure, A1–A4 open (b) rows) | Separate repair units before the R4 exit |
| `MirAnalysis.cs` restructuring (DEC R4-M2a) and the `MirDump` restructuring due at its next change | Before the R5 split (plan §9.1) |
