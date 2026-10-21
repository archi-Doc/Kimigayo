# Independent review: R4's two SPEC proposals (2026-10-21)

Reviewer: independent (wrote neither proposal). Read in `bin/worktrees/r4-proposals`, detached at `origin/dev` `217139ae`:
`draft/Proposals/2026-10-21 Closure Calls as Receiver Calls.md` (P1), `draft/Proposals/2026-10-21 Escapes and Derived
Failures.md` (P2) and the new last section of `draft/INTEGRATED.md`. Nothing was built or edited. Checked against AGENTS.md,
SPEC §3.2.1, §7.2, §7.2.3, §7.3, §7.7, §8.10, §14.8.3, §15.1.5, §15.2.3, §15.4.2–§15.4.3, §15.6 (all subsections), §15.8,
§23.1.2, Appendix D.2, SETTLED, DIAGNOSTICS §4.1, §4.3 and §12 (A6 table), 2a0-D §0, §3, §6, §7, §8, §10, §11, the A5 step 2
plan (§2 PD11/PD13, §4.1, §4.5, §4.9), the S3 plan §2 and §4.2, the A5 plan §5.2, DECISIONS R4-M10, R4-M94, R4-M96–R4-M98, and
greps of `tests/xUnitTest/Tests`, `src/Kimi/Library` and `tests/milestones`. "(H)" below marks a reviewer reading no run checked.

| Proposal | Verdict |
| --- | --- |
| P1 Closure Calls as Receiver Calls | **ACCEPT WITH FIXES** (2 blocking findings; the derivation of §15.8.2's table itself holds) |
| P2 Escapes and Derived Failures | **NOT ACCEPTED** (5 blocking findings in the definition and its rule; redo §2–§3, then re-review) |

No SETTLED entry is reopened by either proposal (none covers closure calls or derived failures). Both record their rationale and
a Principles section, as AGENTS requires for a requested SPEC revision.

---

## P1: Closure Calls as Receiver Calls — ACCEPT WITH FIXES

### Citations checked and accurate

§7.7.3's quote ("a direct call acquires the receiver like a method receiver"), the §7.7.2 paraphrase, §15.6.5's quotes (the
fixed-Origin sentence and "A Closure's environment bindings are Fields of its call receiver"), §15.6.3's containment sentence
(it names "a Closure's call receiver"), §15.8.2's five rows and paragraphs, §15.4.3 rule 1, §7.7.4's erasure wording, D.2
("receiver-dependent or lending public results … generic receiver Semantics and `{environment}`"), the S3 plan's §2 row
(`MirBuilder.cs:216`, "a borrowed receiver has no explicit receiver Origin") and §4.2 steps 2 and 4, 2a0-D §3 row 4 ("until
2c-6 classes it per receiver case"), the A5 step 2 rows 2c-2, 2c-5, 2c-6, the A5 plan §5.2 optional row (lib 0, ms 0, about 15
xUnit forms), the library's zero capture lists and the 44 milestones.

### Derivation (§3)

Rows 1–6 of the §3 table follow from §15.6.3 and §15.6.5 as stated: a Borrow of a component through `ref/Self` or `uniq/Self`
is contained in the receiver Origin; a shared Borrow through a captured `ref/T during o` is contained in `o` only; a Borrow
through a captured `uniq/T during o` is contained in `o` and, up to and including the first shared layer, in the receiver; under
`owner/Self` a component Borrow is anchored in a parameter root (body-local) and `self.c@move` transfers `o`. §15.6.5's
body-local sentence and §7.7.4's erasure condition (with "complete environment is Owned" and a Shared minimum receiver) also
follow. Nested `[x@uniq]`, `[x@move]` and bare-entry Reborrows of outer bindings, pair-capture per-case requirements, Copy
Closures in Consuming calls and the `let` component rules give the same verdicts in every case I constructed, except B1.
Option B's rejection is right: elision over the receiver makes `func (y: ref/i32) -> ref/i32 => y` return `during (y and
receiver)`, which no Callable signature or common Function Type carries (§8.10: the result "cannot borrow the hidden callable
receiver"; §3.2.1). No conflict with D.2 (`{environment}` and receiver-dependent public results stay deferred) or with
§3.2.1's "not a user-accessible struct", apart from N10's wording.

### Blocking findings

**P1-B1. Counterexample: Consuming calls lose `var` mutability (§2, §3 "§7.7.2's permissions", §5).** Under the definition the
Consuming receiver is `owner/Self`, a by-value receiver parameter. SPEC §7.2: receiver parameters are "immutable `let`-like
bindings … its storage cannot be reassigned, reinitialized or newly borrowed exclusively", and §15.1.5 forbids exclusive
acquisition of storage rooted in an owned-Type parameter. Today the environment bindings are exempt: §7.7.2 ("each environment
binding acts as a local of the call with its capture's `let`/`var` mutability", "A consumed `let` capture cannot be
reinitialized", "Environment bindings are not parameters, so the owned-Type parameter restriction of §15.1.5 does not apply")
and §15.1.5 ("A Closure environment binding is not an owned-Type parameter; its `let` or `var` mutability decides"). Valid today,
rejected under the definition:

```kimi
let first = makeResource()                  // Non-Copy
let cycle = func [var first@move] () -> Resource
    let old = first@move                    // Consuming
    first = makeResource()                  // reinitializes a var binding: valid today (§7.7.2)
    return old
```

The same holds for `count += 1` on a `var` capture in a body that also moves a capture, a `uniq/Self` method called on a `var`
capture in a Consuming body, and a nested `[count@uniq]` of an outer `var` binding inside a Consuming outer call (§7.7.2 judges
it "by the outer call's permission"). §3's derivation lists "`owner/Self` moves components out" and omits writes. 2x-P7's cases
f7a–f7n contain no such form, so §8's derivation round would not catch it. Fix: state that the Consuming receiver is an
exclusively writable root whose components keep their `let`/`var` mutability (as §7.2 already gives construction and
destruction receivers "their own privileges"), keep §7.7.2's and §15.1.5's sentences, count this among the closure-specific
rules, and add the forms to §8 step 1; or present the verdict change to the owner as such.

**P1-B2. The Closure Type's fixed signature Origins (§2's definition, §5's Appendix E row).** "A concrete Closure Type is a
structural Type whose components are its environment bindings" read as the Type's whole content drops what §15.2.3 lists
separately: OwnedOrigins includes "concrete Closure captures" **and** "a concrete Closure's fixed signature Origins". A
no-capture closure with a fixed signature Origin, valid in today's tests (`FixedOriginRetentionTest.cs:292`
`func (b: ref/i32 during x) -> ref/i32 during x => b`), has `x` in OwnedOrigins today, so passing it to `keep<T>(value: T)`
with `T is Owned` inside `func use(x: ref/i32)` is a Constraint failure; a Type with no components would be Owned. The same
gap reaches §6's retention claim (the walk must also see the signature's fixed Origins, §15.6.4's "Origin inside a borrowed
argument's referent Type"). Fix: the definition keeps the call signature with its fixed Origins as part of the Type (and see N10
on "structural").

### Non-blocking findings

- **N1.** Header: 2c-D is in the A5 step 2 plan's **§4.5** ("C2 and A5-F7 closures and escape (2c)"); §4.4 is A5-F5 (2b).
- **N2.** "Two closure-specific rules remain" (§2) undercounts: B1's mutability rule; §7.7.3's per-case minimum receiver, which
  a declared instance function cannot have (§7.3.1: one `S/Self`, no generic Semantics; §7.3.2: one shape per group; D.2 defers
  generic receiver Semantics); and the body-inferred requirement itself. The definition already says "in each admitted case";
  say that §7.3.1/§7.3.2 do not apply, and qualify §6's "the ordinary method-call path replaces …" (the method path selects one
  receiver shape per Name).
- **N3.** §3 does not derive row 3 under `owner/Self` (a Reborrow through a captured `uniq` in a Consuming call: contained in
  `o` only; the end of `self`'s storage ends the child, §15.6.3 "a Reborrow of a parameter may be returned"). It follows; add it.
- **N4.** Rule 1 keeps §15.8.2's second paragraph "as written", which says "per the table"; the table becomes informative. Point
  it at §15.6.3/§15.6.4 or keep the table normative.
- **N5.** Rule 2's stated reason: an annotated closure may name fixed enclosing Origins that a Callable `S` cannot write (§8.10:
  "`S` writes no `during`"), so "must be one a Callable signature … can carry" is not exact. The reason is that the receiver is
  hidden and unnamed, and Callable and common Function Types cannot depend on it.
- **N6.** Option B's census: the 26 grep matches include `GenericCallbackEmissionTest.cs:50` (unannotated result, matched
  through a Function-typed parameter) and about a dozen forms whose result Origin is written (`during x`, `during o`, `during
  root`), which elision does not touch. The forms B changes are the elided ones (about 13). The argument holds; say what is
  counted. B also flips §15.8.2's `invalid` example to valid; worth one clause.
- **N7.** §8 census: 16 capture lists in six milestones (`Milestone12.kimi:19` holds two, nested), not 15.
- **N8.** §6, C1-F4 bullet: row 4's atom is a `Projection` atom, the Origin of a Borrow of an environment binding, not a slot.
  Classed as written ("an environment slot is a projection of B's receiver (row 1, Fixed)") it would be Fixed. It is Inferred
  under a borrowed receiver (D14's edge to the receiver, Fixed by row 5's ClosureValue role) and Finite under `owner/Self`,
  as the next clause says; capture Origins are already row 6 (Fixed).
- **N9.** §7 Principle 3: "no closure-specific contract is inferred" is not so: the receiver requirement (§7.7.3) and an omitted
  result's contract (§15.8.2) are inferred from the body. Principle 1's "Closures and methods share receivers" holds for Origins
  and Loans, not for receiver selection.
- **N10.** "Structural Type" is not SPEC vocabulary and reads against §3.2.1's identity rule ("distinct expressions have distinct
  Types" with equal captures). §15.2.3 already treats captures like Tuple components. "Whose components are its environment
  bindings, not user-accessible" says what is needed without the new term.
- **N11.** INTEGRATED says the receiver in elision "is rejected"; it is the proposal's recommendation pending OQ-C2.
- **N12.** Nit: the owner's quote is wrapped mid-phrase in both proposals and INTEGRATED, which renders as 「受け手 つき」 /
  「と 候補3」 in Markdown.

### Options, questions, (H)

The options are fair (D: no change; C narrows SPEC for implementation, rightly rejected under AGENTS); the recommendation follows
once B1/B2 are fixed. OQ-C1 and OQ-C2 are decidable. "(H)" marks the no-verdict-change claim, §5's examples, §6 and the census;
B1 shows the no-verdict-change claim needs a counterexample hunt beyond 2x-P7's cases (B1's Consuming forms, B2's Owned form).

### Shorter or more general formulation

Leave §7.7.2 and §7.7.3 (the permissions and per-case receivers) as they are, and put one sentence in §15.8.2: "A Closure body is
checked against its call receiver as an instance function's body is: its environment bindings are Fields of the receiver's
referent (§15.6.5), so result Origins, containment (§15.6.3) and retention (§15.6.4) follow the receiver rules; the table below
restates them." Make the table informative and delete §15.6.5's fixed-Origin closure sentence (covered by "a parameter or
receiver Origin, a projection of one"). This gives the one form A5-F7, S3's C2 and the C1-F4 view need, with no new Type category
and no instance-function receiver rules, so B1, B2 and N2 do not arise.

---

## P2: Escapes and Derived Failures — NOT ACCEPTED

### Citations checked and accurate

§23.1.2's text (neither "independent" nor "caused only by" is defined); §15.6.5's sentence on locals (one sentence with two
clauses, called "two sentences"); §14.8.3 has no code sentence; 2a0-D R6 ("either code is admitted"); rules 1 and 2 as in 2a0-D
§7; PD11's PreparedInput half; R4-M10 and 2g-1's superseding of "dropped"; R4-M97's summary; N-6's residual (`cf-g-reassign`) and
`rv-g-param`'s DOUBLE as recorded in 2a0-D §7 and R4-M94; AGENTS' "suppressing duplicate reports afterwards"; the merge context
(`RefutedAtResult`, `Ended`). Option A does depart from the owner-approved 2a0-D §7 (R4-M94) and PD11, so under AGENTS' review
weight 2a-2 leaves G:A5-F4's group form and takes the full process. That claim is correct.

### Blocking findings

**P2-B1. "One failure is reported once" is a new requirement presented as today's rule, and it can merge two escapes (§2).**
§23.1.2 leaves grouping implementation-defined; "(grouping, as today)" makes it mandatory. DIAGNOSTICS §4.1 keys instance problems
by Context (instantiation), so one requirement can be recorded once per instantiation today (H: whether those are "the same
requirement"). Worse, "one failure" is a pairwise relation with no stated non-transitivity. One bound-end conflict witnessed by
two escaping flows is "one escape" with each relation:

```kimi
func two(p: uniq/(ref/i32 during a)) -> ref/i32 during a
    let x: i32 = 1
    let r = x@ref
    p@follow = r      // Refuted at the store
    return r          // Refuted at the return; x's storage-end conflict is witnessed by both flows
```

Read as an equivalence, the two Refuted relations become one failure reported once, so an independent failure is lost. Under
OQ-D2 (b) or (c) the guard analogue (a candidate stored into two parameters' referents in one guard: two Refuted relations, one
err(ended)) reports the conflict and drops both relations, again one record for two independent escapes. Fix: an escape is one
(relation, conflict) pair; two relations are never one failure; the conflict goes only when every witness flow closes a reported
Refuted chain; grouping stays implementation-defined, or the change is stated as a change.

**P2-B2. The definition is not testable, and §3's rule disagrees with it (§2, §3).** "Value flow" and "the chain that flow
closes" are not SPEC terms. "The end of the bound (the end of the anchored storage …)" conflicts with §15.6.5, where a body-local
Origin's bound is the body. Two cases that are one escape by the definition (one flow) but two failures by the rule (witness site
differs from the last-edge site):
- `rv-g-annot-esc` (2a0-D, kept as S and C under the owner's OQ-4 (b)): the candidate is stored into `var t: ref/i32 during a`
  at the guard's call (chain closes there, S) and `t` is used after cleanup (err(ended), witness Use = `t`'s next use).
- `var r: ref/i32 during a = …; r = x@ref; return r`: the chain closes at the assignment; the storage-end conflict's witness is
  the return (`Result`).

So either the rule reports one failure twice by the proposal's own definition, or the definition suppresses a record the
owner-approved design keeps. Neither case is in §2's table. Fix: state the definition in published-fact terms (for example, the
conflict's witness carries the Borrow through the holder or value that the relation's last edge fits), and classify these cases.

**P2-B3. "Rests on" is broader than "caused only by" (§2).** "Decided on a construct the other rejected, or on a value or binding
that construct produced" makes this failure optional, beside §23.1.2's own example:

```kimi
func setup() -> i32
    let text: string = 1   // Error: 1 does not fit string
    return text            // Error: string does not fit i32; it rests on the first under P2
```

Today it is independent: it stays when the first is fixed (`= "a"`), so it is not "caused only by" the first. The definition
narrows §23.1.2's guarantee without saying so. DIAGNOSTICS §4.3 treats only missing-information failures, recovery consultation
and later phases' reads of failed nodes as derived ("every other failure is direct"). Fix: an "only source of the failing fact"
or prerequisite definition (see the shorter formulation below), or state the narrowing and its reason for the owner.

**P2-B4. The PreparedInput claim contradicts the definition (§1 table, §3).** "The PreparedInput rule is one escape (the Borrow
and its default's Return)". But `DefaultArgumentBorrow_Kd` is §7.2.3's own requirement at the template (R4-M98: a new Borrow of a
preceding slot that the result keeps), not a Loan conflict at the end of the anchored storage or at guard cleanup. By §2 the PD11
pair is two failures, so replacing 2g-5's rule reports one failure twice (A3 E12), unless "rests on" is meant to cover it, which
the proposal does not say.

**P2-B5. "Both strata already publish the facts it needs" is wrong (§3).** The rule fails on the published facts:
- The Origin problem's `Anchor` is "the first Ref in body order whose canonical destination is the finite source" (2a0-D §6.3,
  D18). In m-twice/same-root (two Borrows of one root, the second returned) the two Anchors name different Borrows, so two
  records are reported for one escape. This is why 2a0-D rule 1 works per atom (§7: "dropping by Borrow alone loses …").
- A Loan problem carries one witness, the first found (R4-M96). m-store's "flow the chain does not close" shows only if that
  holder is found first; otherwise the rule drops an independent conflict.
- `Result` marks `_0` and names no return.

These are facts to publish, a representation change belonging to 2a-2's full process (§6 step 2 should list them), not an
existing input. Option A's "closed by construction (H)" depends on them.

### Non-blocking findings

- **N1.** Header: 2g-D is in the A5 step 2 plan's **§4.9** ("A6 completion (2g)"); §4.7 is A5-F9 (2e).
- **N2.** N-6's residual is "pinned as a recorded limit (DIAGNOSTICS' A6 merge row, STATUS, X-2)": those records are 2a-2's
  (2a0-D §9). Today only DECISIONS R4-M94 records it; DIAGNOSTICS' A6 table has no merge row yet.
- **N3.** `rv-g-param`'s expected records are S, C and P (2a-2's list); the table names two. Classify P.
- **N4.** §15.8.2's "returning a body-local borrow is an error at the returned value, not a Loan conflict at the storage's
  destruction" is a third code sentence; §5's mapping omits it.
- **N5.** The escape rule also pairs two records after both strata recorded (D13's Record step), the pattern §1 criticizes.
  MirBorrows adds problems on arrival and 2a0-D §6.1 forbids removal after adding, so either bound-end Loan problems are held
  until Record or MirReport removes them. Name the D13/MirReport contract change.
- **N6.** §3's rule is in substance 2a0-D's OQ-4 (c) (a Loan-stratum flow fact), which the owner did not choose. Say so and why
  it is now preferred. OQ-D1 (a) asks the owner to supersede OQ-4 (b) and OQ-3 (2) and (9) (R4-M94), and OQ-D2 (a) reverses
  OQ-4 (b)'s guard code. Name these in §4 and the questions (INTEGRATED names R4-M94; the proposal does not).
- **N7.** DIAGNOSTICS §4.3's invariant is `Code == PrerequisiteUnavailable_Kd` exactly when `DerivedFrom` is non-empty.
  Publishing MIR causes through `DerivedFrom` therefore changes a published derived MIR problem's code. State it, its effect on
  2g-1 (which keeps one cause fact) and on H3 (R5).
- **N8.** Principle 2 ("not on what else a view happened to record") does not hold: whether a conflict is reported depends on
  whether the Origin stratum recorded a Refuted twin.
- **N9.** `{ let x = 1; r = x@ref }; use(r)` is not Kimigayo syntax (indented bodies), and `r` needs a declaration outside it.
- **N10.** Missing alternative: OQ-4 (c) inside 2a0-D with no SPEC change, the closest competitor to A. B and C are fairly
  stated. OQ-D1 to OQ-D3 are decidable; OQ-D2's recommendation is conditional on the prototype, which is acceptable.

### Shorter or more general formulation (reviewer's suggestion, unverified)

Drop "one escape", "reported once" and OQ-D2, and define only the existing term in §23.1.2: "A failure is caused only by other
failures when the fact that makes it fail comes only from operations those failures rejected." For example, a bound-end Loan
conflict whose every witness carries the Borrow through a fit that a Refuted relation rejected is caused only by that relation.
Readings:
- Derived: the conflicts of j-bad, m-scope, `rv-g-param`, `rv-g-annot-esc` and m-twice; the PreparedInput Origin relation (the
  kept Borrow is the operation `DefaultArgumentBorrow_Kd` rejected).
- Independent: m-write's write conflict, m-store's conflict (another holder), and `cf-g-reassign`'s relation and err(ended).
- Two escapes keep both relations, and the conflict rests on both (DIAGNOSTICS §4.3: suppressed only when every cause reaches a
  published direct Error).
- `return text` above stays independent, because `string` comes from the annotation.

§15.6.5's sentences and the guard code follow: the relation is direct and the conflict derived. Grouping stays
implementation-defined, and no new obligation is added. The implementation is 2g-1's cause fact, with the witness set (every
flow) as a published fact.

---

## INTEGRATED entry

The format matches the register's recent intake entries: a dated `##` section, the owner's verbatim instruction, `一部取り込み
(open, intake only)` per file, planned mapping, owner questions, and "Nothing is integrated into SPEC or IMPL, no text is frozen,
and no support is claimed". It states nothing integrated. Fixes:
- "reviewed independently before intake" should name the review record, as the G160 entry does, or be dropped if the record
  stays outside the repository.
- P1's bullet should not state option B as decided (P1-N11).
- P2's bullet must follow P2's redo.
