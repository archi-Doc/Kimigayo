# The later SPEC session, the items after N2 — options (revised)

Record (DECISIONS R4-M134). First written on 2026-10-25 by R4's designer lane, on dev `cdd3c99da`, for the request 「(a) 今始める、
N2 から (推奨)」. N2 has its own record (`draft/Sketches/SPEC N2/`). The independent review (`review.md`, REVISE) corrected it, and
the owner answered the corrected questions the same day. This revision keeps the analysis and states the review's corrections
and the answers. The SPEC text is R4-M134's. Probes: `probe-verdicts.md` (designer), `review-verdicts.md` (review), sources in
`probes\`.

## 0. Summary and answers

| Item | The gap or reading | Answer (owner, 2026-10-25, verbatim) and what it means |
| --- | --- | --- |
| RZ1 | §5.2.1 (3) did not say what capability a borrow of a raw Place needs | 「(a) 借用のモードで (推奨)」: by its mode, as §15.1.5's operation table gives it |
| RZ3 | §5.2.3 destroys `_ = (*p)@move` in place; §14.2.4 transfers first. Both pipelines transfer | 「(a) その場で破棄 (推奨)」: in place for raw Places, stated in §14.2.4 |
| RZ5 | Punning any Copy Type can forge references, Slices, provenance and invariant Types | 「(b) 閉じた一覧 (推奨)」: a closed punnable list, with the review's three wording fixes |
| RZ7 | A raw write may store a value whose Origins the destination's Type does not admit | 「(a) 宣言された型に合わせる (推奨)」: fit the declared Place's Type, judged through the writer's contract |
| Element storage | Condition 4 did not cover element storage under a Loan of its collection | 「(a) 集合型の Loan も及ぶ (推奨)」: "or of the collection whose element storage it acts on" |
| G86 | §15.3.5 gave no requirement for a slot used only by a conditional pair slot | 「(a) 場合ごとに (推奨)」: per admitted Semantics case, Fixed points amended |
| G159 (2) | The join compares the own Types when no source supplies a candidate | 「(b) 部分ごとに (推奨)」: part by part over such sources, nested selections included |
| G158 (3) | §6.7.1 errs at uses, Binding at declarations | Settled by the letter (§8.15.3), no question: BU and R5 implement it |
| §15.4.2 | No row for an omitted Origin in a Constraint's Type outside a Function Type | 「(a) 明示が必要 (推奨)」 |
| Closures | The receiver/Subject-mode order; the owner's two readings (R4-M130) | Wording; the range question settled by R4-M130; Q10′ 「(a) はい (推奨)」 |
| G248 F2, O1 | When a default's temporaries end; an interrupted Dictionary literal's entries | F2 「(a) 呼び出し先に入る前 (推奨)」; O1 「(a) 要素ごとに (推奨)」 |
| G248 F1 | An `if` arm's temporaries | Settled by R4-M129; an editorial §3.6 sentence |

## 1. RZ: raw storage follow-ups (PLAN RZ; `draft/Changes/2026-10-15 Raw Storage as Storage.md` §7)

RZ2, RZ4, RZ6 and RZ8 were closed. Authority and forging are never compile-time errors (§5.7 Authority, STATUS raw row), so RZ1,
RZ5, the element-storage item and RZ7's rule change which unsafe programs are defined; RZ3 changes generated code.

### 1.1 RZ1: the capability a borrow of a raw Place needs

§5.2.1 governed "a read, write, Take or borrow of a raw Place", but condition 3 named capabilities only for reading, writing,
Take and in-place destruction; §5.7 Authority lists the `@uniq` and `@objuniq` Borrow among the accesses §5.2.1 decides.
- **(a), answered.** A borrow needs the capabilities §15.1.5's operation table requires of its mode: read for a shared borrow,
  read and write for an exclusive one. The review: name them by the table, with no second list; §5.2.2 already says the
  conditions hold while the result is used.
- **(b).** Only the capability of each access made through it. The unsafe borrow keeps every promise, and the undefined behavior
  happens at a later safe write.

No Binding or MIR change. The library's raw `@uniq` borrows go through uniq remainders and receivers, which have write authority
(Storage.kimi:123, :142, :205, :208; UniqSlice.kimi:45; Array.kimi:108).

### 1.2 RZ3: `_ = (*p)@move`

§5.2.3: "`_ = (*p)@move` destroys the value in place"; §5.2.1 (3) and §5.7 list in-place destruction apart from Take. §14.2.4
acquires the operand "by bare acquisition or transfer" and destroys it at statement temporary cleanup. Probe `rz3-inplace`: a
`drop` compares its Field's address with the one recorded at placement; old and sel print `moved moved end` for the raw Place and
for a local. Users: `DictionaryStorage.clearEntries`, `Storage.kimi:42, :88, :89, :171`, §5.6's `Buffer`.
- **(a), answered.** In place for raw Places (also a Field or element of one), at statement temporary cleanup, before the
  statement's temporaries; §14.2.4 states it.
- **(a′).** Every `_ = P@move` in place. For a tracked Place, A1 and A2 would have to read a mid-scope Drop as a move-out.
- **(b).** The transfer, as both pipelines do. It narrows SPEC to the implementation (the review) and leaves no in-place form.
- **(c).** (b) plus `Raw.destroy`, a second form.

Effects of (a): no Binding change; the HIR's discard classification and `MirBuilder.Discard` build a Drop of the raw Place (R4);
sel lowers it through the pointer (R5); the old generator transfers until R6-check (STATUS).

### 1.3 RZ5: forging by type-punned access

§5.2 let a raw Place "whose Type is Copy and positive-sized" designate a punned Place, so a pun could forge a reference or Slice
with no Loan, a raw pointer from integer bytes with unstated provenance, and a value that breaks a constructor's invariant
(`ResolvedRange`, a user `Self is Copy` Type); a punned write could change an Array's length metadata. The library puns only
`isize` (`(slot@raw/isize)[i]` over `Links`); its Handle Fields are Places since RZ6.
- **(b), answered: a closed list.** Integer, wrapping integer, floating-point, `bool` and `char` Types, raw pointer Types, and
  Tuples and fixed arrays of them. The review's three fixes:
  1. a punned write may change padding bytes of a Place that is not punnable;
  2. a punned raw pointer whose Type states an Origin promises what §5.7's claim promises;
  3. a Field or element of a raw Place is a raw Place of its own Type, so `(*p).f` puns Field by Field (C header parsing stays
     possible; §5.2's `Header` example).

  A punned raw pointer has the provenance of the raw pointer whose bytes it reads when a raw pointer's write, placement or
  relocation last wrote them, otherwise none (§5.5). A punned write changes a byte only when it is padding or its innermost
  Initialized Place is punnable: no payload is constructed and no Case changes, a Copy enum's included.
- **(a).** Copy, with "valid" meaning a held, live value: not decidable from the code.
- **(c).** One rule per class: one more case per class.

### 1.4 RZ7: Origins of values placed in declared Places

A raw Place designates a Place "with Origins treated as equal" (§5.2), and Retained dependencies covers raw storage only.
- `rz7-forge` (a forged pointer stores `z@ref` into a parameter's referent): A5 rejects it, the old path accepts it.
- `rz7-other` (the forged pointer stores another parameter `other: ref/i32 during d` into `slot: uniq/(ref/i32 during c)`): both
  pipelines accept it, and the caller's later write to `other`'s referent while `r` is used is accepted.
- The review's `rv-rz7-leak` (`var r: ref/i32 during a = a`, a forged store of `b`, `return r`) and `rv-rz7-leak-param` are accepted
  too: the caller's rows are `0 Value result`, so `y = 8` while the result is used is accepted.

The letter defined all of them. R4-M116 had recorded (the re-review's R-N2) "a callee that stores a converted pointer where its
contract's Type cannot hold the argument breaks its contract (undefined behavior); no text change"; R4-M131 superseded it only at
unstated positions.
- **(a), answered.** "A value written or placed through a raw Place into a Place outside raw storage must fit that Place's Type,
  Origins included, as a write through its access path would, judged through the writer's contract": a Place reached from inputs
  has the Type the signature gives it. Beside §5.2's designation rule; Retained dependencies keeps raw storage. It supersedes R-N2's
  "no text change" at stated positions.
- **(a′).** The contract rule for storage reached from inputs only (the first version's recommendation). The review showed it
  unsound: it leaves `rv-rz7-leak` (the body's own declared Place) and static storage defined, and §15.6's carriage does not
  help, since a parameter's Loans have no access path in the callee.

### 1.5 Element storage and condition 4 (RZ6's commit `fe2839c11`)

Element pointers have `allocate` authority; condition 4 compared only "a live Loan of a Place overlapping what it acts on", and
the Loan of `a[0]` or `a@ref` is on the Array's Place, outside its element storage.
- **(a), answered.** "…or of the dynamic collection whose element storage it acts on". Not an own step: §15.6.2 defines own
  steps for payloads and §5.7 for `during self` storage, and an own step would end an element pointer's authority at a Move of
  the collection (§5.2.1 (3)'s third limit, `OwnStepDeparture_Kd`), against RZ6's `allocate` authority.
- **(b).** Authority alone.

## 2. G86: a slot used only by a conditional pair slot (§15.3.5)

§15.3.5 computed requirements "from its shared or exclusive uses" without saying what a conditional slot (§15.3.1, §8.1.2) counts
as. §5.7 and §15.6.1 judge per admitted case; §8.15.2: "An instantiation is the case its arguments select"; SETTLED rejects one
premise set for all cases. Binding (`Binding.TypeOrigins.AccumulateRequirements`) gives a pair layer `ref` (`IsExclusive(pair)` is
false); its consumers are the static-binding check, `RawStep.Shared`, `RawMembers`' exclusive flag and the old analysis.
Probes: `g86-static-uniq` and `g86-static-uniqonly` (`s is uniq`) are accepted, the concrete twin is `InvalidOriginBinding_Kd`, and
`g86-static-owner` is accepted.
- **(a), answered.** Per admitted case: shared in the `ref`/`objref` cases, exclusive in the `uniq`/`objuniq` cases, none and
  Phantom where no borrow case stores it; an instance has its case's. The review: §15.3.5's Fixed points sentence is amended (one
  value per declaration per admitted case), and variance stays one per declaration.
- **(b).** The strongest admitted case. It rejects `g86-static-owner`, a valid static owner instance.

`g86-static-uniqonly` is a Binding defect under every option (filed now, apart from the question). Binding's per-case
representation (BU, full process) follows.

## 3. G159 (2): a join none of whose sources supplies a candidate (§14.9.2)

Step 2: "Never supplies no candidate". A Tuple literal with a Never part (`(a, (return 1))`) is not Never: its own Type is
`(i64, Never)` (BU-9). Binding's late fit lets such a source supply no candidate, and when none supplies one, "the sources' own
Types compete": `g159-both-await` is `TypeMismatch_Kd`, while `let t: (i64, i64) = …` is accepted and the array twin is too. The
review: a nested selection all of whose sources await is not a literal (`rv-g159-nested`, `TypeMismatch_Kd` ×2); `rv-g159-nested-cand`
has the candidate `(i64, i64)` and is valid by the current letter ("a literal result is not fixed before outer constraints"), yet
Binding gives `TypeMismatch_Kd` ×2, a defect filed in G159; `rv-g159-match` behaves as the `if` does.
- **(b), answered.** Part by part, worded over the sources that supply no candidate (Tuple, array and Dictionary literals holding a
  Never part, and selections, `do` and `loop` none of whose sources supplies one), each part found as step 2 finds one from its
  parts other than Never, a part Never in every source being Never.
- **(a).** The own Types, an error. **(c).** A Never Expression Type.

## 4. G158 (3): where C layout's failure is an error (settled)

§6.7.1 and §8.15.3 ("Forming the Type only behind a borrow, raw pointer or object handle … does not check them. A failed
obligation is a Language error at the use that needs the layout") decide it: the first version's option (b), a declaration error,
had only the implementation's check as its reason, which would narrow SPEC to fit the implementation (AGENTS). Binding's checks in
`Binding.Attributes` (`ValidateLayoutFields`, `ValidateCLayoutFields`, `CheckCLayoutInstance`) are conservative Language errors (BU),
`Layout`'s CLayout outcome gains the open, derived and empty cases and `check` reports the checked set's obligations (R5), and a
planning note supersedes R5-M55's "Binding's declaration check is lasting (X26)", whose reason was duplicate records.

## 5. §15.4.2: an omitted Origin in a Constraint's Type outside a Function Type (G170, B §3.4)

§15.4.2 had no row for `T is ref/U`, `T is Option<ref/U>` or `T is View<U>` with an omitted slot; §8.2.3 covers only a Function
Type in a Constraint. Binding reports `MissingOriginBinding_Kd` at every such position and at an unbound name (`e5-ref-unbound`).
The review's corrections:
- The outer layer has a form: a pair parameter, whose outer Origin is "one fixed Origin of the body … with no source name"
  (§8.1.1). `rv-e5-pair-item` (`func f<s/U>(x: s/U, item: U) -> U` under `s is ref`) and `rv-e5-pair-result` are clean.
- (a+) needs a rule for how a call instantiates a name that appears only in a Constraint (Constraints are not the Signature,
  §7.5). Even a signature-bound name cannot be proven at a call today (`e5-ref-sig`, `e5-ref-other`, `rv-e5-same-borrow`:
  `UnprovenConstraint_Kd`), so a Constraint-only universal would fail at every call until G170's identity unit.

Options and answer:
- **(a), answered.** "Type in a Constraint, outside a Function Type: Must be complete from explicit information."
- **(a+).** Explicit, and unbound names in a function's Constraints introduce universal Origins, solved at each call.
- **(c).** An omitted Origin is the subject's own unnamed Origin at that position, as a pair binder's is.

(a+) or (c) may come later as an explicit extension.

## 6. The Closure receiver and the Subject mode

§15.1.6 bounds a bare Subject's mode by "any shared layer on the path", while a Closure's receiver comes from its body
(§7.7.3), so both receivers were fixed points. R4-M130 records the owner's two readings and settles the range question ("a
ResolvedRange Subject through a captured `uniq` … is Exclusive too"); 2c-1 is merged (`a228d3ed7`, `124e2a47c`), and `rv-c-range`
is `InvalidAssignment_Kd` on dev. The wording (R4-M134) states:
- the requirement comes from the operations, and a path from an environment binding has no receiver layer (§7.7.3; §15.1.6);
- exclusive access includes an Exclusive-mode Subject, also when the body only reads and whatever the iteration entry does;
- it includes an access through a raw pointer read from the environment, or through a pointer derived from it, that needs write
  capability: a write, a Take, an in-place destruction or an exclusive borrow (Q10′, 「(a) はい (推奨)」).

On dev, `*p = 2`, `*p += 2` and `(*p)@uniq` are Exclusive, while `consume((*p)@move)` (`rv-c-rawtake`), `_ = (*p)@move`
(`rv-c-rawdestroy`) and `let q = p + 0; *q = 2` (`rv-c-rawcopyptr`) are Shared: a 2c-1 follow-up (R4). Open, no rule: a callee
writing through a captured pointer the body passes it (`rv-c-rawinit`: `Raw.initialize(p, 5)` in a `let` Closure is Shared).
DIAGNOSTICS' Note ("because the callee changes its environment or a captured referent") follows the new table wording.

## 7. G248: F2, O1 and F1

**F2.** A default "is evaluated … in the declaration's scope", and "Inspection Loans end before callee entry"; §3.6 did not say
whether a default is an outermost expression. `g248-f2-default`: sel ends the default's temporary before the callee, the old
generator at the statement's end. `g248-f2-guard` (`k: i32 = Guard.init(u@follow@ref).get()` with `u: uniq/i32`): the old path
rejects `g(x@uniq)` (`CallActivationConflict_Kd`), A5 accepts it.
- **(a), answered.** Before callee entry: an omitted default is an outermost expression of its own, and its temporaries end once
  its value is in its pending slot.
- **(b).** Through the call. Corrected by the review: "Inspection Loans end before callee entry" would then read as an
  obligation, not as false, so `g248-f2-guard` becomes an error. `default-ref` (Binding's `UnsatisfiedOriginRelation_Kd`) is weak
  evidence, since the declaration-scope check against a universal parameter Origin also explains it.

**O1 (the review's B6, PLAN G253).** §16.2.1 ordered "each placed component of an unfinished Tuple, array or enum Case
construction" but not a Dictionary literal's inserted entries. At `230938d7a`: `rv-o1-tuple` `two one` (old and sel), `rv-o1-array`
`two one` (old), `rv-o1-dict` `one two` (old; the MIR's own temp, R4-M129, gives the same, and
`ADictionaryLiteralsEntriesArePlacedInAValueOfItsOwn` asserts it).
- **(a), answered.** Per entry, as Tuple and array components; §12.4.3 cross-references §16.2.1. Both pipelines' Dictionary order
  changes, and the MIR test's expectation changes by this decision.
- **(b).** One temporary acquired where the literal begins.

**F1 (settled by R4-M129).** An `if` clause's and a `do`'s body ends its temporaries with it, as a `match` arm's (§14.2.1, §16.2.2
step 3); every F1 probe agrees across pipelines at `230938d7a`. The editorial §3.6 sentence "A Body's items are outermost
expressions (§14.2.1)" records it.

## 8. Unverified

- The undefined-behavior-level items (RZ1, RZ3's semantics, RZ5, RZ7's rule, element storage) are reasoned from the text; no checker
  exists. Library censuses are by code reading.
- The static-storage hole under (a′) is reasoning only: a `ref` static is `Unsupported_Kd` today.
- RZ3 (a)'s MIR and sel shape, G86's per-case representation, G159 (b) and O1 (a) have no prototype; their effect on the corpus,
  milestones and difference ledger is unmeasured.
- The MIR's order for an interrupted Array literal was not observed (sel cannot build it).
- Item 6's coverage was probed through the `let`-Closure `InvalidAssignment_Kd` signal on the CLI.
