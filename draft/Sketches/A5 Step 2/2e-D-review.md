# 2e-D independent design review: A5-F9 (defaults and inline copies, PLAN G171)

I wrote neither design. Base: `origin/dev` `84a21a2b`. No MIR, MirBuilder or MirTypes file has changed since the designs' base
`851e755b`; I checked this with `git diff --stat` after 2b-S5 and bu11 landed. I worked read-only and ran no builds or test suites.

I read AGENTS (Implementation Design, Independent review), `s2-design-common.md`, `brief.md`, `design-A.md`, `design-B.md` and
B's `b-proto`. I also read these inputs:
- the step 2 plan: PD1, PD11, PD15, PD17, PD21, PD28–PD31, OQ6, OQ8, OQ9, §3.7 and §4.7;
- `prototype.md` rounds 19 and 20;
- DECISIONS R4-M68–R4-M73;
- PLAN G100, G148, G160 and G171;
- the M69 plan §§4–5, with INTEGRATED's 2026-10-21 corrections;
- DIAGNOSTICS: Defaults and MIR Loan problems;
- SPEC 7.2.3, 15.6 and 15.6.7 (activation), and 23.1.2.

On dev I read this code: `MirBorrows.*`, `MirReport.cs`, `MirProblem.cs`, `MirBody.cs`, `MirBuilder.cs`, `.Calls.cs`,
`.Defaults.cs`, `.Defaults.Copies.cs`, `.Acquisitions.cs` (`Borrow`), `MirTypes.Holding.cs`, `MirInit.cs`, `MirDrops.cs` and
`MirDataflow.cs` (Replay).

Evidence I produced is in the scratch folder only:
- `rv\run\proto.cs`: B's `R20Cert` copy plus 11 reviewer cases (4 template bodies, 7 callers) and one variant, `R20EntryA`;
- outputs `rv\r20rev.txt` and `rv\r20rev-v.txt`;
- the full default run, byte-equal to the tracked prototype's. Its exit code is 26, the S1′ baseline, for both copies.

## 0. Verdict

| Item | A | B | Recommendation |
| --- | --- | --- | --- |
| Slot step (E15) | One `Walk` row by role; notes E15/E16 are taken | Same row, named E15 | Same rule. Number it at adoption: A3 E15 is R4-M72 and E16 is R4-M73, so it becomes E17 |
| R26-borrow | Slot-rooted, stopping at the first Raw step (A2) | Any Loan rooted at a PreparedInput | **A**. B as written over-reports (B-3) |
| R26-access | Writes are found in `Accessing` before its borrowed-root filter. The view skip also requires no slot write | Collected in the pre-pass and reported after the replay. The skip is not mentioned | **A**. B as written misses dt-fb (B-4) |
| R26 hold | `Record(code, requirement, subject, facts)` split from `Report` | `Report` gains a 6th parameter | **A** (§2.2) |
| P9 rows | Content → Result per input, untied, no store rows | Same | Same |
| Raw pointer | Raw row on `MirWrites.Raw`. r3: located `Unsupported_Kd` when a slot `HoldsRaw` but is not `MirWrites.Raw` and the inputs carry a Loan | Raw row, plus `Unsupported_Kd` on the walk's `RawLoan` bit | **A (r3)**. B misses G160 (a)'s own shapes (B-1) |
| R-a | Pending-slot roots plus reservation-holder roots | PendingSlot roots only | **B**. A's second clause is dead code (§2.4) |
| Copy disposition | R20Own + R20Entry, with no entry test for err(ended) | R20Own + R20Entry, with an entry test on γ too | **The shared rule, with A's err(ended) row**. B's γ test adds an extra record (dc20r-loop-guard, run) |
| Soundness argument | §5.6 (a)–(g) | Lemmas W and S, I1/I2, R20Cert as the oracle | Both hold on the prototype (71 round-20 cases + 506 earlier) and under my code reading. Take B's oracle practice for 2e-4's tests |
| Record identity | Subject = outermost omitting call, new fact `Evaluated` | Same subject, `Inlined` redefined | Same rule. One fact kind (§2.6) |
| A1/A2 ownership | Producer omission, staged to 2g-6 | At once in 2e-4 | **B's timing** if 2x-R7 and a `MirDrops.cs` response precede 2e-4, else A's staging (§2.7) |
| Anchor fact | Builder unit 2e-0 makes each PreparedInput's Source its parameter | Assumes the local's declaration is the parameter | **A**. B's fact names the function (B-2) |
| Issue location | `Loan.Location` | `Loan.At` | Same concept; name it `Location` |
| dc-reserved-rec | OQ-F9-1 (a) | OQ-B1 (a) | (a) |
| Units / hotspots | Adds 2e-0, 2e-4, 2e-5 and 2x-P12; 2g-6 amended | Adds 2e-4 and 2e-5; PQ-2 and PQ-3 | A's list, with the hotspot additions of §2.10 |

- **A has no blocking defect.**
- **B has four blocking defects as written (§3).** Each has a short fold, and A already contains all four folds.
- **Recommended design:** A, with B's R-a root set, B's A1/A2 timing (conditional), B's PQ-1, and B's "each omission test asserts the
  template's record". §5 lists them.

## 1. The problem and the general rule

The relevant SPEC rules are 7.2.3, 15.6, 15.6.7, 15.6.2/15.6.3, 5.2.1 and 23.1.2.

- A default is its declaration's check. It is "evaluated at a call", and its Borrows are "identified by that call and the default's
  operation".
- An operation is checked against the Loans that the values live at that point carry. That includes "an earlier execution of its own
  site that such a value carries".

So the general rule has two halves.

1. **In the template**, the preceding slots are the caller's arguments, which the default may only inspect. That needs a step class
   that makes a slot-rooted Borrow a Loan of the template (E15), and two checks on it:
   - R26-borrow: the result and storage the body does not own keep no new Borrow of a slot;
   - R26-access: no write and no exclusive Loan through a slot.
   A raw pointer read from a slot leads to raw storage (15.6.2), not to the argument.
2. **At a call**, a failure belongs to the template exactly when every participant belongs to one evaluation of it. The participants
   are the access, the Loan, the reservation and the effect. Every other failure is the caller's ordinary Loan check. Scopes name
   sites, not evaluations, so "one evaluation" needs an evaluation fact. R20Entry's entry set is the cheapest exact-enough form of
   that fact: Loans issued in B that are live into B's entry are an earlier evaluation's.

A shorter or more general design? I tried three.

- **One ownership predicate for every recorder.** A1 and A2 have no Loans, so the predicate reduces to `Inlining(location) ≥ 0` for
  them. B's B7 states this. It is shorter only when A1 and A2 move in the same unit (§2.7).
- **Walking outward from the access's innermost copy while a participant lies outside or is stale.** This gives the same answer as
  `Inlining(Common(...))` followed by the entry loop, and costs the same scope walks. It is not shorter.
- **Certificates (B's option C), per-evaluation Loan ids, a cycle rule, and copies issuing no Loans** are each longer, cross-body or
  unsound. Both designs reject them; I agree, for the reasons in A §5.2 and B §2.3.

No candidate beats the shared rule. Its only free choice is the err(ended) row, and the evidence favours A's row (§2.5).

## 2. Item by item

### 2.1 Slot step (E15)

The rule is identical in both designs:

- A Deref directly on a `PreparedInput` root is `StepClass.Inline` (`MirBorrows.Places.cs` `Walk`).
- Copies map `p_i` to the prologue temps (role None), so a copy forms no slot Loan (`MirBuilder.Defaults.Copies.cs`).

I checked the code effects.

- **Effects confined to error paths.**
  - `Flow.Store` joins a slot write into the holder `p_i` instead of E.
  - `Liveness`' `Through` stops counting a slot write as a use.
  - E8's `Ending` treats `p_i.@` as an inline path, so `moves.Find` gives `p_i`'s path. This is A's open risk.
  - All three affect only writes to the slot itself, which are R26-access errors. A PreparedInput has no storage end:
    `Argument` registers a Drop only where `MayNeedDrop` holds, which it does not for `ref/P`. B's condition holds.
- **Equal behaviour.** Reads through the slot step give `σ(p_i) = {Φ_E}`, and content then gives σ(E). Under the old Shared step the
  read gave content(σ(p_i)). The outcome is the same.

Number: A3 §12 E15 is R4-M72 and E16 is R4-M73, as A notes. B and the brief call the new step "E15". Number it E17 at adoption.

### 2.2 R26-borrow and R26-access

- **Raw limit.**
  - The prototype's `PreparedRules.Rooted` excludes places past a raw pointer (`Case.RawAt`) from both rules.
  - A's A2 defines "slot-rooted" once, up to the first Raw step, and both rules read it.
  - B applies the raw limit to R26-access only. `Loans.Issue` forms a Loan past a Raw step, rooted at `p_i`. So under B, a default
    that returns a borrow of raw storage reached through a slot's raw pointer gets `DefaultArgumentBorrow_Kd`. That form is A's
    dt-raw-ref, valid by 15.6.2 (H on the Origin stratum). B-3.
- **View skip.**
  - `MirBorrows.Solve.cs:37` returns before the replay when a view has no Loan and no guard candidate (R4-M72).
  - dt-fb, `x@follow = 9`, has neither. A adds "no slot write" to the skip, found in the pre-pass.
  - B collects writes in the pre-pass and reports after the replay. The replay never runs for that view, so dt-fb is accepted on the
    MIR. B-4.
  - B's own 2e-2 list (`DefaultAccessTest` :26) would catch it.
  - B's pre-pass collection would also need Call destinations, which `Loans()` does not visit. A's `Accessing` hook gets them for
    free.
- **The skip as an open family.** 2b-D's design A adds a "static Drop" term to the same skip (its R3 rule). F9 adds "slot write".
  Recommendation: one pre-pass flag, "the view has an input of some Loan-stratum producer", set by each producer's pre-pass scan,
  instead of a growing conjunction. This is not blocking. Record it in 2e-2's DEC entry, or with 2b-D's unit if that lands first.
- **Hold.** Both designs route R26 through R4-M73's hold. B's 6th parameter does not fit, for two reasons.
  - (a) `Report` always adds a witness fact (`Use` or `Store`, `MirBorrows.Reports.cs:51–58`). R26's record would carry a fact that
    DIAGNOSTICS does not name; it names "the preceding prepared parameter" only.
  - (b) `Report` derives the subject from a `MirLocation`, but R26-borrow's subject is the Loan's site. A Loan has no location until
    2e-4.
  A's `Record(code, requirement, subject, facts)` keeps the hold in one place and avoids both problems.

### 2.3 P9 rows and the raw pointer

- **P9.** Content(input i) → Result is identical in both designs. The inputs are the prologue temps (`MirBuilder.Defaults.cs:129`),
  so Content(t_i) = σ(slot_i). The result is untied, which `Calls.Call` already handles.
- **B's raw `Unsupported_Kd` condition (B-1, unsound).**
  - B relies on the `Holds` walk's `RawLoan` bit for a value that also holds a Loan. The walk:
    - stops at a shared reference: `referent` is computed only for exclusive or uniq views (`MirTypes.Holding.cs:174–179`);
    - past an exclusive reference, walks a raw pointee in mode Exclusive+RawPointee, whose fact is `Write`, not `RawLoan` (`:151`,
      `:155`);
    - stops early once `Loan|Write` are found (`:182–199`).
  - So `RawLoan` is never found for round 19's dc-raw-hid slot, `b: ref/RawBox` (prototype `RawHidden`). Nor is it found for G160
    (a)'s own example, `storeRaw(b@uniq, …)`.
  - Those calls get neither the raw row nor the `Unsupported_Kd`. dc-raw-hid is accepted, as in `R19NoRawRow`. That is a regression
    from today's `Unsupported_Kd`.
- **A's r3.**
  - `MirTypes.HoldsRaw` walks components, referents included, so it is true for `ref/RawBox`, `uniq/RawBox`, and G160 (b)'s and (c)'s
    shapes.
  - r3 is a located `Unsupported_Kd`, never a Language error. It needs no `MirTypes` change and no shared hold on
    `MirTypes.Holding.cs`.
  - It over-covers rc handles and Loan-free raw pointees beside a Loan-carrying input. That is a registered PD28 producer with G160's
    representation as its removal. Accept it.
- **Static raw route.** A recursive default that stores into a static's raw pointee (`State.rp[0] = x`) gets no P9 store. That is the
  same for every call. The Origin stratum (a static pointee's Origin) is the rule for it (H). It is not F9's; note it for G160's design.

### 2.4 R-a

- **The root set.**
  - `MirBuilder.Call` routes every explicit argument of an omitting call through `Pending` (`MirBuilder.Calls.cs:87`, `:105`), which
    returns `Move(slot)` (`MirBuilder.Defaults.cs:92`).
  - The prologue borrows `arguments[...].Place`, the slot (`:129`), and each default's value is `Move(slot)` (`:142`).
  - So every argument root of an omitting call is a PendingSlot, and a reserved argument's reservation is formed in, or moved into,
    its slot.
  - B's claim (H) is true by code reading. A's "or reservation holders" clause never adds a root. Drop it, as AGENTS' Short asks, and
    keep B's 2e-3 test asserting the root kind (`ReceiverDefaultTest.ReservedMethod` shape).
- **Timing.**
  - Both designs end the call's prologue Loans before its plan and checks, in the solve and in the replay. That is correct: the
    call's rows read `Move(slot_i)`, whose states never hold their own inspection Loans.
  - A value that keeps the caller's Loan only through a removed inspection Loan arises only for an invalid template, which R26 then
    reports (dc-p15). A valid copy reads through the shared layer, and `Issued`'s parents put σ(slot) into its value
    (dc-fd, dc-copied).

### 2.5 Copy disposition (G171): the soundness check and the attack

Both designs adopt R20Own + R20Entry, decided per candidate before selection. I re-derived the argument against the code.

- **Entry is unique.**
  - `Inline` maps the template's block 0 to the caller's current block. "The builder targets no entry block" (`Copies.cs`), and
    Returns become Gotos out.
  - So B's entry dominates B. The replay (`MirDataflow.Replay`: reached heads in reverse postorder) meets it first.
- **Live-in at the entry.**
  - `Enter(head)` loads the chain head's live-in, and `Advance` updates the bits after each location.
  - So "the replay's live bits before `Advance`" (A) is the live-in, including at a chain head, the case dc20-loop-own exercises.
  - The gather must happen before `Plan`/`Advance` on the state before the location, as A states.
- **No stale Loan escapes Entry.**
  - B names no caller holder except the prologue temps `t_i` (fresh per call), the slots (live until the call), statics and E (always
    in the gather), and D. D is dead at the entry: it is written in B and was moved at the previous call.
  - A B-local read before it is written is live at the entry.
  - Closure through direct Loans is taken at the entry, as a check set closes.
  - So a stale B-issued Loan that reaches a check set inside B is in Entry(B). The gather inherits exactly the check sets'
    completeness.
- **Over-approximation is only an extra record.**
  - A stale Loan in a caller holder or slot that is live into B is live after any access in B, so it is in that check set. The
    caller's record is a genuine 15.6 failure.
  - Only E8 inside B, ending a stale Loan rooted at a copy local, separates the two. That requires an escaping local borrow, which is
    an invalid template.
- **err(ended).**
  - γ_H survives an evaluation only in a value live after H's `EndBorrow` in that evaluation. The first such evaluation is its
    template's own err(ended). So A's row, with no entry test, is sound.
  - B's entry test on γ moves that failure to the caller, which then reports a second record beside the template's.

**Attack in a scratch copy** (B's `proto.cs` with R20Cert, 11 new cases (4 template bodies, 7 callers) and `R20EntryA` = R20Entry without the γ entry test):

| Case | Shape | SPEC | R20Own | R20Entry (B) | R20EntryA (A) | R20Cert |
| --- | --- | --- | --- | --- | --- | --- |
| dc20r-loop-e | A's dc20-loop-e: `while c => _ = State.keepe()`; the earlier Loan only in E (raw store of `counter@ref`) | error | omitted **UNSOUND** | error | error | error |
| dc20r-loop-fx | same with the err(effect) form (`touch()` writes counter) | error | **UNSOUND** | error | error | error |
| dc20r-loop-slot | the earlier Loan enters through the call's own explicit slot (`prev = State.keep2b(prev)`) | error | **UNSOUND** | error | error | error |
| dc20r-loop-closure | the earlier Loan reachable only by closure (`h = g@uniq`, g dead by name) | error | **UNSOUND** | error | error | error |
| dc20r-loop-nested-entry | `fk`'s copy begins with keep2's copy (shared entry) in a loop | error | **UNSOUND** | error | error | error |
| dc20r-loop-guard | gd's escaping γ kept across iterations (`let k = State.gd(); inspect(prev); prev = k`) | valid (gd's error) | omitted | error **(extra record)** | omitted | omitted |
| dc20r-guard-once | the same without the loop (control) | valid (gd's error) | omitted | omitted | omitted | omitted |

- `-r20`: 0 failures. The original 60 round-20 cases and the 506 earlier ones are unchanged under every mode.
- `R20EntryA` equals `R20Cert`, the sound-by-construction oracle, on all 71 round-20 cases.
- `R20Entry` differs from both only in dc20r-loop-guard.
- I found no unsound case for the shared rule. The new cases are model forms: (H) on Binding accepting group-storage borrows (PQ-1).

**Held, limited and effect templates.**
- **Held template.** A held template reports its own Loan problems as `Unsupported_Kd`, so an omission still has a record. Coverage
  needs participants from the current evaluation, never the template's status. A caller view held by a copy's store reports its
  owned failures as `Unsupported_Kd` (dc20-held).
- **Limit records** stay at their operation (R4-M69, R4-M71).
- **err(effect)** takes the entry test on its Loan (dc20r-loop-fx).
- **R3** has no participating Loan. Its omission therefore relies on the template's view being checked. 2b-D's designs change the
  skip so that a static Drop runs the stratum; keep that as a stated interface condition of 2e-4.

### 2.6 Record identity

Both designs make the subject of a caller-owned problem whose access lies in a copy the outermost omitting call, with the operation
in a fact.

- This follows 15.6 ("identified by that call and the default's operation") and 23.1.1 ("at the concrete use").
- It keeps two calls apart with no change to `MirReport`'s or publication's key.

Residue that both designs name: one record per (code, call). It also merges a relocated failure with the omitting call's own failure
of the same code, since both subjects are `SourceRef.Of(node)`. SPEC 23.1.2 makes grouping implementation-defined. Record it for
U11, which can add the fact to the Reason key.

**Fact:**
- Use one kind for "the default's operation". No consumer reads `MirFactKind` outside A5 today: `MirReport.Add` drops every
  Inlined-fact problem before publication, so only `MirAnalysisTest` uses the kind.
- If A1/A2 move in the same unit (§2.7), replace `Inlined` by `Evaluated`, A's name, which says what the site is. The vocabulary
  count stays the same.
- If A1/A2 are staged, keep A's two kinds until 2g-6.

### 2.7 A1/A2 ownership

The end state is the same in both designs:
- A1 and A2 omit a problem whose location lies in a copy; this is exact for them (R4-M69; B §2.5).
- `MirReport.Add` drops nothing.
- `MirFactKind.Inlined` leaves the vocabulary.

B does it in 2e-4. That needs responses for three hotspot files:
- `MirInit.cs`: 2x-R7 (PQ-3);
- `MirDrops.cs`: due, with no step-2 response planned; B misses it;
- `MirProblem.cs`: due; B misses it.

A stages it to 2g-6 behind 2x-R7 and a `MirDrops` ruling. During the stage two ownership forms coexist, recorded with a removal
condition.

**Recommendation:** at once in 2e-4. 2x-R7's only prerequisite is 1c-9, so it can run in a lane now. `MirDrops.cs` needs a ruling or
split, since its change is two lines. If either response is missing at 2e-4's start, fall back to A's staging, decided by the PD21
count.

### 2.8 Anchor fact

`MirBuilder.Argument` gives every argument local `Info(this.declaration, local: true)` (`MirBuilder.cs:270`). For a template that
is the function's declaration (`Template` → `Body(..., decl.Function, ...)`). So B's "the root local's declaration" names the
function, not the parameter that DIAGNOSTICS ("relate the preceding prepared parameter"), PD15 (v) and PD31 require. B-2.

The alternatives do not work:
- A5 may not read syntax (`MirBorrows.cs` contract).
- The adapter may not re-derive facts (PD31).

So the builder must publish the fact, which A's 2e-0 does.
- It is a W1 unit outside the group (PD29), after R5's restructure of `MirBuilder.cs` (AR-1).
- It is a schedule dependency for 2e-2. Ask R5 whether its restructure can carry it.

### 2.9 `Loan.Location` vs `Loan.At`

`Loan.Site` is `body.Subject(statement.Source)` at the issuing statement (`MirBorrows.Loans.cs:152`). So an int location in
`MirBorrows.Location`'s numbering reproduces the anchor and gives the issue scope, with one representation.

The two names are equivalent. Prefer `Location`, the numbering `loanAt` is indexed by. `At` reads like the `MirLocation` variables
named `at`.

### 2.10 dc-reserved-rec

- 15.6 says "a Borrow is not checked against the Loan it forms". 15.6.7 says an activation is checked against "the values live into
  its call other than its own argument".
- P9 puts the reservation's own Loan into the default's value, as SPEC's letter for a recursive default requires.
- A5 accepts by R4-M68, since the Loan is unique outside its holder.
- Both designs read this as valid and ask for a clarifying sentence. Agree with (a).
- No interim rule (PD30) is needed, because the MIR already accepts.

### 2.11 Units and hotspots

A's unit list is complete; B's lacks 2e-0. Both miss some PD21 counts:

| File | Changes since last restructure | F9's edits | Response |
| --- | --- | --- | --- |
| `MirBorrows.Calls.cs` (from `8213c83f`) | 1c-7, G148 | 2e-1 (3rd); 2e-3 removes the `Placed` call at `:69` (4th) | 2x-R3 before 2e-3, or a ruling. A has it; B misses 2e-3's edit |
| `MirBorrows.Checks.cs` (from 1c-4) | 1c-5, 1c-7, 1c review fixes (already 3) | 2b-3's hook first | 2b-D design B's proposed 2x-R13 before 2b-3. Then 2e-2, 2e-3 and 2e-4 make 2e-4 the 4th change, so a response is due before 2e-4 |
| `MirBorrows.Activations.cs` | due (§3.7) | 2e-4: `Conflict` gains the operation's scope, called at `:73` and `:101` | 2x-R9's ruling before 2e-4. **Both designs miss it** |
| `MirBorrows.Reports.cs` (from 2x-R2) | — | 2b-3, 2e-2, 2e-3; 2e-4 is the 4th | a ruling or split before 2e-4. Both have it |
| `MirProblem.cs` (from 1c-4) | 1c-6, 1c-8, 1c review fixes | 2e-4 | a ruling before 2e-4. A has it; B misses it |
| `MirInit.cs`, `MirDrops.cs` | both due | 2e-4 (B) or 2g-6 (A) | §2.7 |
| `MirBuilder.cs` (W1) | R5's | 2e-0 | after AR-1 |
| `MirTypes.Holding.cs` (shared) | — | none under r3 | B's hold disappears |

2x-P12 (A's round 21) is optional. My scratch run covers its loop cases. If the planning session wants tracked evidence, fold the 11
cases above, `R20EntryA`, A's dt-raw-ref, dc-raw-hid under r3, and the two records of dc20-twice-both into one draft round before
2e-4.

Recommend an R5 ask, B's AR-9: a `MirValidator` rule that an Inlined scope's locations are entered only at its first location. It
does not block 2e-4, but the entry rule's soundness rests on this builder invariant.

## 3. Blocking defects

**Design A:** none found.

**Design B, as written** (each has a short fold that A already contains):

| ID | Defect | Kind | Fold |
| --- | --- | --- | --- |
| B-1 | B4's `Unsupported_Kd` reads `RawLoan`, which `Holds` never sets past a shared or exclusive reference and may skip after `Loan\|Write`. dc-raw-hid (`ref/RawBox`) and G160 (a)'s `uniq/RawBox` get no raw row and no `Unsupported_Kd` | Unsound acceptance; a regression from today's `Unsupported_Kd` | A's r3 (`HoldsRaw` ∧ ¬`MirWrites.Raw` ∧ a Loan in the inputs' Contents) |
| B-2 | The Anchor fact names the function's declaration, not the parameter | Contract (DIAGNOSTICS, PD15 (v), PD31) | A's 2e-0, with its W1 slot and review |
| B-3 | R26-borrow has no raw limit, so a valid borrow of raw storage through a slot is `DefaultArgumentBorrow_Kd` | Language error on a valid form | A's A2 definition, one rule for both checks |
| B-4 | R26-access is reported only after a replay that `Run` skips for Loan-free views, so dt-fb is accepted | Unsound acceptance | A's skip term, or the one pre-pass flag of §2.2 |

## 4. Non-blocking findings (open issues or folds)

1. B's γ entry test adds an extra caller record (dc20r-loop-guard). Use A's err(ended) row.
2. B's `Report`-with-requirement adds a witness fact to R26 records and needs a location R26-borrow lacks. Use A's `Record`.
3. A's R-a reservation-holder clause is dead code. Use B's root set and its test.
4. The view skip is becoming an open family (Loan, guard, static Drop, slot write). Make it one pre-pass flag.
5. Hotspots missed (§2.11):
   - both designs: `Activations.cs`;
   - B: `Calls.cs` in 2e-3, `MirProblem.cs`, `MirDrops.cs`.
6. The erratum number E15 is taken; use E17.
7. One record per (code, call) also merges with the call's own failure of the same code. Record it for U11.
8. B lists no S1015 sections. Take A's: §7.2.3, plus MIR tests for §15.6.4 and §15.6.7.
9. Entry detection: compute each Inlined scope's entry location once in the pre-pass (its first location in reverse postorder), not
   with an O(depth) walk per replayed location. This is an implementation note.
10. Interface conditions to state in 2e-4's DEC entry:
    - R3's omission needs 2b-D's skip change;
    - err(effect) selects through `Covered`;
    - the copy-entry builder invariant (AR-9).

## 5. Recommended combination

A's design with these changes:
- **R-a:** B's root set (PendingSlot only), with B's root-kind test.
- **A1/A2:** B's timing, if 2x-R7 and a `MirDrops.cs` response are ready at 2e-4's start; then a single fact `Evaluated` replaces
  `Inlined` in 2e-4. Otherwise A's staging to 2g-6.
- **Tests:** B's practice that every omission test in 2e-4 asserts the template's record (the R20Cert property).
- **G171 witnesses:** B's PQ-1 (a) (§6).
- **Hotspots:** the additions of §2.11.

Everything else is A's:
- E17 (A1/A2 of A's §0), the slot-rooted definition and R26 with its skip term, and `Record`;
- P9 with the raw row and r3;
- R20Own + R20Entry with A's participating-scope table, err(ended) without the entry test;
- A11's subject rule, `Loan.Location` and `MirBody.Common`/`OmittingCall`;
- 2e-0 and the unit order 2e-0 → 2e-1 → 2e-2 → 2e-3 → 2e-4 → 2e-5.

The PD21 responses (§2.11) go at each unit's start.

## 6. Consolidated questions

**Owner (with OQ2's package approval):**

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-F9-1 | dc-reserved-rec: is an activation checked against its own reservation's Loan when a recursive default's value carries it (SPEC 15.6, 15.6.7, 7.2.3)? | (a) No: valid. The MIR already accepts (R4-M68), and a clarifying 15.6.7 sentence is added at the next SPEC session. (b) Yes: an error. That needs a new A5 rule and, until SPEC states it, PD30's `Unsupported_Kd`. (c) A located `Unsupported_Kd` until decided | (a): a valid recursive default cannot alias the input (7.2.3); only P9's letter carries the Loan |
| OQ-F9-2 | Confirm the departures from owner-approved texts as one list (OQ6's pattern). (1) R4-M69's Inlined rule, which also amended R4-M10's Inlined sentence: superseded for A5 in 2e-4, and for A1/A2 in 2e-4 or 2g-6. (2) R4-M73's sub-bullet "R4-M69 governs inline default copies": the hold applies to every owned failure. (3) R4-M72's Defaults bullet and its premise: superseded by F9's producers and the slot step (E17), as R4-M72 itself plans | (a) confirm all; (b) per item | (a). The M69 plan's exclusion of relocation is not in force (R4-M86), so it needs no confirmation |

**Planning session (with BU or R5 where named; an owner question only if they disagree):**

| ID | Question | Recommendation |
| --- | --- | --- |
| PQ-1 (B's) | BU's 2b-6 would make the unqualified in-group borrow Binding's `Unsupported_Kd`, removing every caller-owned G171 witness until P42. Does that include implicit receivers (`counter.add()` in the group)? | Split 2b-6: the qualified part lands as planned; the unqualified (and implicit-receiver) part lands after G:A5-F9. 2e-4's witnesses then fail before and pass after on normal source, and PLAN keeps a P42 row for the re-expectation |
| PQ-2 | Hotspot responses: `Calls.cs`, `Activations.cs`, `Reports.cs`, `MirProblem.cs`, and `Checks.cs` after 2x-R13 | Rulings where the edit is one or two lines (`Calls.cs` 2e-3, `Activations.cs` 2e-4, `MirProblem.cs`); for `Reports.cs`, a split if A's `Record` makes it two responsibilities |
| PQ-3 | A1/A2 at once (2x-R7 and a `MirDrops.cs` response before 2e-4) or staged to 2g-6 | At once if ready (§2.7) |
| PQ-4 | r3 as a temporary producer, registered under PD28 with G160's representation as its removal stage | Accept. It goes to the owner with OQ9's list at X-2 |
| R5 asks | (i) 2e-0's slot in W1 after AR-1, or inside R5's restructure; (ii) AR-9's validator rule; (iii) H3's rendering of the `Evaluated` fact | Send with the package |

## 7. Unverified

- **Monotonicity of type facts, `TyCtxt.Retains` and A4's keys under instantiation.** Both soundness arguments use it (A §5.6 (c),
  B's S1). The mitigation is the omission tests asserting the template's record, plus a generic template whose instance gains a row.
- **Binding acceptance of the new loop shapes** (raw store of a static borrow; unqualified group borrows); this is PQ-1.
- **dt-raw-ref's validity in the Origin stratum** (H).
- **The prototype models none of the following:**
  - the compiler's entry detection or `Loan.Location`;
  - R26's interaction with a Move of a borrowed slot (A1's code plus a Loan conflict: independent, kept);
  - the Evaluated/Inlined records.
- **No measurements.** Both designs give their size, speed and V16 terms as (H).

## 8. Confirmation of the consolidated package (`package.md`, 490 lines)

I checked the package read-only against this review, the step 2 plan and dev's code. I ran no suites.

### (1) The recommendations are carried over, and no B defect re-enters

Each B defect is fixed by the package's own decision:

| B defect | Fixed by |
| --- | --- |
| B-1 (raw pointer) | D9's r3 uses `HoldsRaw`; `MirTypes.Holding.cs` is unchanged |
| B-2 (Anchor fact) | D6 and 2e-0 |
| B-3 (raw limit) | D2's one slot-rooted definition, read by both R26 rules |
| B-4 (dt-fb) | D4 (1)'s `Accessing` hook, plus D5's flag; dt-fb is in 2e-2's tests |

The other recommendations are present as I meant them:
- D7: `Record`, and R26 records carry no witness fact.
- D10: B's PendingSlot root set, with the root-kind test.
- D12: err(ended) without an entry test; dc20r-loop-guard (one record) is in 2e-4's tests.
- D13: `Evaluated` replaces `Inlined` only when D15 lands in 2e-4. If D15 is staged, both kinds exist until 2g-6.
  `MirFactKind` is not one of ARCH's closed vocabularies, so the planning session decides this.
- D15: the A1/A2 condition.
- PD28: r3 is registered there, with G160's representation as its removal stage. It reuses `Unsupported_Kd`, so no second code is
  added.
- The oracle practice: every omission test in 2e-4 asserts the template's record.

### (2) New blocking defects

None in the rules. In particular:
- **R-a.** D10 ends only the subtrees of the moved PendingSlots. Every argument of an omitting call is one of these
  (`MirBuilder.Calls.cs:87`, `:105`). The template call's inputs are the prologue temps, which are not PendingSlots, so its P9
  Content rows still read σ(slot).
- **err(ended).** Argued in §2.5 and shown by `R20EntryA`, which equals `R20Cert` on all 71 cases.
- **r3.** A located `Unsupported_Kd`, never a Language error.

One statement in the soundness section is wrong: **C-1**, below.

### (3) The units against the step 2 plan

- **Chain.** Lane C runs after 2b-3 (PD1). 2e-1 has 2b-3 as its chain prerequisite, and 2e-4 needs 2b-3 merged.
- **PD21 responses.** All are present:
  - 2x-R3 before 2e-3;
  - for `Activations.cs`, `Reports.cs`, `Checks.cs` and `MirProblem.cs`, a response before 2e-4;
  - 2x-R7 and a `MirDrops.cs` response as D15's condition.
- **Holds.** 2e-0 takes R5's W1 slot and its own review (PD29); 2e-4 holds `MirBody.cs` (shared).
- **Asks.** BU's 2b-6 split is PQ-1, and R5's three asks (2e-0's slot, AR-9, H3) are stated.

Non-blocking notes:
- (a) **Record the agreements.** §7 should say that BU and R5 agreed (2026-10-21).
- (b) **2x-R3 and 2x-R9 change position.** Record the swaps in PLAN §3 (PD1).
  - 2x-R3 moves from after 2a-2 (its prerequisite in the step 2 plan's §4.1) to before 2e-3.
  - 2x-R9's own prerequisite is 2f-D. So the package's "2x-R9's ruling before 2e-4" should read: "a PD21 ruling for
    `Activations.cs` at 2e-4's start (124 lines, one responsibility; 2e-4 adds one argument at `:73` and `:101`). 2x-R9 keeps its 2f-D
    condition."
- (c) **Where r3 is recorded.** The package says r3 is recorded "in the checks, like `CallRetention.Limit`". If that means
  `Checks.Check` (`MirBorrows.Checks.cs:84`), 2e-1 also changes `Checks.cs`, and 2e-3 becomes that file's fourth change. Record r3 from
  `Calls.Call` when `checks` is set (2e-1 already changes `Calls.cs`), or count 2e-1 for `Checks.cs`.
- (d) **2e-0's lane.** 2e-0 runs in R5's W1 slot, not on lane C. Only 2e-2 depends on it.

### (4) Owner questions and SPEC sentences

No SPEC sentence is needed for either point the coordinator asked about:
- **E17** is A3's representation of 7.2.3's slot (an erratum). The step 2 plan's 2e-D row, adopted with R4-M74, already names "the
  PreparedInput Deref's step class Inline".
- **Relocating caller-owned problems** follows SPEC:
  - 15.6 identifies a default's Borrows "by that call and the default's operation";
  - 23.1.1 locates a record "at the concrete use";
  - 23.1.2 makes grouping implementation-defined.

PD15 (i) needs none either (15.6.3's letter). The only SPEC sentence the package needs is OQ-F9-1's 15.6.7 clarification.

OQ-F9-2 is incomplete by one item: **C-2**, below.

### Remaining blocking items, with exact fixes

**C-1 (soundness text, §5.7 "Builder invariant", and §12).**
- The package claims: "If the invariant breaks, Entry(B) over-includes, which produces extra records, never an unsound omission."
  That holds only for half of the invariant.
  - If the template's block 0 gained a predecessor, the entry state would include current-evaluation Loans. That over-includes, which
    gives extra records only.
  - If an edge entered a copy from outside at any location other than its first, the state that edge carries would not be in Entry(B).
    A stale Loan would then be omitted while its template reports nothing. That is unsound.
- The rule is sound only while the builder keeps the invariant. Dev's `Inline` keeps it today, but nothing validates it. AGENTS:
  consumers trust validated IR, and the validator is the producer's.
- **Fix.** Replace the sentence with: "If the template's block 0 had a predecessor, Entry(B) would over-include (extra records only).
  If a copy were entered elsewhere than its first location, Entry(B) would miss the Loans that edge carries, and an omission could be
  unsound. So AR-9's `MirValidator` rule is a prerequisite of G:A5-F9's dev merge."
- Add "AR-9 merged" to 2e-4's prerequisites, or to the group's merge condition. R5 has agreed to AR-9.

**C-2 (OQ-F9-2, item 4).** R4-M72's A6 table was adopted with the plan (R4-M74). DIAGNOSTICS states it: A5 "records each conflict at
its access or activation". D13 records a Loan problem that a body owns inside a copy at the outermost omitting call, with an
`Evaluated` fact. Those problems were dropped until now, so this is a new row of that owner-approved table.
- **Fix.** Add item (4) to OQ-F9-2: "(4) R4-M72's A6 table gains a row: a Loan problem a body owns whose access lies in an inline
  default copy is recorded at the outermost omitting call, with the default's operation as its `Evaluated` fact. SPEC 15.6, 23.1.1
  and 23.1.2. PD17's 2g-4b may revisit primary locations."
- Keep the note that the M69 plan's exclusion is not in force.

**Verdict:** ACCEPT once C-1 and C-2 are applied. Both are text changes, plus one prerequisite line. Notes (a)–(d) are non-blocking.
