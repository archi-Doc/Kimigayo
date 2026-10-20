# 2e-D package: A5-F9, defaults and inline copies (ER-defaults, the copy disposition, PLAN G171)

Copied from the R4 session's scratch on 2026-10-21; references to design-A.md, design-B.md, review.md, package.md and scratch probe folders name files kept outside the repository.

Consolidated by designer A on 2026-10-21, on the planning session's instruction. It takes the design review's (`review.md`)
per-item recommendation: design A is the base, folded with the items the review takes from design B. No repository file was
edited.

Base: `origin/dev` `851e755b`. The review checked `84a21a2b`; no MIR, MirBuilder or MirTypes file differs between the two.

Sources:
- `design-A.md` (no blocking defect);
- `design-B.md` (four blocking defects, B-1 to B-4, each fixed by A's form);
- `review.md` with its scratch run `rv\run\proto.cs`: 11 reviewer cases and the variant `R20EntryA`.

The package goes to the owner once (R4-M74 OQ2). Its approval makes it the owner-approved design of G:A5-F9 (DEC Q6 form 2) for
units 2e-1 to 2e-5. Units 2e-0 and 2x-P13 take their own reviews (PD29).

G:A5-F9 merges into dev after its group review passes, with one more condition: **AR-9 merged**, the `MirValidator` rule that a
copy is entered only at its first location (§5.7).

## 0. Decisions

| # | Decision | Origin | Unit |
| --- | --- | --- | --- |
| D1 | **Slot step (A3 E17 at adoption; E15 is R4-M72, E16 is R4-M73).** In a default template, a Deref directly on a `PreparedInput` root is `StepClass.Inline`: the slot itself. It is one row of `MirBorrows.Walk`, decided by the local's role. Copies are unchanged: their `p_i` are the prologue's temps, role None, whose Deref stays Shared | A, B | 2e-2 |
| D2 | **Slot-rooted Place.** A Place is slot-rooted when it is rooted at a PreparedInput, passes the slot step, and has no Raw step after it (SPEC 15.6.2: a raw Place borrow has its own fresh anchor; 5.2.1 authority). Both R26 rules read this one definition, the prototype's `Rooted` | A (B-3) | 2e-2 |
| D3 | **R26-borrow.** At each Return of a template, a slot-rooted Loan in the closure of σ_in(_0) ∪ σ_in(E) is `DefaultArgumentBorrow_Kd` at the Loan's site. This includes a slot borrow stored into storage the body does not own (PD15 (iv)) | A, B | 2e-2 |
| D4 | **R26-access.** (1) A write to a slot-rooted Place is `DefaultArgumentAccess_Kd` at the write. The checks' `Accessing` visitor finds it before its borrowed-root filter. (2) An exclusive-mode slot-rooted Loan whose issue the replay reached, and which R26-borrow did not report, is `DefaultArgumentAccess_Kd` at its site. Moves stay A1's | A (B-4) | 2e-2 |
| D5 | **View skip as one pre-pass flag.** The view is skipped unless it has an input of some Loan-stratum producer: a Loan, a guard candidate, a slot write, or 2b-3's static Drop if 2b-D adds one. Each producer's pre-pass scan sets the flag. 2e-2 converts R4-M72's skip to this flag, or adds its term if 2b-3 already introduced the flag, and records it in its DEC entry | review §2.2 | 2e-2 |
| D6 | **Anchor fact = the parameter (PD15 (v), PD31).** The builder publishes each PreparedInput local's Source as its parameter's declaration. Today it is the function's (`MirBuilder.Argument` → `Info(this.declaration)`) | A (B-2) | 2e-0 |
| D7 | **One recorder with the hold.** `Reports.Record(code, requirement, subject, facts)` applies R4-M73's hold once. The Loan-conflict `Report` builds its facts and calls it, and so do the R26 rules. R26 records carry no witness fact | A | 2e-2 |
| D8 | **P9 rows.** A template call's rows are Content(input i) → Result for every input, untied, with no store rows | A, B | 2e-1 |
| D9 | **Raw row and r3.** A slot whose case Type is `MirWrites.Raw` (rule G's raw clause) also stores every input's Content into E. **r3:** a slot whose case Type `HoldsRaw` but is not `MirWrites.Raw`, at a call whose inputs' Contents hold a Loan, makes the template call a located `Unsupported_Kd`. r3 is registered under PD28; its removal stage is G160's representation | A (B-1) | 2e-1 |
| D10 | **R-a.** Before an omitting call's plan and checks, the Loans of the Move Path subtrees of the pending slots it moves end, in the forward solve and the replay. Every argument of an omitting call is a moved pending slot, so these are the call's inspection Loans. R4-M70's default taint is deleted: for template calls in 2e-1, for copies in 2e-3 | B's root set | 2e-1, 2e-3 |
| D11 | **Copy disposition (G171): R20Own + R20Entry.** A Loan-based failure is its template's when the deepest Inlined scope containing its participating scopes exists, after moving outward while its Loan or reservation is carried into that scope's entry. Such a candidate is skipped before selection. Every other failure is the reporting body's | A, B | 2e-4 |
| D12 | **err(ended)** has no entry test: γ_H outlives an evaluation only after its template's own err(ended). B's γ test adds a record (dc20r-loop-guard, run) | A | 2e-4 |
| D13 | **Record identity.** A Loan problem a body owns whose access lies inside a copy takes as subject the omitting call of the outermost copy (SPEC 15.6, 23.1.1). One fact kind, `Evaluated`, names the default's operation | A, B | 2e-4 |
| D14 | **Issue location.** `Loan.Location` (the issuing statement's or terminator's location) replaces `Loan.Site`. It also serves an R3 Loan issued at a Drop | A (name) | 2e-4 |
| D15 | **A1/A2 ownership.** One rule for every recorder (producer omission). It lands in 2e-4 if 2x-R7 and a `MirDrops.cs` response have landed when 2e-4 starts; then `MirFactKind.Inlined` and `MirReport.Add`'s drop go. Otherwise it is staged to 2g-6, recorded in PLAN | B's timing, A's fallback | 2e-4 or 2g-6 |
| D16 | **dc-reserved-rec is valid.** An activation is not checked against its own reservation's Loan, whichever value carries it. Owner question OQ-F9-1 | A, B | 2e-1 |

## 1. Required meaning (SPEC)

- **7.2.3.**
  - Explicit arguments go into pending slots. Each omitted default is evaluated once, in parameter order, in the declaration's scope.
  - A default may Copy a preceding Copy value, or "inspect it through temporary shared access (also a reserved exclusive input,
    without its exclusive access)". It "cannot Move or modify that slot".
  - "Its result cannot keep a new Borrow or Reborrow of a preceding argument, but may Copy an existing shared borrow."
  - "Inspection Loans end before callee entry."
  - A recursive default's value carries the Loans of every preceding argument.
  - A default is checked at its declaration like a generic body. "Beyond the call's ordinary argument and Loan checks", it can
    make the call invalid only through its effects.
- **15.6.**
  - "The Borrows of a default evaluated at a call are identified by that call and the default's operation."
  - Every execution of one site forms the same Loan.
  - A Borrow "is checked against an earlier execution of its own site that such a value carries".
  - Storage the body does not own "may be used after every point".
- **15.6.2–15.6.3.** A raw Place borrow is rooted at its fresh anchor. A Borrow past a shared dereference forms no Loan.
- **15.6.4 and 15.6.7.** Effects and reservations are checked where they occur. Defaults may inspect reserved inputs through
  shared Loans that end before entry. An activation is checked against the values live into its call, other than its own
  argument.
- **23.1.1–23.1.2.**
  - Unsupported is reported "at the concrete use, including an instantiation".
  - Each independent failed requirement is reported. Grouping, and whether derived failures are reported, are
    implementation-defined.
- **DIAGNOSTICS, Defaults paragraph.** A default's own failure is recorded once, by its declaration's check. "The calls that omit
  it add no other record."
- **S1015 sections.** This family implements §7.2.3's A5 parts and adds MIR tests for §15.6.4 (a recursive default's retention;
  default operations and effects against the call's Loans, with 2b-3) and §15.6.7 (inspection of reserved inputs, R-a).
  §16.2.1's cleanup of pending slots is A2's and stays unchanged.

## 2. Template rules (2e-0, 2e-2)

**Design chain.**
- Meaning: inside a default, a preceding parameter is the caller's slot, which the default may only inspect, shared. The
  default's own failures are mutating the slot, borrowing it exclusively, and keeping a new Borrow of it in the result or in
  storage the body does not own.
- Gap: the template's `p_i: ref/P_i` is a shared reference. So a Borrow of the slot forms no Loan (E3), and a write through it
  meets nothing. Round 19 finding 4: nine template errors are accepted under the shared step.
- Owner: `MirBorrows`, which classifies steps and issues Loans.
- Unnecessary afterwards: R4-M72's "no Loan-stratum producer" bullet. The old path stays the only reporter on the default
  pipeline until R6-check.

**Rules.**
- **D1, E17.** `Walk` gains one row: a Deref of a PreparedInput root is Inline. The review checked the side effects; all lie on
  error paths only, since they concern writes to the slot itself, which R26-access rejects:
  - `Flow.Store` joins a slot write into `p_i` instead of E;
  - `Liveness.Through` stops counting such a write as a use;
  - `Ending` treats `p_i.@` as an inline path.

  A PreparedInput has no storage end: `Argument` registers no Drop for `ref/P`. Reads through the slot step give {Φ_E}, through
  which E's state is reached, equal to the old shared read.
  PD15 (i): `y: ref/i32 during a = x@follow@ref` is `&(*(*p_x))`, steps [Inline, Shared], so it forms no Loan and is valid.
- **D3, R26-borrow.** In the checks replay of a template view, at each Return:
  - one gather is seeded by `_0`'s holder and E, using the checks' Based pass and closure;
  - every slot-rooted Loan found is reported once per Loan per view, and marked for D4 (2).
  σ(E) is load-bearing (round 19 finding 5: dt-raw-borrow, dt-twice).
- **D4, R26-access.**
  - (1) is a hook in `Accessing.Place` before the `borrowed` filter, so it also sees Call destinations.
  - (2) runs after the replay, over the exclusive slot-rooted Loans whose issuing location was replayed.
  - One code per failure: dt-sem-u gets Borrow only.
  - Raw writes are excluded by D2: dt-raw-copy is valid.
  - The Loan-free dt-fb (`x@follow = 9`) still runs the stratum, by D5.
- **D6, D7, facts and records.**
  - Subject: the Loan's site (D3, D4 (2)) or the write (D4 (1)).
  - Fact: `Anchor`, the parameter.
  - Requirement: `Ownership.DefaultArgumentBorrow` or `Ownership.DefaultArgumentAccess`.
  - Recorded through `Record`, so a held template's R26 failures are located `Unsupported_Kd`.
  - R26 sites always lie in the template's own scope region: a nested copy reaches the slots only through its prologue temps,
    whose Deref is Shared.

**Precedence.**

| Pair | Rule | Basis |
| --- | --- | --- |
| R26-borrow vs R26-access | R26-access skips a Loan that R26-borrow reported | Round 19 finding 6; DIAGNOSTICS "explained once by the result diagnostic" |
| R26 vs `DefaultArgumentMove_Kd` | Moves are A1's | R4-M69 |
| R26 vs ordinary Loan conflicts in the template | Independent; both kept (dt-sem-local) | SPEC 23.1.2 |
| R26-borrow vs the Origin stratum (E12, PD11) | 2g-5 drops an Origin-stratum problem whose finite chain end is a slot-rooted Borrow that R26-borrow reported in the same body and view. F9 publishes the Borrow's site as subject, the view bits and the body. 2a's MIR Origin stratum reads E17 to treat such a Borrow as body-local at the parameter root (an input to 2a0-D) | PD11; SPEC 15.6.5 |

**Rejected for D1:** a builder representation of slots as non-references. It changes the template's parameters, which R5's
generator calls for recursive defaults, and A1's Moves of Args. It is a W1 change made only for an A5 classification.

## 3. Template calls (2e-1)

**Design chain.**
- Meaning: a recursive default's value carries every preceding argument's Loans (7.2.3). The default cannot write through slots
  (D4), but may write by raw authority (5.2.1).
- Gap: the unknown-callee rows (R4-M57, R4-M63, `Calls.Rows`' template branch). They carry Value of every input into the result
  and into exclusive inputs, with a quadratic scratch, plus R4-M70's taint.
- Owner: `MirBorrows.Calls`.
- Unnecessary afterwards: that branch, and `Flow.Placed`'s template-call disjunct.

**Rules.**
- **D8.** One row (input i, Content, Result) per input; untied; no store rows. The inputs are the prologue temps over the slots
  (`MirBuilder.Defaults.cs:129`), so Content(input i) = σ(slot i), the arguments' Loans. Round 19 finding 3: with or without R-a,
  no verdict changes against the Value rows, so 2e-1 lands before 2e-3.
- **D9, raw row.** Round 19's raw row was given per case. Its derived condition is rule G's raw clause, `MirWrites.Raw` on slot
  i's case Type; such a call also stores Content(every input) into E. Round 19's dc-raw-hid slot is `ref/RawBox`, whose raw
  pointer lies past a shared reference and beside a Loan (G160 (a), (c)). Neither `MirWrites.Raw` nor the walk's `RawLoan` bit
  holds for it. The walk stops at shared references, and past an exclusive one it records `Write`, not `RawLoan`
  (`MirTypes.Holding.cs:151–199`). That is B-1. Options:
  - (r1) nothing: dc-raw-hid is accepted, which is unsound;
  - (r2) a raw row whenever `HoldsRaw`: Language errors on valid rc and Loan-free raw slots, which AGENTS forbids;
  - **(r3, chosen)** when a slot's case Type `HoldsRaw` and is not `MirWrites.Raw`, and the union of the inputs' Contents holds a
    Loan, the template call is a located `Unsupported_Kd` at the call, with `Ownership.Unsupported`. It is recorded from
    `Calls.Call` when `checks` is set, that is in the checks replay, never in the forward solve. It is not recorded in
    `Checks.Check`, so 2e-1 changes `Calls.cs` only and does not count for `Checks.cs`. It needs no `MirTypes` change and no hold on
    `MirTypes.Holding.cs`.

  r3 over-covers rc handles and Loan-free raw pointees beside a Loan-carrying input. It is registered under PD28, with G160's
  representation as its removal stage. The explicit twin dc-raw-hid-x stays G160 (a). A recursive default storing into a
  static's raw pointee gets no P9 store, as with every call; that is noted for G160's design (review §2.3).
- **Taint.** 2e-1 deletes `Placed`'s template-call disjunct. Copies keep the taint until 2e-3.

## 4. R-a and the taint's removal (2e-3)

**Design chain.**
- Meaning: "Inspection Loans end before callee entry" (7.2.3, 15.6.7).
- Gap: the prologue's shared borrows (`MirBuilder.Defaults.Defaults`) are ordinary caller-scope Loans that nothing ends. So a
  copy's value derived from one meets the call's Move of the slot (dc-p15). R4-M70's taint makes that `Unsupported_Kd`, and its
  join imprecision turns real Language errors into `Unsupported_Kd` (dc-j1, dc-joined, dc-copied).
- Owner: A5's endings (`MirBorrows.Endings`, E8's owner).
- Unnecessary afterwards: the whole default taint.

**Rule (D10).**
- `MirBuilder.Call` routes every explicit argument of an omitting call through `Pending` (`MirBuilder.Calls.cs:87`, `:105`),
  which returns `Move(slot)`. A reserved argument's reservation is formed in, or moved into, its slot. Each default's value is
  `Move(slot)` (`MirBuilder.Defaults.cs:142`). So every argument of an omitting call is a moved PendingSlot, and the Loans rooted
  at those slots are exactly the call's inspection Loans.
- For a Call that moves a PendingSlot s, the Loans of s's Move Path subtree (E8's `Subtree`/`Range`, one run per slot) end before
  the call's plan and checks, in the forward solve and the replay.
- Representation: one set per block, since the Call is its block's terminator. It sits beside E8's per-location `ended` and is
  applied in `Carry` before `Plan`.
- The call's rows read `Move(slot_i)`, whose states never hold their own inspection Loans. A valid copy's value keeps σ(slot)
  through `Issued`'s parents, so a copied existing borrow keeps its caller Loan (dc-copied, dc-fd). A value that keeps a caller
  Loan only through a removed inspection Loan comes only from an invalid template, which R26 reports (dc-p15).

**Compared** (round 19):
- R-a: chosen; sound, with one record per failure;
- `R19Taint`: `Unsupported_Kd` for Language errors, and records at invalid copies' calls;
- `R19InlinedDrop`: unsound on dc-j1, dc-joined and dc-copied;
- `R19NoEnding`: second records.
A builder `EndBorrow` cannot express R-a: E8 ends only Loans rooted at what is destroyed.

**Deleted:**
- `Marker.Taint` and `MarkerOf`'s PendingSlot row;
- `Flow.Placed` and its calls in `Flow.Plan` and `Calls.Call` (`Calls.cs:69`);
- `Candidates`' second `Tags`, `Taint` and the scan's taint;
- `Checks.Conflict`'s tainted fallback;
- `Reports.Report`'s taint branch;
- the DIAGNOSTICS taint row.

PLAN G136 N7 closes. `ADefaultsTaintMakesItsConflictsUnsupported` is rewritten into `MirBorrowsTest.Defaults` with its cases' SPEC
verdicts. The reason is R4-M70's own removal stage.

## 5. The copy disposition (2e-4, PLAN G171)

### 5.1 Chain

- **Meaning.** A template owns a failure that every evaluation of it repeats: a conflict whose Loan, reservation or γ, and whose
  operation, belong to one evaluation of it (7.2.3). Anything else meets a Loan issued outside that evaluation, and is the call's
  ordinary Loan check (15.6). That covers Loans issued by the caller, by an enclosing default, by a sibling default, or by an
  earlier evaluation of the same default at the same site.
- **Gap.**
  - R4-M69 decides by the access's scope alone. Round 20 found it unsound in 14 callers and 2 templates.
  - `Loan.Site` loses the issue location.
  - Selection precedes ownership, so a template-owned first candidate hides a caller-owned later one.
  - Scopes name sites, not evaluations, so a Loan from an earlier evaluation shares the access's issue scope (round 20 finding 2).
- **Owner.** `MirBorrows` decides which candidate an operation reports, before it selects one. `MirBody` owns the scope-tree
  queries.
- **Unnecessary afterwards.**
  - Removed: A5's `Inlined` fact, `Report`'s access-scope `Inlining` call, and the `held && inlined < 0` exception.
  - Superseded in DEC: R4-M73's sub-bullet "R4-M69 governs inline default copies".
  - With D15 in 2e-4: `MirFactKind.Inlined` and `MirReport.Add`'s drop.

### 5.2 Options compared

| Option | Verdict |
| --- | --- |
| R4-M69 (today) | Unsound (G171; 16 failures in round 20) |
| R20Own alone | Unsound on loops: dc20-loop, -loop-nested; the reviewer's dc20r-loop-e, -loop-fx, -loop-slot, -loop-closure, -loop-nested-entry (run) |
| **R20Own + R20Entry, err(ended) without an entry test (chosen)** | `R20EntryA` equals the sound-by-construction oracle `R20Cert` on all 71 round-20 cases; the 506 earlier cases are unchanged (review, run) |
| R20Entry with an entry test on γ (B) | Gives an extra caller record on dc20r-loop-guard (run) |
| Copy-on-a-cycle rule | Sound, but gives extra records for template-owned failures in loops, and needs an SCC query |
| Per-evaluation Loan twins (the change of shared structure) | Equal decisions, but adds a new `MirLoanSets` operation, Loans ×(1 + depth), and renames at every copy entry in every solve |
| Template certificates (B's option C) | Sound by construction, but cross-body: driver order, builder rows, views mapped across bodies. Kept as the tests' oracle (§5.6) |
| Copies issue no Loans | Unsound: a copy's own reservation is needed for its activation against the caller's shared Loans. It also contradicts 15.6's identity of default Borrows |
| Template validity (drop when the template has a problem) | Hides independent caller failures. It is a suppression after the fact |

### 5.3 Rule

| Failure (producer) | Participating scopes | Entry test on |
| --- | --- | --- |
| Access conflict (`Check` → `Conflict`) | the access; the candidate Loan's issue | the Loan |
| Activation and err(destroy) (`Activations`, `:73`, `:101`) | the call; the reservation's issue; the Loan's issue | the Loan and the reservation |
| err(effect) (2b-3, at a Call or a Drop) | its site; the Loan's issue | the Loan |
| R3 (2b-D) | the Drop, and its Loan if 2b-D issues one there | none: such a Loan is issued and met at the Drop |
| err(ended) (`Ended`) | the `EndBorrow`; the guard local's declaration | none (D12) |

- Issue scope = `Scope(Loan.Location)` (D14). `MirBody.Common(a, b)` walks `Outer` (MirValidator guarantees Outer < index), and
  the boundary is B = `Inlining(Common(P))`.
- **Entry(B).** At B's entry, before `Plan` and `Advance`, one gather runs on the state before the location. It uses the checks'
  Based pass from E and the holders live into the location (the replay's live bits, which `Enter` sets at a chain head), closed as
  a check set closes. Entry(B) is the set of Loans found whose issue lies in B, one `MirLoanSets` set per Inlined scope and view.
  - Nested copies entered at one location share the gather.
  - A copy that issues no Loan gets no gather; a pre-pass count per scope decides this.
  - **Implementation note** (review): compute each Inlined scope's entry location once in the pre-pass, as its first location in
    the replay's reverse postorder, rather than an `Inlining` walk per replayed location.
  - The entry is unique: `Inline` maps the template's block 0, which no edge targets, to the caller's current block, and Returns
    become Gotos out. So the entry dominates B, and the replay meets it first.
- **Covered(scope, loan[, reservation]).**
  1. B := `Inlining(Common(scope, issue))`.
  2. While B ≥ 0 and a tested element ∈ Entry(B), B := `Inlining(Outer(B))`.
  3. The failure is covered iff B ≥ 0.
- **Selection.** `Conflict` gains the operation's scope: the access's, or for an activation or err(destroy),
  `Common(access, issue(reservation))`. A conflicting candidate that is `Covered` is skipped and the scan continues, so a covered
  first candidate never hides an owned later one (dc20-first, -first-rev). An operation outside every copy
  (`Inlining(scope) < 0`) skips the test.
- **err(ended).** `Ended` records nothing when `Inlining(Common(EndBorrow scope, guard scope)) ≥ 0`.
- **err(effect).** 2b-3's candidate selection goes through `Covered` and records through `Report`. Until 2e-4, a copy's
  err(effect) is dropped by R4-M69's fact, as today: shadow only, no regression (PLAN G171).
- **Report.** Owned failures go through `Report` → `Record` with the hold applied uniformly, and with D13's subject. Limit
  records (`Unsupported(at)`: an unclassified destruction, `CallRetention.Limit`) are unchanged: one per body, at the operation
  (R4-M69, R4-M71). Their location is 2g-4b's.
- **Placement.** The predicate and the entry sets go in `MirBorrows.Defaults.cs`. `Common` and `OmittingCall` go in `MirBody`,
  beside `Inlining`.

### 5.4 Record identity (D13)

**Subject.** For a Loan problem a body owns (Language, or a held view's `Unsupported_Kd`) whose access lies in a copy, the subject
is `MirBody.OmittingCall(scope)`. That is the Source of the outermost Inlined scope containing the access: the call, in the
reporting body's own source. Two calls give two subjects in `MirReport` and at publication, with no key change, so
dc20-twice-both gives two records. The record names the caller's site, not a library default's.

**Fact.** One `MirFactKind`, `Evaluated`, names the default's operation (the access).
- If D15 lands in 2e-4, `Inlined` is deleted in the same unit, so the kind count is unchanged.
- If D15 is staged, `Inlined` stays as A1/A2's drop flag until 2g-6, and `Evaluated` is the only kind for the default's operation.
- No consumer outside A5 reads `MirFactKind` today. R5's H3 renders `Evaluated` (an R5 ask, §7).

**Residue, for U11.** One record per (code, call). This merges:
- two independent failures with one code at different operations of one copy;
- a relocated failure with the omitting call's own failure of the same code.

SPEC 23.1.2 makes grouping implementation-defined. U11's Reason key can add `Evaluated`. PD17 places primary locations at 2g-4;
2g-4b keeps this rule, or replaces it with one that keeps two calls apart.

### 5.5 A1/A2 ownership (D15)

A1's and A2's problems in a copy concern the copy's operations on its own locals and through its prologue temps. The template
performs the same operations on its locals and PreparedInputs (R4-M69's `DefaultArgumentMove_Kd`). They compare no issued Loan, so
`Inlining(location) ≥ 0` is exact for them, loops included: a local read before it is written is A1's error in the template too.
One concept, one form: both producers omit such a problem, `MirReport.Add` drops nothing, and `MirFactKind.Inlined` leaves.

**Condition.** The move lands in 2e-4 if two responses have landed when 2e-4 starts:
- 2x-R7, the restructure of `MirInit.cs` (G94, G94 follow-up, 1c-1, 1c-6, 1c-8; its only prerequisite is 1c-9, so it can run in a
  lane now);
- a `MirDrops.cs` response: a ruling for its two-line edit, or a split. It has had five changes since `bc77e504`.

Otherwise D15 is staged to 2g-6, behind both responses. PLAN records two ownership forms, with that removal condition. The
decision is taken by the PD21 count at 2e-4's start.

### 5.6 Soundness (the M69 plan §4's proof obligation)

**Claim.** Every failure that D11 omits is reported, relative to its boundary B, by B's template T_B in the view the copy's case
selects, as a Language problem or a located `Unsupported_Kd`, or T_B carries another located record that blocks acceptance. Every
failure D11 does not omit is a failure of the reporting body.

- **(a) Writes and reads across the copy boundary** (B's Lemma W).
  - A copy reaches caller state only through these: the prologue temps (fresh per call; reading through them gives σ(slot i));
    statics and E; raw pointers read from slots, which lead to E; and its own pending slot D. D is dead at the entry: it is
    written in B, and the previous call moved it.
  - The copy writes no caller holder except D and E. Its writes through temps pass a shared layer, which R26-access rejects in
    T_B. Its calls' store rows reach holders only past exclusive or raw layers (rule G).
- **(b) Entry lemma** (B's Lemma S).
  - A Loan issued in B from an earlier evaluation can reach a check set inside B only through a holder live into B's entry, E,
    or the closure; the entry gather takes exactly those. A B-local read before it is written is live at the entry.
  - Over-approximation only moves failures to the caller. The extra record that can result needs E8 inside B to end the stale Loan
    first. That requires a copy-local borrow escaping its evaluation, which makes the template invalid.
- **(c) Simulation half.** Fix one evaluation of B, an access a in B, and T_B's view ν.
  - **Correspondence** (B's I1). The copy is T_B folded, one to one: copy locals ↔ T_B's locals; D ↔ `_0`; temps ↔
    PreparedInputs; Returns ↔ Gotos. The copy's Drops are a subset of T_B's.
  - **Loans.** The copy's B-issued Loans are a subset of T_B's under ν: `ByCase` matches ν, and E17 only adds Loans in T_B.
  - **States.** T_B's states contain the copy's, restricted to this evaluation's B-issued Loans: they flow among the same locals,
    `_0` and E.
  - **Liveness** corresponds inside B.
  - **Calls.** Retention and A4's keys under T_B's abstract instantiation contain the instance's (H; SPEC 15.6.4: "an unproven
    absence of retention never permits access"; 8.15).
  - So T_B's check set at a contains the copy's, and T_B reports a problem at a's counterpart, perhaps with another first
    candidate. Nested copies follow by induction on depth.
- **(d) Lifecycle half.** A template the rule relies on is analyzed in every view, or it carries a located record that rejects:
  - a copy exists only for a template that is not recursive, did not fail (no Opaque value, no findings), and stays under the
    expansion limit;
  - A5's arena gives `OwnershipStorageLimit_Kd`; A1/A2's truncation is `ViewsOf`; P13's hold gives `Unsupported_Kd`;
    `CallRetention.Limit` and the destruction bound give located `Unsupported_Kd`;
  - at the case bound there is no template;
  - a skipped view has no Loan, so the copy issues none in it;
  - R3's omission relies on the template's view being run, which D5's flag and 2b-D's static-Drop term give (an interface
    condition of 2e-4).
  Coverage needs participants from the current evaluation, never the template's status.
- **(e) A moved-out failure is genuine.** A stale Loan in a caller holder, slot or E that is live into B is live after any access
  in B. It is unique there, so whenever the current evaluation's Loan conflicts, the stale one does too (15.6: "an earlier
  execution of its own site").
- **(f) err(ended).** γ_H survives an evaluation only in a value live after H's `EndBorrow` in it, which is the template's own
  err(ended). So D12 needs no entry test.
- **(g) Executable form.**
  - `-r20` re-run: R20Entry is sound on the 60 round-20 cases, and the 506 earlier cases are unchanged.
  - The reviewer's 11 cases (7 callers, 4 template bodies): R20EntryA equals R20Cert on all 71. R20Own is unsound on the 5 new
    loop cases.
  - 2e-4's tests make this the compiler's form: every omission test asserts the template's record (the R20Cert property; B's
    practice).

### 5.7 Interfaces

- **G148's hold (R4-M73).** The hold reclassifies only owned failures. Covered candidates are omitted before it. A held template
  reports `Unsupported_Kd` for its own failures. A held caller view reports its owned failures as `Unsupported_Kd` at the
  omitting call (dc20-held).
- **2b-D (ER-effects).**
  - err(effect) selects through `Covered` and records through `Report`.
  - R3's Loan, if 2b-D issues one, has its Drop's terminator as `Loan.Location`.
  - The static-Drop skip term joins D5's flag.
- **Future producers** (2a-2's merge, 2f's Φ_c) select through `Covered` and record through `Report`/`Record`.
- **Builder invariant.** B's correspondence and unique entry rest on `Inline`'s invariants: a copy is entered only at its first
  location, and the template's block 0 has no predecessor. Dev's `Inline` keeps both, but nothing validates them. If the
  template's block 0 had a predecessor, Entry(B) would over-include (extra records only). If a copy were entered elsewhere than its
  first location, Entry(B) would miss the Loans that edge carries, and an omission could be unsound. So AR-9's `MirValidator` rule
  is a prerequisite of G:A5-F9's dev merge (ask AR-9, §7; R5 has agreed).

### 5.8 Deletions (2e-4)

- `Report`'s `Inlining` call, its Inlined fact and its held exception;
- the Inlined clauses in the contracts of `MirBorrows.cs` and `Reports.cs`, and also of `MirReport.cs` and `MirProblem.cs` when
  D15 lands in 2e-4;
- `Loan.Site`, replaced by `Location`;
- DIAGNOSTICS' A5 `Inlined` sentence;
- in DEC: R4-M69's A5 part and R4-M73's sub-bullet; with D15, also R4-M69's A1/A2 part and R4-M10's Inlined sentence.

## 6. SPEC reading: dc-reserved-rec (D16)

Case: `rr(v@uniq, w@ref, 3)` with a recursive default.
- P9's Content row puts σ(slot_v), which holds v's reservation Loan R_v, into the default's value. There R_v is unique.
- At the call, v's activation meets only shared-mode Loans (R4-M68), so the MIR accepts after 2e-1. Today it reports `U` at the
  template call.
- 15.6's "a Borrow is not checked against the Loan it forms" covers this if the activation counts as the reservation's delayed
  Borrow. But 15.6.7's "checked against the values live into its call other than its own argument", read alone, names the
  default's value.
- A valid recursive default cannot alias v (7.2.3), so R_v in its value comes only from P9's letter.
- No interim (PD30) is needed: the MIR already accepts. This is owner question OQ-F9-1.

## 7. Planning-session decisions and asks

| ID | Decision or ask |
| --- | --- |
| Agreements (2026-10-21) | BU agreed to PQ-1's split of 2b-6. R5 agreed to three things: 2e-0 as R4's own builder unit after 2x-E2, with the sel check at its merge; AR-9 in R5's W1; and H3 rendering `Evaluated` |
| PQ-1 | **Ask BU to split 2b-6** (agreed). 2b-6's qualified part lands as planned. Its unqualified-in-group and implicit-receiver part lands after G:A5-F9's dev merge. 2e-4's caller-owned witnesses (dc20-g171, -fx, -n1, -sib, -twice, -twice-both, -first, -gen, -held, -loop) then fail before and pass after on normal source, as AGENTS requires for a defect fix. That part of 2b-6 re-expects them as Binding's `Unsupported_Kd`, stating its reason. PLAN keeps a P42 row to restore them. The default pipeline loses nothing by the delay: its old Analysis reports `Unsupported_Kd` at those static accesses |
| R5 asks (agreed) | (i) 2e-0 is R4's own builder unit after 2x-E2, not on lane C, with the sel check at its merge; (ii) **AR-9**, in R5's W1: a `MirValidator` rule that an Inlined scope's locations are entered only at its first location, and that a template's block 0 has no predecessor. It is a prerequisite of G:A5-F9's dev merge (§5.7); (iii) H3's rendering of the `Evaluated` fact, with D13's subject |
| E17 | The slot step is numbered A3 §12 E17 at adoption, in 2e-2's DEC entry |
| r3 | Registered under PD28 (a STATUS row with its SPEC form; G160's representation as the removal stage). It reaches the owner with OQ9's list at X-2 |
| D5 | 2e-2 makes the view skip one pre-pass flag, or adds its term if 2b-3 made it first, recorded in that unit's DEC entry (non-blocking) |
| D15 | A1/A2 in 2e-4 if 2x-R7 and a `MirDrops.cs` response have landed at its start, else 2g-6 |
| Hotspots (PD21; counted at each unit's start) | `MirBorrows.Calls.cs`: 1c-7, G148, 2e-1; 2e-3 removes the `Placed` call, the fourth change, so **2x-R3 lands before 2e-3**. That moves it ahead of 2a-2, its prerequisite in the step 2 plan, which PLAN §3 records (PD1). `MirBorrows.Activations.cs`: due; **a PD21 ruling for `Activations.cs` at 2e-4's start** (124 lines, one responsibility; 2e-4 adds one argument at `:73` and `:101`). 2x-R9 keeps its 2f-D condition. 2e-1 does not touch `MirBorrows.Checks.cs` (r3 is recorded from `Calls.Call`, D9). `MirBorrows.Checks.cs`: three changes since 1c-4; after 2b-3's response (2b-D's proposed 2x-R13), 2e-2, 2e-3 and 2e-4 make 2e-4 the fourth, so a response is due before 2e-4. `MirBorrows.Reports.cs`: 2b-3, 2e-2, 2e-3, so a ruling or split before 2e-4 (a split if `Record` makes two responsibilities). `MirProblem.cs`: three since 1c-4, so a ruling (50 lines) before 2e-4. `MirInit.cs`, `MirDrops.cs`: D15's condition. `MirBody.cs` (sh): a hold line for 2e-4. `MirBuilder.cs` and goldens (W1): 2e-0, R4's own builder unit after 2x-E2, in the W1 slot R5 agreed to |
| 2x-P13 | The optional draft round (2x-P12 is 2b-D's Transferable round). It folds the reviewer's 11 scratch cases and `R20EntryA` (`rv\run\proto.cs`), adds dt-raw-ref, dc-raw-hid under r3 with a valid HoldsRaw twin, dc20-twice-both's two records, and a stale Loan that E8 ends inside a copy. Earlier cases unchanged. Run before 2e-4 if the planning session wants tracked evidence |

## 8. Cost

**Work terms (A3 6.2's counter):**
- 2e-1: rows per template call equal the inputs (plus raw rows), so linear; the scratch shrinks from N(N+1) to N + 1.
- 2e-2: per template view, one gather per Return (one check's cost), one compare per Deref walked, and one pass over the Loans
  after the replay.
- 2e-3: per omitting call, one memoized `Remove` per holder in each solve pass and in the replay (E8's dense term). It deletes the
  taint's two scope walks per pending-slot store and the second tag set.
- 2e-4: per Loan-issuing copy per view, one gather in the replay; per conflicting candidate inside a copy, O(depth) walks and a
  scan of Entry(B); nothing outside copies.

**V16 shapes** (VERIFICATION rows added by their units; Work at 2× and 4×):
- "F9 template calls": N preceding parameters × M calls, Work ∝ M·N;
- "F9 prepared inputs": N inputs borrowed into `_0`, Work ∝ N;
- "F9 omitting calls": N omitting calls, Work ∝ N·holders;
- "F9 copies": N Loan-issuing copies in a loop at depth D, linear in N for fixed D.

**Memory.**
- Per view: one set id per block (R-a), and one set id and one stamp per Inlined scope.
- `Loan.Location` (4 bytes) replaces `Site` (8 bytes).
- No per-head state, so `MirDataflow.Prepare`'s arena term is unchanged.
- Pools are reused, so warm runs stay at 0 B.

**Speed (H):** no new solve. Corpus Work changes within ±1 %, measured in each unit's commit.

**Size (H):**
- Code about +200/−135 over 2e-0 to 2e-4. That includes D15's +4/−20 in 2e-4, which moves to 2g-6 if staged.
- Tests about +550.

## 9. Records

- **DECISIONS** (written by the units):
  - ER-defaults: P9, the raw row and r3 (2e-1); E17, R26, `Record` and D5's flag (2e-2); R-a (2e-3).
  - Template ownership with entries, D13 (`Evaluated`, the subject) and D14 (`Loan.Location`) (2e-4).
  - D15's outcome; OQ-F9-1's answer.
  - Superseded: R4-M57's and R4-M63's "until F9"; R4-M70's taint; R4-M72's Defaults bullet; R4-M69's Inlined rule (A5 in 2e-4,
    A1/A2 with D15) and R4-M10's Inlined sentence; R4-M73's sub-bullet.
  - Rejected options: §5.2, §3 (r1, r2), §4.
  - The package approval names G:A5-F9's units 2e-1 to 2e-5.
- **DIAGNOSTICS:**
  - Defaults paragraph: the MIR's producers; Borrow also covers stores into storage the body does not own; the parameter fact.
  - MIR Loan problems: copies (template-owned failures omitted; owned ones at the omitting call with `Evaluated`); the taint row
    deleted; F9's two rows and r3's row added; the :307 "no producer" paragraph replaced; `Inlined` stays as A1/A2's flag until
    D15 lands.
- **ARCH:** the Loan stratum row; MIR storage (`MirBody.Common`, `OmittingCall`); MirReport's row.
- **PLAN:**
  - G171 closed by 2e-4, with the P42 row from PQ-1;
  - G160 (a)/(c) gain the template-call shape and r3;
  - G136 N7 closed;
  - D15's staging, if it applies;
  - U11's one-record-per-call residue;
  - OQ-F9-1;
  - §3 (PD1): 2x-R3 moves ahead of 2a-2 to before 2e-3; 2e-0 runs off lane C after 2x-E2; AR-9 joins G:A5-F9's merge condition.
- **STATUS:** the defaults row and r3. **CODEMAP:** `MirBorrows.Defaults.cs`.

## 10. Units

Order on lane C (PD1, after 2b-3):

2e-1 → 2e-2 → 2x-R3 → 2e-3 → [a PD21 ruling for `Activations.cs`; the `Reports.cs`, `Checks.cs` and `MirProblem.cs` responses;
D15's responses if ready; 2x-P13, optional] → 2e-4 → 2e-5.

- 2x-R3 moves ahead of 2a-2, its prerequisite in the step 2 plan; PLAN §3 records the move.
- 2e-0 is not on lane C. It is R4's own builder unit after 2x-E2, and only 2e-2 depends on it.
- The group merges into dev after its review passes and AR-9 has merged (§5.7).

Common rules for every unit:
- Tests go in the new `T/MirBorrowsTest.Defaults.cs`, at the MIR-problem layer, and each fails before its unit.
- In 2e-4, every omission test asserts the template's record.

| ID | Unit | Change | Prerequisites | Verification | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2e-0 | PreparedInput sites (R4's own builder unit, not on lane C) | The template prologue gives each PreparedInput local its parameter's declaration as Source (`MirBuilder.Body`); the goldens with defaults are updated | 2e-D approved; 2x-E2 (R5's agreement) | Comp: `MirBuildTest` (a template's `p_i` names its parameter); `MirDumpTest`; goldens; the sel check at its merge; +cen | +3/−1, t+15, goldens | R (PD29) | W1 (R5's agreed slot), sh |
| 2e-1 | P9 template-call rows | D8, D9 (raw row, r3 registered), D16's verdict; deletes the unknown-callee branch and `Placed`'s template disjunct; DEC (supersedes R4-M57's and R4-M63's "until F9", and R4-M70 for template calls) | 2e-D; 2b-3 (chain) | Comp: `RecursiveDefaultTest`'s Generic f(4, 7), Closure and Reference give none; dc-ref-live errs at `n = 4` (today U); dc-p06 none (today U); dc-reserved-rec none (today U); dc-raw-rec errs; a direct raw write through a raw-only slot errs; dc-raw-hid is r3's U at the template call; a valid HoldsRaw twin gets r3's U; +V16 "F9 template calls"; +warm; +cen; +led. Done when a5-defaults and u-defaults have 0 rows | +16/−30, t+80 | G:A5-F9 | no |
| 2e-2 | PreparedInput rules | D1 (E17), D2–D7: the `Walk` row; new `MirBorrows.Defaults.cs` with its contract; R26-borrow at Returns; R26-access hook and post-replay pass; D5's flag; `Record`; requirements and the Anchor fact; DIAGNOSTICS Defaults (:109) and :307 amended (PD15 (iv)); DEC (E17, R26; supersedes R4-M72's Defaults bullet) | 2e-1; 2e-0 | Comp: `DefaultAccessTest` :25–:32 (Access ×2, Borrow ×6; :28 valid, PD15 (i)), :16–:20 and :44 valid; `SemanticsDefaultTest` ExclusiveCase (one code); audit-calls row 26; SPEC 7.2.3's `label` and `erased` valid; dt-cap-ref valid and dt-cap-uniq Access; dt-raw-copy valid; dt-raw-borrow Borrow; dt-twice Borrow and Access; dt-nested valid and dt-nested-bad Borrow; dt-raw-ref valid; dt-fb (Loan-free) Access; the Anchor fact (PD31); a held template's R26 is U; +V16 "F9 prepared inputs"; +warm; +cen; +led. Done when a5-old-default-access and -borrow have 0 blocking rows | +80/−8, t+150 | G:A5-F9 | no |
| 2e-3 | R-a; the taint deleted | D10; §4's deletions; DEC (R-a; supersedes R4-M70's taint, its role-table row and R4-M73's default-taint records); G136 N7 closed | 2e-2; 2x-R3 | Comp: dc-p15 gives only the template's Borrow (today U at the call); dc-reserved-bad and dc-gen2-bad-u the same; dc-j1 and dc-j1-clean error at `x = 2` (today U); dc-joined and dc-copied error at `k = 2` (today U); dc-reserved-inspect, dc-fe, dc-p13 and dc-fa valid; the inspection Loan's root is a PendingSlot in a reserved-receiver call (`ReceiverDefaultTest.ReservedMethod` shape); a copied existing borrow keeps its caller Loan; err(destroy)'s dz pair; `ADefaultsTaintMakesItsConflictsUnsupported` rewritten; +V16 "F9 omitting calls"; +warm; +cen; +led. Done when a grep finds no taint marker and no default-slot `Unsupported_Kd` | +20/−55, t+90/−70 | G:A5-F9 | no |
| 2e-4 | Copy ownership (G171) | D11–D14, and D15 if its condition holds: `Loan.Location`; `MirBody.Common` and `OmittingCall`; `Covered` and the entry sets in `MirBorrows.Defaults.cs`; `Conflict`'s scope argument (callers in `Checks.cs` and `Activations.cs`); `Ended`'s test; err(effect)'s selection; the entry gather in `Carry`; D13's subject and `Evaluated`; §5.8's deletions; DIAGNOSTICS; ARCH rows; DEC (supersedes R4-M69 for A5, and for A1/A2 with D15, and R4-M73's sub-bullet); G171 closed | 2e-3; 2b-3 merged; PQ-1's split; a PD21 ruling for `Activations.cs` at its start (2x-R9 keeps its 2f-D condition); `Reports.cs`, `Checks.cs` and `MirProblem.cs` responses; 2x-R7 and a `MirDrops.cs` response for D15; for the group's dev merge, AR-9 merged | Comp, normal source, each failing before. Caller-owned: dc20-g171 (error at the call; `Evaluated` and `Reservation` facts) with its disjoint, read-only and explicit twins; dc20-fx; -n1; -sib; -twice; -twice-both (two records at two calls); -first and -first-rev with reordered declarations; -indep; -guard-across; -gen-u and -gen-r; -held (U at the call); -limit (Limit U kept at the operation); dc20-loop and the reviewer's loop-e, -fx, -slot, -closure and -nested-entry. Template-owned, omitted in the caller with the template's record asserted: `ADefaultsProblemsAreItsTemplates`' f, g and h; -n2, -n4, -n5; -loop-own (also with local Loans only); -loop-nested; -guard-in; dc20r-loop-guard (one record); -r3 after 2b-3. Also `AnInlinedProblemIsDropped` replaced with D15; +V16 "F9 copies"; +warm (nested copies in a loop); +cen; +led | +75/−40, t+200 | G:A5-F9 | sh (`MirBody.cs`) |
| 2e-5 | A5-F9 records, ledger close | M39's Binding part void (†, OQ6); ARCH's R4 row, PLAN §3, CODEMAP, PLAN (G171, G160, G136 N7, D15, U11's residue, OQ-F9-1), STATUS (defaults row, r3), `classification.tsv`, 7.2.3's target sides, OldRule on `DefaultAccessTest` :28, `Landed += F9`, X-2 registration of r3 | 2e-1 to 2e-4; OQ6 | Doc + Evid: +led. Done when e0ffa5a60934, 9fcd0c360b5d, b29228117a54 and dc7ace954713 pair and the rows 2x-T2 assigned are gone | doc, tsv ±8 | G:A5-F9 | no |
| 2x-P13 | Round 21 (optional draft) | §7's cases; `R20EntryA` tracked | 2x-P11 | Draft (no unsound acceptance, earlier cases unchanged) | ~+200 draft | R (in 2e-D's record) | no |
| 2g-6 (amended, only if D15 is staged) | A1/A2 producer omission | A1 and A2 omit problems located in a copy; `MirFactKind.Inlined` and `MirReport.Add`'s drop deleted; `AnInlinedProblemIsDropped` replaced | 2x-R7; `MirDrops.cs` response | Comp: `MirInitTest`, `MirDropTest`, `MirAnalysisTest`; verdicts unchanged | +4/−20 | G:A6 | no |

## 11. Owner questions (with OQ2's package approval)

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-F9-1 | dc-reserved-rec: is an activation checked against its own reservation's Loan when a recursive default's value carries it (SPEC 15.6, 15.6.7, 7.2.3)? | (a) No: valid. The MIR already accepts (R4-M68), and a clarifying 15.6.7 sentence is added at the next SPEC session. (b) Yes: an error. That needs a new A5 rule and, until SPEC states it, PD30's `Unsupported_Kd`. (c) A located `Unsupported_Kd` until decided | **(a)**: a valid recursive default cannot alias the input (7.2.3); only P9's letter carries the Loan |
| OQ-F9-2 | Confirm the departures from owner-approved texts as one list (OQ6's pattern): (1) R4-M69's Inlined rule, which also amended R4-M10's Inlined sentence, is superseded for A5 in 2e-4, and for A1/A2 in 2e-4 or 2g-6 (D15); (2) R4-M73's sub-bullet "R4-M69 governs inline default copies" is superseded: the hold applies to every owned failure; (3) R4-M72's Defaults bullet and its premise are superseded by F9's producers and the slot step (E17), as R4-M72 itself plans; (4) R4-M72's A6 table gains a row: a Loan problem a body owns whose access lies in an inline default copy is recorded at the outermost omitting call, with the default's operation as its `Evaluated` fact (SPEC 15.6, 23.1.1, 23.1.2); PD17's 2g-4b may revisit primary locations | (a) confirm all; (b) per item | **(a)**. The M69 plan's exclusion of relocation is not in force (R4-M86), so it needs no confirmation |

## 12. Still unverified

- **Monotonicity under instantiation** (H): of type facts, `TyCtxt.Retains` and A4's keys (§5.6 (c)). Mitigations: every omission
  test asserts the template's record, and a generic template whose instance gains a row.
- **Binding acceptance** of the loop shapes (H): raw stores of static borrows in defaults, unqualified and implicit-receiver group
  borrows. If PQ-1 is refused, the caller-owned branch keeps only MIR-layer and template-owned witnesses until P42.
- **dt-raw-ref** is valid in the Origin stratum (H).
- **r3's corpus count** is 0 (H), checked by +cen.
- **The builder invariants** for the unique entry are not validated until AR-9. A copy entered elsewhere than its first location
  would make Entry(B) miss that edge's Loans, and an omission could be unsound. So "AR-9 merged" is part of G:A5-F9's dev-merge
  condition. A block 0 with a predecessor would give extra records only.
- **The P2 entry order** relies on reverse postorder over CheckTarget edges (H). A copy after a diverging call tests it.
- **E17's `Ending` effect** on slot writes (error paths only) has not been run.
- **Relocation (D13)** changes the subjects of ledger rows; these are non-blocking location differences, unmeasured.
- **The prototype does not model** the compiler's entry detection, `Loan.Location`, R26's interaction with a Move of a borrowed
  slot, or D13's records. R20Cert and the reviewer's cases are scratch-only until 2x-P13.
- **No measurements:** every size, speed and V16 term is (H).
