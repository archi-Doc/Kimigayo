# Abstract Calls Reach by Paths

Date: 2026-10-22, revised the same day after two rounds of independent review (§13). Scope: a SPEC revision that removes the
abstract-input Origin reach of SPEC 8.11.4 rule 1 and everything that existed because of it (`preserves results`, rule 3,
delegation, P12's abstract descent), and the implementation plan for the new MIR path and the old Analysis (the default
pipeline). Base: dev `4c2e5d69`; line numbers refer to it ("8.11.4:789" is line 789 of `docs/spec/08-…md`).

Status: 取り込み済み (2026-10-22). Adopted by the owner on 2026-10-22 with Q1 (a) and Q2 (a) (§1; DECISIONS R4-M106); §5
integrated into SPEC by unit U0 (INTEGRATED), units U1–U5 implement it (PLAN). It supersedes the proposal
[Preserves Results by Receiver Place](../Obsolete/2026-10-16%20Preserves%20Results%20by%20Receiver%20Place.md) ("S2"), closed as
完了 because none of it was integrated, and the open questions of the sketch
`draft/Sketches/A5 Step 2/preserves-revision.md` ("2f-S1"). Adoption implies no implementation support (STATUS).

Conventions: (H) marks an inference that was not run. Case ids `c1`… are 2f-S1 §8's, `h1`… and `p1`… are
`draft/Sketches/A5 Step 2/spec-readings.md`'s, `36a`… are `draft/Sketches/A5 Step 0/prototype.md`'s.

## 0. Summary

An abstract call reaches what a concrete call reaches: the Places reachable by path from its inputs (15.6.2), the Loans its
published contract lets its results and writable storage keep (15.6.4, with the descent of 15.6.3), and its environment
effects (8.11.2). Two abstract-only rules are deleted: the clause of 8.11.4 rule 1 that lets an abstract input "reach every
Loan its Origins may denote", and 15.6.3:571's sentence that gives an abstract call's descending result the parent Loans of
its receiver's route (P12). Everything that existed to excuse the first goes with it: `preserves results` (8.11.3), rule 3,
8.11.5 delegation, the earlier-result conformance check, the Callable and slot variants (8.11.6, 6.4.6) and Iterator's bound
(22.2.1, 22.2.3). `confined` stays and becomes the only effect bound.

- **Soundness** reduces to two properties the SPEC already relies on (§4): generic checking over-approximates every
  instantiation (L1: 8.15.1 and contract compatibility, 15.3.7), and the concrete call rule is sound under containment and the
  raw-borrow promise (L2: 15.6.3, 5.2.1 condition 4, 5.2.2). Under L1 a counterexample to this proposal is a counterexample for
  concrete calls too, and no exclusion such as `preserves results` can repair either.
- **Gains:** one call rule for concrete and abstract callees; no relation between calls, invalidation, Move carry, tags,
  phantoms or earlier-result holder; the planned A5-F6 units and the descent unit are not needed; about 1,200 compiler lines go.
- **Costs:** no program changes from valid to error under SPEC, apart from kind A of §7 (a declaration of `preserves results`
  becomes a syntax error). Unsafe code that only abstract calls reach loses a caller-side
  protection it may have relied on (§4.4). The old Analysis changes under a freeze exception (§11.2), and G193's 18 tested forms
  become the containment errors SPEC already requires (§1).

## 1. The owner's decisions and questions

- **Direction and corrections** (owner, 2026-10-22, verbatim: 「推奨の通り修整し、bの移行方法で進めます。正式な変更案を書き、独立レビューにかけて下さい」).
  The owner had received a comparison of three designs and a review of the strongest one ("C"), and accepted that review's
  corrections: the cause named as rule 1's Origin clause (§2), soundness argued by L1 and L2 (§4), 5.2.1 condition 4 and 5.2.2
  as the governing unsafe obligation (§4.2), the unsafe exposure located (§4.4), 16.3.3:196 made explicit (§3.4), migration
  planned, superseded decisions listed, SPEC text in full, verdict changes counted by name, and the shared destruction-part
  definition of `MirTypes.Destruction` and `DropGlue.Parts` left to a separate unit. One accepted correction, keeping P12's
  descent, rested on a premise the design review refuted (§13, S1); Q1 asks the owner again.
- **Migration (b):** the frozen old Analysis, and Binding's `EffectSummary` and `PublishesIndependentResults`, are changed so
  that the default pipeline follows this proposal, instead of keeping `preserves results` as a temporary library declaration
  until R6-check. This is an owner exception to the migration freeze (COMPILER_ARCHITECTURE "Freeze (D5)", DECISIONS R4-M35);
  its text is in §11.2 and U0 records it.
- **Adoption** (owner, 2026-10-22, verbatim: 「Q1 (a) P12も削除、Q2 (a) CallEffectConflict_Kdを廃止、で採択します。提案をdevにコミット・pushしてください。」):
  the proposal is adopted with Q1 (a) and Q2 (a). Appendix A stays as the record of the option not chosen.

**Q1, P12 (15.6.3:571's abstract descent).** Answered: (a). P12 gives a descending result of an abstract call the parent Loans of its
receiver's route (`var r = h@follow@uniq; let first = r.0.take()` keeps `r`'s Loan on `h.*`). Its recorded justification is
rule 1's reach: "The Loans that a requirement call's result may keep … (§8.11.3, §8.11.4 rule 1) run through the receiver's
dereferences" (INTEGRATED 2026-10-17), and its cases 36c, 36e and 36g are errors only because an abstract call reached the
phantom Φ_c. With that reach deleted, P12 protects nothing a concrete call needs, and "Concrete calls keep §15.6.4's
per-argument rule" (the same record).

- (a) Delete P12 (recommended). Abstract and concrete descent become one rule. `takeFirst`'s `h.1 = 5` becomes valid (rustc
  accepts it, 36f); no program changes from valid to error; the descent unit, PLAN G113 and the 22.2.3:133 tension disappear.
- (b) Keep P12 as a precision restriction with no soundness role. One shape then changes from valid to error (a result taken
  through a Reborrow, then a call on the original), the old Analysis keeps (d) differences, and a descent unit with a new
  Binding fact follows. Appendix A lists the changes to this proposal under (b).

**Q2, `CallEffectConflict_Kd`.** Answered: (a). Its only producer is the old Analysis's rule 1 and rule 3 (U1 deletes it); R4-M72's Effects
bullet (with the A5 step 2 plan's PD7) kept it for A5-F6's Φ_c, which no longer exists. (a) Retire the code, its requirement
and its DIAGNOSTICS rows in U1 (recommended); environment conflicts with static Loans stay `ComparisonLoanConflict_Kd`
(R4-M92). (b) Keep it reserved; DIAGNOSTICS:158 and :301 then only change their section citations.

**Confirmed by adoption** (no alternative is sound or none is needed):

- **G193's containment skip goes in U2.** Binding's `PublishesIndependentResults` exempts results that `preserves results`
  verified from 15.6.3's containment (PLAN G193; DECISIONS R4-M94 OQ-2 (a) removes it with the old path at R6-check). The skip
  is unsound today: a `Cursor` that returns `.Some(self.value)` as `ref/i32 during source` from a `uniq/i32 during source`
  Field, with an `update` method that writes `self.value@follow`, passes `check` and aborts at run time after `first =
  cursor.next(); cursor.update()` (review probe p1; without `Self is Iterator` the return is `UnprovenOriginRelation_Kd`). With
  the verification gone, keeping the skip would accept every such form. The 18 tested forms become containment errors; PLAN
  G193's "not miscompiles" is corrected.
- **Documents that change only on explicit instruction:** STYLE.md:116 cites §8.11.6, and SPECJ_TERMINOLOGY.md:30 lists
  `preserves results` as a term; adoption authorizes updating that citation and dropping the term. `docs/SPECJ.md` is being retranslated on `codex/specj-from-source` (`bbae6089`); its source
  revision predates this change and it follows only on an explicit translation update.
- **Optional:** a SETTLED entry recording why an effect bound that relates two calls is rejected (§9), if the owner wants one.

## 2. Problem and root cause

**Required meaning.** A call acts with the authority it obtains from its inputs, by paths through them (15.6.2) and by raw
authority (5.2.1 condition 3), and from the environment (8.11.2). A Loan that a value carries protects the Loan's Place while
the value may be used (15.6); carrying a Loan grants no access. An access through a reference that carries a Loan does not
conflict with that Loan: paths from distinct roots are disjoint, since "the Loans each reference carries cover its aliases"
(15.6.2:506), and a raw access is exempt from "the Loan of the path the pointer was taken from" (5.2.1:58).

**What the rules state wrongly.** 8.11.4:789 adds, for abstract calls only, "an abstract input may reach every Loan its
Origins may denote". It treats the Loans an input carries, and every Loan another value of the same Type carries, as Places the
call may access. A concrete call never reaches a Loan on another root (15.6.2:506), and a conforming implementation cannot reach
one without breaking containment (15.6.3) or the raw-borrow promise (5.2.2). So the clause forbids keeping any result of an
abstract call across a later call on a value of the same Type, and a stack of rules re-permits part of it: `preserves
results` and its eligibility (8.11.3), rule 3 (8.11.4:791), delegation (8.11.5), the Callable and slot variants (8.11.6:843,
6.4.6:349), Iterator's bound (22.2.1:78, 22.2.3:135) and 15.6.3:583's exception. P12 (15.6.3:571) was added to make the
clause's phantom sound. These need value identity or a receiver-Place history and an undefined Place summary (S2 §1), and, in
the revised design, tags, invalidation, Move carry, an earlier-result holder, delegated tags, carrier atoms and phantoms
(2f-S1 §4–§5).

**Evidence that the clause is the cause.**

- The same run-time state is accepted through a concrete call: `a = it.next(); b = it.next()` on a concrete Iterator is checked
  by argument Borrows alone (15.6.3:583; S2 §2.3 item 3).
- S2's own soundness argument H2 reduces the `preserves results` conformance to the ordinary body check, containment and the
  raw obligations (S2 §2.3), so the conformance check protects nothing those three do not.
- Loans that a generic body receives through parameters are already unreached there (15.6:450; c58 `pass`), so the clause only
  constrains results obtained inside a body.
- The new MIR path already checks abstract calls by path (§11). The clause lives only in the old Analysis, which applies it
  even to user generic functions whose callee is concrete (`EffectBoundCallerTest.UserGenericCallsKeepTheUnboundedModel`).
- Every shape the clause rejects among the known cases is sound for every valid implementation: 8.11.5's `Merge`, `zip` and
  `both` (G2), Peekable, Keep, filters that drop an item (§13, the review's attack table).
- The A5-F6 family has taken repeated fixes for one cause: S2, its 2f-S1 revision, the owner's G1 and G2 (R4-M76), the open G3
  and G4, 2x-S1's stored Φ, P12 and G113, R4-M68's F6 condition, E3's residue and PD30's interim. AGENTS asks for the analysis
  one level higher.

**Owning components.** Binding publishes Origins, retention and contracts; A5 forms and checks Loans by path; A4 decides
environment effects and `confined`. No component, representation or vocabulary entry is added.

**What becomes unnecessary.** Rule 1's Origin clause and rule 3; `preserves results` with its eligibility, conformance check,
delegation, Callable and slot forms, and Iterator's bound; P12; the planned F6 representation (carrier atoms, Φ(c, i), tags,
the receiver-Place relation, invalidation, Move carry, the earlier-result holder, descent into stores) and the old Analysis's
and Binding's implementation of the same rules (§11.3).

## 3. Rules

### 3.1 Abstract calls (8.11.4, 6.4.6, 8.11.6)

A requirement call without a known concrete callee, a call through a Callable-constrained value and a dynamic virtual call
take their effects from:

1. **Inputs:** the Places reachable by path from the receiver and arguments in each parameter's access mode, shared past a
   shared layer, raw layers included, also inside an abstract part, as today. An input reaches a Loan only through these Places,
   never because one of its Origins may denote it.
2. **Environment:** none with `confined`, otherwise unknown, conflicting with live mutable-static Loans (15.6.4), as today.

Their results and the writable storage their contract lets them write keep the Loans of 15.6.4. Nothing else relates two
calls: a result whose Type does not depend on the call's receiver Loan conflicts with a later call on the same receiver only
through Loans it keeps on Places that call reaches.

### 3.2 Effect bounds (8.11 and Appendices D, E, F)

The vocabulary is `confined` alone. The grammar loses `preserves results`; a source that writes it is a syntax error. 8.11.3
and 8.11.5 are deleted, 8.11.4 becomes 8.11.3 and 8.11.6 becomes 8.11.4.

### 3.3 Descending results (15.6.3:571, :575, :583)

One rule for every callee: a result that descends from the receiver carries every Loan that a read through the receiver
carries (15.6.3), as 15.6.4's per-argument retention gives it ("a result … that may retain an Origin inside a borrowed
argument's referent Type … carries every Loan that a read through the argument carries"); a projection such as `Self.Item`
may retain any Origin of `Self`, and "an unproven absence of retention never permits access". So a generic body that builds an
Iterator from a local Borrow still cannot write the source while an item lives (U1's guard rows). P12's two sentences
(15.6.3:571, 15.6.4:595) are deleted (Q1). 15.6.3:583 loses its `preserves results` exception. The split
sentence of 15.6.3:571 cites 5.2.2 instead of restating part of it (§4.2).

### 3.4 Destruction in a generic body (16.3.3:196)

Destroying a value of a Type parameter or projection not proven Copy is, for the body's own Loan checks, a destruction that
may observe a Loan (15.6.6) with unknown environment effects. It reaches Loans by path like any observing destruction, never
Loans on other roots. Under `confined` it remains an unclassifiable effect (8.11.2). This answers 2f-S1's G4 with its proposed
reading.

### 3.5 Iterators (22.2.1, 22.2.2, 22.2.3; LIBRARY)

`Iterator` is `LendingIterator` with `associate LendingIterator.LentItem(step) is Item`: its item names no Origin of `next`
(22.2.1), so keeping earlier items across later calls needs no bound. A LendingIterator item that names `step` still conflicts
with the next `next`. 22.2.3's first paragraph (independence: an exclusive item is a split child or a transferred value, and an
item of an Iterator reached through a borrow keeps no Loan of that borrow) stays as the implementations' obligation and is now
true for abstract calls too; its second paragraph is replaced (§5.9).

## 4. Soundness

### 4.1 L1: generic checking over-approximates every instantiation

A generic body "must be valid for every well-formed Type, length and Origin binding that satisfies its Constraints" (8.15.1).
For calls this holds when each abstract call is checked at least as strictly as the concrete call that an instantiation makes:

| Fact | Abstract call | Concrete call of a conforming implementation |
| --- | --- | --- |
| Argument Borrows | Formed by the requirement's modes | The same Borrows; the implementation accepts them (inputs `required <: implementation`, 15.3.7:362) |
| Retention | The canonical contract (15.6.4) | No more (results `implementation <: required`, 15.3.7:362) |
| Descent | What a read through the receiver carries (15.6.3, 15.6.4) | The same rule |
| Environment | Unknown unless `confined` | The implementation's summary; `confined` is verified (A4) |
| Destruction | Observing by path, unknown environment (§3.4) | The Type's destruction summary, observing by path |
| Callable call | Receiver and captures by path, the bound, the Callable signature | The closure's or Function Item's summary (8.11.6 table) |
| Dynamic virtual call | The slot's public contract and bound (6.4.6:347) | An override, held to the slot |

The abstract call rule is now the concrete one, so L1 for calls reduces to 15.3.7's compatibility, 8.15.1 and A4's
verification of `confined`. Unit U5 tests it mechanically (§11.1).

### 4.2 L2: the concrete call rule is sound

A concrete call is checked by its argument Borrows, retention and static summary. It is sound when no conforming body reaches,
through its inputs or the environment, a Place that a live value outside the call protects:

- **Safe access by path.** A Reborrow through an input is contained in the input's Origin up to the first shared layer (15.6.3),
  so a safe body cannot hand out a value that keeps such a Place beyond its receiver Loan; shared access grants no mutation
  (15.6.2), and `obj`, `rc` and `arc` payloads are own steps whose shared forms are read-only (5.2.1 condition 3).
- **Raw access.** A raw Place borrow promises exclusivity "while the result or any value keeping its Origin is used" (5.2.2),
  and every raw access must conflict with no live Loan of an overlapping Place, wherever the value carrying it is held
  (5.2.1:58). This is a run-time obligation over all accepted programs, not over what the body sees.
- **Environment.** Unknown or summarized environment effects meet live mutable-static Loans (15.6.4, rule 2), and a Borrow of
  mutable static storage never leaves its body (15.6.4, P6).

15.6.3:571's "never lending overlapping Places to two live exclusive children" covers only exclusive children. A shared child of
a Place the remainder may still lend exclusively (a `peek` returning `ref/E during source` for the next element, then a
`consume` that writes it, c43) is forbidden by 5.2.1 condition 4, not by that sentence, so the sentence cites 5.2.2 (§5.6).

L2 is the assumption every concrete program already makes. It stays argued, as S2's H2 is, and depends on A5's containment
(15.6.3, unit 2a) and on G160's addresses and raw Origins (15.6, 5.7). The default pipeline does not yet meet these premises
(G160, G184, G177, G192, G196 and G193 are open there); this proposal widens its acceptance on top of them, but concrete callers
share every one of those gaps, so no new class of unsoundness follows (H).

### 4.3 Consequence

If L1 and L2 hold, every generic program this proposal accepts is sound for every valid instantiation. A counterexample either
breaks L1, where the abstract model is weaker than a published contract (fix the contract comparison, retention or mode), or
breaks L2, which affects concrete code as well (fix containment, a raw obligation or A5). `preserves results` only excludes
conflicts, so it cannot repair either. The fallback for a counterexample is therefore a narrow restriction on the base rule for
its form, never a reintroduced exclusion.

**Without P12.** P12's cases 36c, 36e and 36g were labelled UNSOUND in prototype rounds 8–9 for accepting what the SPEC of
the time rejected: a later abstract call (`a.inner.clear()` in 36c) reached the phantom Φ_c, and P12's parents were the only
Loans on its path. Without Φ_c the later call reaches only paths that meet no Loan the earlier result still carries, so 36c
and 36g take their concrete twins' verdicts, and 36e keeps its 15.6.7 error at the write (H). INTEGRATED's E8 safety note
assumes, "as §15.6.3 and P12 make true", that every value carrying a Loan also carries its parent Loans; a descending result
carries no child of the receiver's route (its receiver Loan is excluded), so 15.6.3's Borrow rule and 15.6.4's whole-argument
retention keep that assumption (H). U0
adds a restated E8 safety note under this intake; the 2026-10-17 entry stays.

### 4.4 Unsafe code that relied on rule 1

Existing accepted programs gain no new executions; only programs that become valid can expose unsafe code. Today an unsafe
implementation that acts again on a Place it lent, while a value keeping the lent Origin may be used, breaks 5.2.2 whenever an
accepted program keeps that value; a concrete caller already can (15.6.3:583). So the code at risk is unsafe code that no
accepted program can call concretely in that pattern:

- conformance witnesses of a Type that is never named outside generic code (internal Types, private conformances);
- overrides reached only through dynamic virtual calls;
- Closures and Function Items passed only to generic code;
- implementations behind a runtime-Contract view.

The audit (U0) checks every program of §7's kind B, and every one run natively, for undefined behavior, and searches for code
that acts, through the authority a raw lend was taken from, on a Place it lent: a raw borrow (`@ref`, `@uniq`, `@objref`,
`@objuniq` of a raw Place) whose result leaves the body by return, store or capture, and a raw or safe read, write, Take, Move,
reset or clear, the Type's own `drop`, or a re-lend in another method that may reach that Place while the lent Origin may be
used.

**Library (read at the base):** the raw borrows are `Storage.splitFirst` (`Storage.kimi:115`, `:123`), its Dictionary variants,
the pair split (`Storage.kimi:205`, `:208`) and `UniqSlice.index`/`indexUniq` (`UniqSlice.kimi:39`, `:45`). The first three lend
`during state.source` from concrete iterators' public `next` and internal concrete helpers; the last two lend `during self`.
None is reachable only abstractly, so the library relies on nothing rule 1 gave (H, by reading).

## 5. SPEC text

Each site gives the current text and the new text. Anchors to 8.11.4 and 8.11.6 become anchors to 8.11.3 and 8.11.4, and their
link text changes with them (`#8114-checking-and-use` → `#8113-checking-and-use`, `#8116-callable-effect-bounds` →
`#8114-callable-effect-bounds`): 15 and 10 links in `docs/spec`, `docs/impl` and `docs/LIBRARY.md` at the base, 13 and 9 after
the deletions of 8.11.5 and P12's sentence. Numeric citations outside SPEC follow in U3 (§11.1).

### 5.1 8.11 (:666)

- Now: "The vocabulary is closed: `confined` and `preserves results`."
- New: "The vocabulary is closed: `confined`."

### 5.2 8.11.1 (:679–704)

- The example's `contract StableTake: Take` with `effect Take.take preserves results` becomes `contract QuietTake: Take` with
  `effect Take.take confined`; `takeTwo` moves to 8.11.3 (§5.4).
- :704 "With only `S is Take`, the second `take` is rejected while `first` is live." is deleted.

### 5.3 8.11.2 (:721, :722, :724)

- :721 and :722: "An effect that cannot be classified: a conflict under either bound." becomes "An effect that cannot be
  classified: a conflict under `confined`."
- :724: "A foreign call also makes the accesses its signature permits through its arguments; a `preserves results`
  implementation may call a foreign function when those accesses do not conflict with the Loans of earlier results." becomes
  "A foreign call also makes the accesses its signature permits through its arguments."

### 5.4 8.11.3 deleted; 8.11.4 becomes 8.11.3 "Checking and use" (:785–812)

8.11.3 (:770–781, eligibility, guarantee and the `Peek` example) is deleted. In the renumbered 8.11.3:

- Rule 1 (:789). Now: "1. *Through inputs:* every Place reachable from the receiver and arguments in each parameter's access
  mode, only shared past a shared layer ([§15.6.2](…)), raw layers included, also inside an abstract part ([§5.2.1](…)); an
  abstract input may reach every Loan its Origins may denote; destroying an owned handle argument adds [§16.3.3](…)." New: "1.
  *Through inputs:* every Place reachable from the receiver and arguments in each parameter's access mode, only shared past a
  shared layer ([§15.6.2](…)), raw layers included, also inside an abstract part ([§5.2.1](…)); destroying an owned handle
  argument adds [§16.3.3](…). As for any call, an input reaches a Loan only through these Places ([§15.6.2](…)), never because
  one of its Origins may denote it."
- Rule 3 (:791) is deleted.
- :793. Now: "A possible conflict with a live Loan ([§15.6.2](…)) is an error at the later call." New: "A possible conflict with
  a live Loan ([§15.6.2](…)) is an error at the call."
- After the `Feed` example (:812), add:

  ```kimi
  // Take is the contract of §8.11.1.
  func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
      S is Take
      let first = source.take()
      let second = source.take()   // Valid: first keeps no receiver Loan, and the call reaches only source's Places.
      return (first@move, second@move)
  ```

  followed by: "A result whose Type depends on its call's receiver Loan, such as a LendingIterator item `ref/E during step`
  ([§22.2.1](…)), keeps that Loan, so a later call that conflicts with it, such as an exclusive call on the receiver, is an
  error while the result is live."

### 5.5 8.11.5 deleted; 8.11.6 becomes 8.11.4 "Callable effect bounds" (:841–856)

8.11.5 (:814–837, delegation, `Drain` and `Merge`) is deleted. In the renumbered 8.11.4:

- :841. Now: "Each kind appears at most once in one Constraint; `preserves results` needs receiver `ref` or `uniq`." New:
  "`confined` appears at most once in one Constraint."
- :843. "must satisfy each bound" becomes "must satisfy the Constraint's bound"; the last sentence, "`preserves results`
  excludes conflict with Loans of earlier results of the same callable value.", is deleted.
- Table (:850, :851). "Slot bound, for `confined` only ([§6.4.6](…))." becomes "Slot bound ([§6.4.6](…))."; "Available bounds,
  for `confined` only." becomes "Available bounds."
- :856: "under rules 1 to 3 of [§8.11.4](#8114-checking-and-use)" becomes "under rules 1 and 2 of
  [§8.11.3](#8113-checking-and-use)".

### 5.6 15.6, 15.6.3 and 15.6.4 (:450, :571, :573–581, :583, :595)

- :450: the link "([§8.11.4](…))" becomes "([§8.11.3](…))".
- :571, P12. The sentence "When the call is a requirement call without a known concrete callee ([§8.11.4](…)) or a call
  through a Callable-constrained value ([§8.11.6](…)), the value also carries every Loan of the receiver argument except the
  receiver Loan of the call ([§8.11.3](…)), such as the parent Loans of a Reborrowed receiver." is deleted (Q1 (a)).
- :571, split. Now: "… each child is a `uniq` borrow of a raw Place returned `during state.source`, and never lending
  overlapping Places to two live exclusive children is the unsafe obligation." New: "… each child is a `uniq` borrow of a raw
  Place returned `during state.source`, and keeping the promise of [§5.2.2](…) for every child is the unsafe obligation."
- :573–581, the `takeFirst` example, becomes:

  ```kimi
  // Take is the contract of §8.11.1.
  func takeFirst<S>(h: uniq/(uniq/S during s, i32)) -> Option<S.Item>
      S is Take
      var r = h@follow@uniq
      let first = r.0.take()  // Descends from the receiver r.0: keeps what a read through it carries, not r's Loan on h.*.
      h.1 = 5                 // Valid: no live value carries r's Loan.
      return first@move
  ```

- :583. Now: "A result may be kept across later calls on the same receiver when its Type does not depend on its call's
  receiver Loan and the callee's effects (for `preserves results`, its effect bound, [§8.11.3](…)) do not conflict with it."
  New: "A result may be kept across later calls on the same receiver when its Type does not depend on its call's receiver Loan
  and no later call's effects conflict with the Loans it keeps, whether the callee is concrete or abstract ([§8.11.3](…))."
- 15.6.4 :595, P12's second sentence (SPEC commit `0779b975` added it with the first): "A result that descends from the
  receiver of a call that [§15.6.3](#1563-reborrowing-and-region-splitting) names also carries the Loans it adds there." is
  deleted (Q1 (a)).

### 5.7 6.4.6 (:347, :349)

- :347: "its effects follow rules 1 to 3 of [§8.11.4](…)" becomes "its effects follow rules 1 and 2 of [§8.11.3](…)".
- :349 (the `preserves results` paragraph) is deleted.

### 5.8 12.7.1 (:218) and 16.3.3 (:196)

- 12.7.1 :218, "Exclusive input bound": the anchor follows §5; its "at most as … rule 1 permits" now excludes the Origin reach.
  The bound concerns completeness of what is reached through the receiver, which the Origin clause never granted, so no
  verdict changes (H).
- 16.3.3 :196. Now: "In a generic body, destroying a value of a Type parameter or associated-Type projection is likewise unknown
  for the body's own Loan checks, unless the Type is Copy; the body's summary publishes it as a parameter-dependent effect that
  callers compose with the actual Type ([§4.9.6](…)), unless [§15.6.4](…) widens it." New: "In a generic body, destroying a
  value of a Type parameter or associated-Type projection not proven Copy is, for the body's own Loan checks, a destruction
  that may observe a Loan ([§15.6.6](…)) with unknown environment effects; the body's summary publishes it as a
  parameter-dependent effect that callers compose with the actual Type ([§4.9.6](…)), unless [§15.6.4](…) widens it."

### 5.9 22.2 (:78, :96, :117, :135, :141)

- :78: the line `    effect LendingIterator.next preserves results` is deleted from `contract Iterator`.
- :96. Now: "An item without `step` follows the ordinary Loan rules ([§15.6.3](…)), but generic code keeps it across the next
  `next` only under a `preserves results` bound." New: "An item without `step` follows the ordinary Loan rules
  ([§15.6.3](…))."
- :117: "and to `Iterator` exactly when `I` does, with the same `Item`, dependencies and effect bound" becomes "and to `Iterator`
  exactly when `I` does, with the same `Item` and dependencies".
- :135. Now: the paragraph beginning "`preserves results` ([§8.11.3](…)) means that the effects of `next` conflict with no Loan
  kept by an item the same Iterator returned earlier …" and ending "… is only a LendingIterator." New: "Keeping earlier items
  across later calls needs no bound: a later `next` reaches what its receiver Borrow reaches ([§8.11.3](…)), and an item keeps
  no Loan of that Borrow. Every implementation, including user Types that declare `Self is Iterator`, returns nothing
  contained in its receiver's Origin ([§15.6.3](…)) and keeps the promise of [§5.2.2](…) for every item it lends from raw
  storage. Destroying a
  whole Iterator is a destruction that may observe a Loan ([§15.6.6](…)). Unknown and parameter-dependent effects follow
  [§15.6.4](…), [§16.3.3](…) and [§4.9.6](…). A wrapper that borrows itself for its result is only a LendingIterator."
- :141: "// preserves results permits retaining first." becomes "// Valid: first keeps no receiver Loan."

### 5.10 Appendices

- D :49: "effect vocabularies beyond `confined` and `preserves results`" becomes "effect vocabularies beyond `confined`".
- E :48: "… or a virtual slot: `confined` or `preserves results`." becomes "… or a virtual slot: `confined`."
- E :91 (the `preserves results` row) is deleted.
- F :144. Now: "| `effect` `confined` `preserves` `results` | `effect` when it starts an item and is followed on the same line by
  `confined` or `preserves`, or, in a Contract item, by a ContractSelector; `confined`, `preserves` and `results` only after
  `effect`. …" New: "| `effect` `confined` | `effect` when it starts an item and is followed on the same line by `confined`, or,
  in a Contract item, by a ContractSelector; `confined` only after `effect`. …"
- F :398: `EffectBound := "confined" | "preserves" "results"` becomes `EffectBound := "confined"`.

### 5.11 Documents outside SPEC

- LIBRARY :104: the `effect` line is deleted. :107. Now: "Iterator instead publishes a step-independent Item and declares
  `preserves results` for `next` (…): a later `next` on the same iterator conflicts with no Loan an earlier item keeps, so
  callers may retain earlier items across later calls. LendingIterator declares no bound. External source dependencies still
  apply." New: "Iterator instead publishes a step-independent Item, so an item keeps no Loan of the borrow its call forms on the
  receiver and callers may retain earlier items across later calls
  ([§22.2.3](spec/22-core-execution-and-foreign-functions.md#2223-iterator-independence)). External source dependencies still
  apply." :597: ", and declares `preserves results` for `next`, which every conformance is checked against" is deleted.
- IMPL Appendix A :12: the anchor follows §5.
- DIAGNOSTICS, STATUS, CODEMAP and the explicit-scope citations: §11.1.

## 6. Examples

Valid under this proposal:

```kimi
func zip<I>(left: uniq/I, right: uniq/I) -> (Option<I.Item>, Option<I.Item>)
    I is Iterator
    let a = right.next()
    let b = left.next()          // Valid: left's call reaches left's Places, not a's Loans (an error under G2 (a)).
    return (a@move, b@move)

struct Merge<J>                  // 8.11.5's Merge without StableTake; with it, 8.11.5 rejects self.left.next().
    J is Iterator
    Self is Take
    associate Take.Item is J.Item
    var left: J
    var right: J
    public func take(self: uniq/Self) -> Option<J.Item>
        match self.left.next()   // Valid: an ordinary body.
            .Some(let item) => return .Some(item@move)
            .None => return self.right.next()

func again<I>(it: uniq/I) -> (Option<I.Item>, Option<I.Item>)
    I is Iterator
    var r = it@follow@uniq
    let first = r.next()
    let second = it.next()       // Valid: first keeps no Loan of r (an error under Q1 (b)).
    return (first@move, second@move)
```

Still errors, for the reasons named:

```kimi
func lend<L>(it: uniq/L) -> ()
    L is LendingIterator
    let a = it.next()
    let b = it.next()            // Error: a's LentItem(step) keeps the receiver Loan of the first call.
    use(a)

struct Cursor {source}           // G193's forms: containment, now also on the default pipeline.
    Self is Iterator
    associate Iterator.Item is ref/i32 during source
    var value: uniq/i32 during source
    public func next(self: uniq/Self) -> Option<ref/i32 during source>
        return .Some(self.value) // Error (UnprovenOriginRelation_Kd): the Reborrow is contained in the receiver's Origin.

struct Slot<T>                   // 8.11.2, unchanged.
    Self is Reset
    var item: Option<T>
    public func reset(self: uniq/Self)
        self.item = .None        // Error: destroying the old T has unknown effects under confined.
```

## 7. Verdict changes

A census read every test (C# sources and the Kimi programs in their strings, native fixtures, `spec-cases.json`,
`syntax.json`, `classification.tsv` with the ledger sources of `artifacts/verify/mir-diff/bu18-head`), the milestones, the
library and the SPEC examples at `bbae6089`. Each verdict is a reading (H) unless a test already pins it; the review checked
the sums. Kinds: (A) the program declares `preserves results`, which becomes a syntax error; (B) error → valid; (C) valid →
error; (D) verdict unchanged, subject gone; (E) unchanged.

| Kind | xUnit rows | Main sites |
| --- | --- | --- |
| A | 79: 21 become valid once the clause is removed (A→B), 34 keep their verdict, 23 lose their subject (declaration, eligibility, parse, API and Hover tests), 1 becomes D | `EffectBoundCallerTest`, `EffectBoundDeclarationTest`, `EffectBoundImplementationTest`, `EffectBoundReportTest`, `EffectBoundSyntaxTest`, `CallableEffectBoundTest`, `CallReservationTest` (concrete callers, unchanged), `Virtual*Test`, `RequirementFunctionItemTest`, `RequirementIdentityTest`, `LspEffectHoverTest`, `KimiLibraryShapeTest` |
| B | 47 without a declaration (`IteratorOriginEffectsTest` 46, `CallableEffectBoundTest` 1), plus the 21 A→B rows and the spec case below | Conformances that drop an abstract item while an earlier result lives, Merge-like wrappers, writes reaching item Loans, unbounded requirement calls; Take-only `takeTwo`, `tupled`, `through`, `Merge`, `Swapped`, `Lending`, `Topped`, R1g's rows |
| C | **0** | Under Q1 (a) no program changes from valid to error (Appendix A for (b)) |
| D | 22 named (the census counted 23) | `IteratorIndependenceTest` (10 rows whose containment error stays), `IteratorIndependenceEditingTest` (3), `IteratorOriginEffectsTest` (4), `VirtualEffectSelectionTest` (3), `StorageBoundaryTest` (1), `EffectBoundImplementationTest.PublishedOperationBodiesStayWithinTheirRows` (its `preserves` half) |
| E | the rest | Every `Mir*Test` row: MIR already follows rule 1 by path |

- **Implementation-only changes (§1, G193).** 15 rows (10 in `IteratorIndependenceTest`, 5 in `IteratorOriginEffectsTest`),
  plus 3 (H) in `IteratorIndependenceEditingTest`'s unedited states, expect valid today only through G193's skip: they return a
  Reborrow through `self` of a `uniq/X during source` Field, a containment error under SPEC 15.6.3 now and under this proposal.
  They become errors with U2.
- **Tests already over-strict.** `EffectBoundCallerTest.UserGenericCallsKeepTheUnboundedModel` (:54–55) expects
  `CallEffectConflict_Kd` for user generic functions with concrete callees, which the SPEC already accepts (`classification.tsv`
  `a5-old-effects` (a)); `zip` and `both` (:42–43) are accepted by the old Analysis although G2 (a) makes them errors. All stay or
  become valid.
- **Ledger.** `a4-preserves` (35 sources): 34 B, 1 D (containment, `3508c37bbfe8`). `a4-binding` (39 rows): 12 B, 15 D, 9 A
  (+3 (H)). `a4-binding-limit` (1) and `a4-callable-preserves` (5): A. `a5-old-effects` (9): 7 B, 2 E.
- **SPEC examples.** No code block goes from valid to error. 8.11.5's `Merge` becomes valid (as a `Take`); 8.11.1's Take-only
  `takeTwo` (prose, :704) becomes valid; `Drain`'s example disappears with 8.11.5; `nextPair` keeps its verdict; 15.6.3's
  `takeFirst` with `h.1 = 5` becomes valid, so spec case `15.6.3-abstract-descent` changes its expectation and loses its Old (d)
  note (the old Analysis already accepts it).
- **Library and milestones.** `Iterator.kimi:6` only; milestones 0 (12, 13, 14, 29, 34, 35, 37 and 44 read), `tests/mir` and the
  workloads 0.
- **A reading of today's rule 3.** It excludes the effects of the later call of the same requirement on the same value,
  including its receiver Borrow (S2 §2.1, plan row 2f-4), not a fresh caller-side Borrow of another argument: `a =
  it.take(s@uniq); b = it.take(s@uniq); use(a)`, with `a` retaining `s`, is an error today and under this proposal (H).
- **Case ids in the corpus.** c13, c14, c15/h1, c16/33d, c17, c18 (all four rows), c30, c31, c32, c35, c50 are B; c11, c12, c19
  (test), c20, c21, c28, c29, c52, h5 are E; c27 is A; c34 and K are D. The other 2f-S1 and spec-readings ids, and 36a–36h, exist
  only as probes or prototype cases.

## 8. Principles

- **One Concept, One Canonical Form.** One call rule for concrete and abstract callees: reach by path, retention and descent by
  the published contract, environment by A4. Loans protect; paths and raw authority grant access. The effect vocabulary shrinks
  to `confined`.
- **Local Reasoning.** A call is judged by its arguments, its published contract and its environment effects. No relation
  between two calls, no history of a receiver Place and no body of an implementation is consulted at a caller; a conformance is
  an ordinary body plus signature compatibility and `confined`.
- **Explicit Semantics.** A result's dependence on its receiver is stated by its Type (`step`, an elided `self` Origin, a
  receiver Origin); retention by the public contract; the unsafe obligation by 5.2.1 and 5.2.2, now cited where splits are
  defined.
- **Compiler Server Protocol.** A rejection is an ordinary Loan conflict with two locations (the Borrow and the use that keeps
  it live); the earlier-call and invalidating-operation locations of S2 are not needed.

## 9. Options considered

Designs written independently and compared: A (S2, 2026-10-16, revised by the 2f-S1 sketch, 2026-10-19, each reviewed in its
own session) and C (this proposal). B and the variants below were compared in the owner's conversation and by this proposal's
reviews.

| Option | Core | Removes | Remains hard | Verdict |
| --- | --- | --- | --- | --- |
| A: S2 revised by 2f-S1 (OQ3 (a), conditional) | A receiver-Place relation between calls of one requirement | Value identity | Tags, invalidation, Move carry, delegated tags, results vs stores, carrier atoms, an earlier-result holder; G3, G4 open; S2's losses 1–5 | Rejected: it keeps the cause and adds the most mechanism |
| B: a separation guarantee | `preserves results` guarantees a result and the receiver's remaining authority are compatible | The call history | A check over other methods and destruction; it restates 5.2.2 as an effect | Rejected: a second copy of the ownership rule |
| Keep rule 1's Origin clause for some forms | A whitelist of forms that stay excluded | Part of the mechanism | Which forms; why they would be unsound when concrete calls are not | Rejected without a counterexample (§4.3) |
| C with P12 (the first draft; Q1 (b)) | C, keeping 15.6.3:571's abstract descent | All of A's mechanism | P12's only justification was rule 1's reach; generic and concrete descent differ; a descent unit with a new Binding fact; one valid → error shape; (d) differences on the default pipeline | Not chosen (Q1 (a)) |
| **C: reach by paths** | The concrete call rule for every call | Rule 1's Origin clause, P12 and everything that excused or completed them | L1 and L2 stay argued; the unsafe exposure (§4.4) | **Adopted** |

## 10. Decisions superseded and records

On adoption, U0 records in one commit (DECISIONS entries name the reason that no longer holds; superseded entries stay):

- **DECISIONS:**
  - R4-M74 OQ3 (a), S2's conditional adoption, and OQ2's 2f-D package: S2 exists to excuse rule 1's Origin clause.
  - R4-M76 G1 (ii′) (a conformance's earlier result): no earlier-result check remains. G2 (a) (values of one abstract Type share
    Origin atoms for rule 1): reversed.
  - R4-M35's approval of A3 §9 item 10 (P12) and INTEGRATED 2026-10-17's P12 record: its justification was rule 1's reach (Q1).
  - R4-M22's assignment of `preserves results` to A5 and its unit A5-3 (U9); A4's `confined` is unchanged.
  - R4-M72's Effects bullet, its Φ_c half (`CallEffectConflict_Kd` as A5-F6's code), with PD7 (Q2).
  - R4-M68's F6 condition (a shared-receiver Φ_c's mode).
  - R4-M94 OQ-2 (a)'s timing for G193's skip: U2 removes it (§1).
  - Residues that name F6, Φ_c or 2f-D: R4-M53 ("`Map`, for disturbance (F6)"), R4-M73 (F6's Φ_c producer), R4-M92 and R4-M98
    (2x-R9's 2f-D condition), R5-M56 (the `a4-preserves` and `a4-callable-preserves` rows).
  - The freeze exception (§11.2).
- **A5 step 2 plan** (`draft/Proposals/2026-10-18 A5 Step 2 Implementation Plan.md`, its proposal text frozen where
  integrated; the changes go to its DECISIONS and PLAN records): PD3 (2x-P6), PD5 (2f-D), PD6 (C5 units 2f-1, 2f-2), PD7, PD16,
  PD23 (the stored Φ_c reading), PD30's S2 interim, PD31 (A5-F6's Φ_c facts), X-5's A5-F6 prototype-parity cases, the §3.6 rows
  and the F6 rows 2x-P6, 2f-S1, 2f-S2, 2f-D, 2f-1 … 2f-7, all replaced by §11. The Complete Polonius A5 plan's §5.3 S2 row and
  App. A row 10 (checked by X-5) close.
- **2f-S1:** G3 (moot: no Origin reach remains) and G4 (answered by §3.4).
- **PLAN** (U0 records the position and plan changes; each closure lands with the unit that completes it): G113's F6 part closes (P12's descent into results and stores; its E12 part stays); G193 closes with U2, its "not
  miscompiles" corrected; G78 loses its earlier-result exclusion and its 8.11.6 citation; line 11's pointer to G78 and line 115's
  "value-flow delegation" follow; R1g closes with U1; G106 loses A5-F6's signatures; F6 leaves R4's family list and the exit gate
  counts §11's units.
- **STATUS:** the "Callable effects" row, row 27 "Virtual calls" ("earlier-result identity exclusions") and line 5's mention
  of loss P12 change in U2, when the boundary changes.
- **R5 and R6 plan:** its ask A-7 and row 13 / H5 ("need A5-F6 and A5P-S2", `EffectSummary` about 1,985 lines) change; U0
  notifies R5's planning session.
- **`draft/INTEGRATED.md`:** the intake with this proposal's mapping; S2 to `draft/Obsolete` (完了) and the A5P record's "Not
  integrated: S2" closed; a restated E8 safety note without P12 under this intake (§4.3), the 2026-10-17 entry unchanged;
  DECISIONS R4-M39's link to S2 updated to its new path.

## 11. Implementation plan

The inventory was read at `bbae6089` (docs and library equal to the base). Sizes count non-blank lines and are approximate.

**Where the rules live today.**

- **MIR (A5, A4).** `MirBorrows*` already checks abstract calls as concrete ones: argument Borrows by path and retention rows
  from `TyCtxt.Retains`, with no requirement-specific branch. A4 reads `.Confined` from the `(Confined, Preserves)` pairs of
  `TyCtxt.DispatchBounds` and `TyCtxt.CallableBounds`. No F6, Φ or `preserves` code exists, and P12 is not implemented.
- **Old Analysis (default pipeline).** `OwnershipAnalysis.RequirementEffects`/`RequirementEffect`/`ValueIdentity`
  (`OwnershipAnalysis.cs:1672–1765`) record one `EffectRegion` Place per abstract region Type, for requirement calls and for
  every user generic function without a published summary; `OwnershipAnalysis.Closures.CallableEffects` (`:219–278`) does the
  same for Callable calls. `OwnershipBody.VerifyRequirementEffects` and `HeldRequirementConflict`
  (`OwnershipBody.Borrows.cs:895–1029`) apply rule 1's reach to any related value and rule 3's exclusion by value identity, and
  produce `CallEffectConflict_Kd` (its only producer) or R1g's `Unsupported_Kd` for Callables. P12 is not implemented (spec case
  `15.6.3-abstract-descent`, Old (d)).
- **Binding.** `EffectSummary` with `preserves = true` checks every `Self is Iterator` conformance and every `preserves` slot and
  Callable evidence (`Binding.EffectBounds.cs`, about 570 of its 1,884 non-blank lines), with eligibility and reporting in
  `Binding.EffectDeclarations.cs` (about 145). `Binding.PublishesIndependentResults` (`Binding.ArgumentOperations.cs:267–271,
  326–375`) skips containment for those results (G193).
- **Parser, catalog, Hover.** `Parser.Effects.cs:89–93`, `EffectBoundKoto`, `Constants.PreservesKeyword`/`ResultsKeyword`; the
  `preserves`-only requirement and failure rows; `HoverBuilder.Effects.cs`.

### 11.1 Units

U1, U2 and U3 hold BU and adv files (`Binding.ArgumentOperations.cs`, `TyCtxt.Effects.cs`, `MirDifferenceLedger.cs`), so
under PD29 each unit takes its own review; U0 changes SPEC, DECISIONS and COMPILER_ARCHITECTURE, which keep their review. Each
unit is verified and pushed. U0, U1 and U2 land in one dev merge after their three reviews, so dev never has the SPEC without
the default pipeline's change, never parses `preserves results` after the SPEC drops it, and never runs without the
`preserves` verification while G193's skip stays; U3, U4 and U5 follow in that order. A final review covers U0–U5 after U5.
None edits `MirBorrows*`, so none is on A5's chain (PD1).

| Unit | Content | Size | Checks | Owner, review |
| --- | --- | --- | --- | --- |
| U0 | SPEC intake (§5, the anchors, LIBRARY, IMPL App. A) and the test data it invalidates: in `spec-cases.json`, `8.11.3-preserves-results` deleted (its block is gone), `8.11.6-callable-effect-bounds` re-anchored to 8.11.4, `15.6.3-abstract-descent`'s `h.1 = 5` variant deleted (the line is no longer commented) and its family retagged to a landed one (or `F6` added to `VerifiedSpecExampleTest.Landed`, whose only F6 case it is); the `classification.tsv` clause cells (`a4-binding-limit`, `a4-callable-preserves`, `a4-x2`); records (§10), COMPILER_ARCHITECTURE's freeze exception, this file to `draft/Changes`; the unsafe audit (§4.4) recorded in the intake | docs, data | `VerifiedSpecExampleTest`, `MirCensusTest`, `MirBorrowsTest.SpecVariants`, `MirDifferenceTest` | Planning session; own review |
| U1 | Old Analysis: delete the `EffectRegion` Place kind, `RequirementEffects`/`RequirementEffect`/`ValueIdentity`, `Closures.CallableEffects`, the region part of `AddExplicitDependencies`, `VerifyRequirementEffects`, `HeldRequirementConflict`, `RelatedValues`/`RootsRelated`/`HasIssueAt`, `HasExplicitDependency`, `ReportCallEffect` and their model records; abstract calls fall to the ordinary Type-Origin dependencies. Retire `CallEffectConflict_Kd` with its only producer (Q2 (a)): the catalog entries, DIAGNOSTICS rows 158 and 301, the `a5-old-effects` code and requirement cells, `MirDifferenceLedger.cs:71`. Add the spec case `8.11.3-checking-and-use` for 8.11.3's `takeTwo` with no expected problem (here, because the old Analysis rejects it until this unit). First add default-pipeline guard rows that are errors today and must stay errors: a generic body that builds an Iterator from a local Borrow and writes the source while an item lives, directly, through a Reborrow, by re-assigning the Iterator and through an aggregate (the review's probes p6–p9), with their concrete twins | about −400, tests about +40 | Unit checks of the changed tests and the guard rows; ledger run. Done when every verdict U1 changes is in §7 or classified SPEC-valid with its clause, including the two conservative paths `HasExplicitDependency` fed (`OwnershipBody.Borrows.cs:1661`, `OwnershipBody.LocalRegions.cs:118`); a form newly accepted that SPEC does not make valid stops the unit for the owner (a located `Unsupported_Kd` guard as BU-10, or a recorded difference) | R4; own review |
| U2 | Binding, grammar and library: delete `EffectSummary`'s `preserves` half (delegation, earlier-result scan, stored values, result access), the `preserves` parts of `AvailableEffectBounds`, `ReportEffectViolation` and `ValidateDeclaredEffectBound`, `PreservesResultsRejection`, the slot and Callable `preserves` checks, `PublishesIndependentResults` and its skip in `PathReborrowOrigin` (G193); retire the `preserves`-only requirement and failure rows (`NoBorrowedReceiver`, `DependentResult`, `CallableReceiver`, `ResultLoan`, `UnclassifiedAccess`, `DelegationFailure`) and remove the `preserves` parts of DIAGNOSTICS rows 156, 157, 336 and 342; `EffectBound := "confined"` in the Parser and `syntax.json`; Hover's `preserves results` text; remove `Iterator.kimi:6`; STATUS's rows (§10); CODEMAP row 90, whose members U1 and U2 delete; a native fixture with a Take-only `takeTwo`, `zip` and `Merge` | about −810 | Unit checks; `KimiLibraryShapeTest`; the native fixture (explicitly selected); ledger run | R4; own review, BU reviewer (PD6) |
| U3 | Behavior-preserving vocabulary cleanup: delete `EffectBoundKind` (one member remains) and turn the `(Confined, Preserves)` pairs into the `confined` flag in Binding, `TyCtxt.Effects`, `SemanticQuery`, `MirEffects*` and Hover; the mechanical citation pass in code and test comments (8.11.4 → 8.11.3 first, then 8.11.6 → 8.11.4; about 40 comments), STYLE.md:116, SPECJ_TERMINOLOGY.md:30 and PLAN G113's E12 text if it cites rule 1 | about −35, 40 edited, comments | Unit checks; the whole test build. Done when no kept code, test or document cites the deleted 8.11.3 or 8.11.5 or an unrenumbered 8.11.4 or 8.11.6 (each grep hit read) | R4; own review, BU reviewer |
| U4 | Records: `classification.tsv` rows `a4-preserves`, `a4-binding`, `a4-callable-preserves`, `a4-binding-limit`, `a4-requirement` (its part), `a5-old-effects`; `codegen-pending/C-frame.tsv:139` | data | `MirDifferenceTest`; diff review | R4; own review (test data) |
| U5 | L1 check: a test theory that instantiates the generic cases of §7, U1's guard rows and the 2f-S1 and spec-readings ids that exist as tests with concrete Types, and asserts on MIR and on the default pipeline that each generic verdict is at least as strict as its instantiations' | tests about +100 | The theory | R4; own review |

### 11.2 Migration (b)

**The freeze exception**, as U0 adds it to COMPILER_ARCHITECTURE "Freeze (D5)": "and the owner's exception for Abstract Calls
Reach by Paths (2026-10-22): the old Analysis, Binding's `EffectSummary` and `PublishesIndependentResults` drop rule 1's Origin
reach, rule 3 and `preserves results` (units U1, U2), so the default pipeline accepts the forms that proposal makes valid and
rejects G193's forms by containment; a form U1 or U2 newly accepts that SPEC does not make valid stops the unit for the owner
(DECISIONS, the proposal's adoption entry)."

- **Generation.** The newly accepted programs use calls the old Emission already generates; U2's native fixture shows it, as
  the G147 (2b) exception required its whitelist to.
- **Differences left.** Under Q1 (a) the old Analysis and MIR agree on every §7 verdict (H); U1's completion condition, its
  guard rows and U5's theory on the default pipeline find any other. The default pipeline's open gaps that L2 assumes closed (§4.2) stay listed in PLAN and STATUS as today.

### 11.3 Deletions

About 1,200 compiler lines: Binding about 780, old Analysis about 390, Parser about 18, the diagnostic catalog about 13, Hover
about 12, MIR about 7 edited; library 1 line. Added: U1's guard rows and U5's theory (tests about +140) and one native fixture. Planned and no longer needed: 2x-P6 (about +480 draft lines), 2f-D and all A5-F6
units 2f-1 … 2f-7 (planned net about +487, tests about +680), with 2f-6's record work folded into U4. Tests:
`EffectBoundCallerTest`, `IteratorIndependenceTest` and most of `IteratorOriginEffectsTest` lose their subject (§7).

### 11.4 Tests and measurements

- **Tests.** Each verdict change of §7 is re-expected in the unit that causes it, citing this proposal's adoption (AGENTS: never
  weaken an expectation except for a SPEC revision, stated in the commit). Kept subjects move to the owning class at the lowest
  public layer: rule 1 by path (`takeTwo` Take-only, `zip`, a LendingIterator item with `step`) to `MirBorrowsTest` and the
  default-pipeline acceptance class; containment for the G193 forms to the Origin-relation tests; `confined` tests unchanged.
- **Measurements.** U1–U3 delete code on Binding's and Analysis's paths: the compiler pipeline series and warm and cold Bind
  allocation (VERIFICATION) before and after, with allocation regressions updated where they fall. No generated-code change is
  expected.
- **Gate.** R4's exit counts U0–U5 in place of G:A5-F6; U4 reclassifies the ledger's A5-F6 rows.

## 12. Not verified

- Nothing was built or run by this proposal; the review's probes p1, p3, p4 and p6–p11 used a `Kimi.dll` of 2026-10-17 (the
  default pipeline before U1). L1 and L2 are
  argued, not prototyped (U5 tests L1); L2 is S2's H2 without the conformance step.
- The removal of P12 (§4.3), the E8 restatement and 12.7.1's unchanged verdicts are readings.
- §7's verdicts are readings of the census, except where a test already pins them; the containment readings behind D and G193's
  18 forms are (H).
- The library audit (§4.4) is a reading; the test, milestone and kind-B halves are U0's.
- The default pipeline's open gaps (§4.2) are shared with concrete callers (H).
- Open, outside this proposal's cause: with P12 gone, 15.6.3's descent sentence ("carries every Loan that a read through the
  receiver carries") restates 15.6.4's per-argument retention; reducing it to a cross-reference that keeps "Destroying the
  Iterator or remainder does not end published child Loans" and the term 22.2.3 uses is a separate SPEC edit.

## 13. Review record

Round 1 (2026-10-22): two independent reviewers who wrote neither the design nor this text; both "approve with changes". The
first revision answered every finding. Round 2 (the same reviewers, the same day): both "approve with changes"; every round-1
finding resolved, and the new findings below are applied in this text.

| Id | Severity | Round-2 finding | Disposition |
| --- | --- | --- | --- |
| S9 / T16 | Blocking | P12's second sentence survives in 15.6.4:595 (SPEC commit `0779b975` added both) | Deleted in §5.6; kept in Appendix A |
| T20 / S12 | Blocking | A U0+U1 merge before U2 leaves dev parsing `preserves results`, rejecting kind-B conformances and running G193's skip without its verification | U0, U1 and U2 land in one dev merge (§11.1) |
| S10 / T26 | Non-blocking | `again`'s `var r = it@uniq` borrows the parameter slot (`InvalidAssignment_Kd`, probe p10) | `it@follow@uniq` (probe p11 accepted), also in Appendix A |
| S11 | Non-blocking | U1 changes the default pipeline, and deleting `AddExplicitDependencies`' region part may drop protection of a locally borrowed source (probes p6–p9, errors today) | U1 adds them and their concrete twins as guard rows first; U5 runs on both pipelines |
| S13 | Non-blocking | §0 kind A; 22.2.3:135's "no Reborrow through `self`" would forbid SliceIterator's shared Borrow | §0; "returns nothing contained in its receiver's Origin" |
| — | Non-blocking | §3.3 should cite 15.6.4's "unproven absence"; §4.3's "unsound" wording; E8 also rests on 15.6.4; `Cursor`'s comment | Applied |
| T17 | Non-blocking | Anchor counts after both deletions are 13 and 9 | §5 |
| T18 | Non-blocking | Spec cases: delete `8.11.3-preserves-results`, delete `15.6.3-abstract-descent`'s `h.1 = 5` variant and land its family, add `8.11.3-checking-and-use` | U0; the new case in U1 (T27) |
| T19 | Non-blocking | SPECJ_TERMINOLOGY.md:30 is a term row, not a citation | §1 |
| T21 | Non-blocking | STATUS changes when the boundary changes, so in U2; DIAGNOSTICS rows with the producers they describe | §10; U1 (158, 301), U2 (156, 157, 336, 342) |
| T22 | Non-blocking | `takeFirst` spans :573–581 | §5.6 |
| T23 | Non-blocking | INTEGRATED's 2026-10-17 entry stays; the restated E8 note is added | §4.3, §10 |
| T24 | Non-blocking | U3's citation pass needs a completion condition | U3 |
| T25 | Non-blocking | The freeze text names its DECISIONS entry | §11.2 |
| S9 (optional) | Non-blocking | 15.6.3's descent definition now restates 15.6.4's per-argument retention | Not taken: open (§12) |

Round 3 (the text reviewer, the same day, a confirmation of the round-2 fixes): T16 and T20 resolved; §11.1 consistent with
§10, §11.2, §1 and PD29. Its findings are applied in this text:

| Id | Severity | Round-3 finding | Disposition |
| --- | --- | --- | --- |
| T27 | Blocking | The new `8.11.3-checking-and-use` case fails `VerifiedSpecExampleTest` in U0, since the old Analysis rejects `takeTwo` until U1 | The case moves to U1 |
| T28 | Non-blocking | Q2 still named U3; DIAGNOSTICS rows 156, 157, 336 and 342 also govern `confined` | Q2 names U1; U2 removes only their `preserves` parts |
| T29 | Non-blocking | PLAN closures and CODEMAP row 90 timing | Closures land with their units; CODEMAP row 90 in U2 |

**Soundness and design.** The reviewer attacked L1 and L2 over implementations reachable only abstractly, raw Origins and
`during self`, object handles and slots, Callables, defaults, reservations, destruction, statics and tasks, stores into other
arguments, Semantics cases and pair slots, recursion and raw Take, and found no unsound acceptance in the core change.

| Id | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| S1 | Blocking | P12 is kept on a reason this proposal deletes (INTEGRATED 2026-10-17); §3.3's and §6's justifications were wrong | P12 deleted (Q1 (a)); §3.3, §4.1, §4.3, §5.6, §6, §7 revised; Appendix A for (b) |
| S2 | Blocking | U5's descent into stores has no SPEC basis | G113's F6 part closes; no descent unit |
| S3 | Blocking | Q1 (b) of the first draft (keep G193's skip) is unsound; probe p1 aborts at run time | Option removed; §1 records the skip's removal and the correction to G193 |
| S4 | Non-blocking | §4.4: only newly valid programs expose unsafe code; the audit misses safe and `drop` actions through the lent authority | §4.4 restated; the audit widened and given the kind-B programs |
| S5 | Non-blocking | Class (C)'s scope under other caller-side Borrows | Class (C) gone with P12; §7 states the reading of rule 3 |
| S6 | Non-blocking | L2's premises are open on the default pipeline | §4.2 names them; §4.2's case analysis closed (environment, payloads) |
| S7 | Non-blocking | Appendix D:49, 12.7.1, the descent fact, SETTLED | §5.10, §5.8; the fact gone with P12; SETTLED in §1 |
| S8 | Non-blocking | Make L1 checkable | U5 |

**SPEC text, completeness, plan and process.** The reviewer confirmed every §5 quote and line, the anchor counts, the
inventory spot checks and the census sums.

| Id | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| T1 | Blocking | Appendix D:49 missing | §5.10 |
| T2 | Blocking | `CallEffectConflict_Kd`'s reservation is R4-M72's Effects bullet with PD7, not R4-M92 | §1 Q2, §10 |
| T3 | Blocking | U0 and U3 break `VerifiedSpecExampleTest`, `MirCensusTest`, `MirBorrowsTest.SpecVariants`, `MirDifferenceTest` and the test build | Test data moved into U0 and U3, with their checks; `a4-x2` added |
| T4 | Blocking | U1 newly accepts descent forms (`_ = it.other()` after a result through a Reborrow) and changes two `HasExplicitDependency` paths | Under Q1 (a) the descent forms are SPEC-valid; U1's completion condition classifies the rest or stops for the owner |
| T5 | Non-blocking | Records missing (G78, PLAN lines 11 and 115, STATUS row 27, A5 plan PDs and X-5, A5P, R5 plan, DECISIONS residues, R4-M39's link) | §10 |
| T6 | Non-blocking | About 40 stale section citations; STYLE and SPECJ_TERMINOLOGY | U3; §1 |
| T7 | Non-blocking | Wording (a)–(h) | (a), (f) moot with P12; (b)–(e), (g), (h) applied |
| T8 | Non-blocking | Misquote of 15.6.2:506 | §2 |
| T9 | Non-blocking | §6's concrete-Iterator claim and `Merge` | §6 revised |
| T10 | Non-blocking | Count inconsistencies | §7 D, §11.1 U3, §11 |
| T11 | Non-blocking | PD29, Q6, U0's review, where the exception is recorded, its exact text | §11.1 (own reviews, merge order), §9 (independent designs), §11.2 |
| T12 | Non-blocking | U5's new Binding fact | Gone with P12 |
| T13 | Non-blocking | `preserves results` silently accepted between U2 and U3 | The grammar change moved into U2 |
| T14 | Non-blocking | (d) differences need STATUS and PLAN | None under Q1 (a); Appendix A under (b) |
| T15 | Non-blocking | SPECJ's revision, the proposal's status after intake | Header, §1 |

## Appendix A. If the owner keeps P12 (Q1 (b); not chosen)

- **SPEC:** 15.6.3:571's abstract sentence stays without its "(§8.11.3)" link; the `takeFirst` example stays. 22.2.3:133's
  last clause becomes: "An item keeps no Loan of the borrow its call forms on the receiver. When the call is a requirement call
  without a known concrete callee, the item also carries the Loans that [§15.6.3](…) adds to a descending result, such as the
  parent Loans of a Reborrowed receiver." 22.2.3:135's first sentence becomes "Keeping earlier items across later calls through
  the same receiver Borrow needs no bound."
- **Soundness:** P12 is a precision restriction with no soundness role; L1's descent row reads "a superset".
- **SPEC (cont.):** 15.6.4:595's "A result that descends from the receiver of a call that §15.6.3 names also carries the Loans
  it adds there." stays.
- **Verdicts:** `var r = it@follow@uniq; let first = r.next(); let second = it.next(); use(first)` changes from valid to error (0 uses;
  its slot form is 6.4.6:349's "Reborrows … preserve the relation", 0 tests (H)); 36f stays an error.
- **Units:** a descent unit for results only (G113's store part closes, S2), with its per-call Binding fact designed and
  reviewed on its own (Q6 form 1); the old Analysis, which does not implement P12, accepts the reverse-order shape and the
  forms with another call on the original (`_ = it.other()`): (d) differences with ledger rows, a STATUS limit and a PLAN item,
  or a located `Unsupported_Kd` guard, as the owner chooses.
