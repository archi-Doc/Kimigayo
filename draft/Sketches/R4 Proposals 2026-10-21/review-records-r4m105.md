# Independent review: R4-M105 records and SPEC §5.7 intake (2026-10-22)

Reviewer: independent (wrote none of the text). Read in `bin/worktrees/r4-proposals` (detached `82279438`, uncommitted):
`git diff HEAD -M` over `docs/spec/05-raw-pointers-and-unsafe-memory.md`, `docs/dev/DECISIONS.md` (R4-M105), `docs/dev/PLAN.md`
(G160), `draft/INTEGRATED.md`, and the move to `draft/Changes/2026-10-22 Raw Pointer Rules Left Open by G160.md`.
Checked against AGENTS.md (Documentation, Diagnostics, draft rules); the proposal (§1–§7); `review-3.md` §1; `review-rp2.md`;
the G160 design (D1, D3, D5, D7, D13, D18, §3, §6, §7, §9.2, §9.4); DECISIONS R4-M94 (supersession marks), R4-M95, R4-M99, R4-M103,
R4-M104; SPEC §3.3.2, §3.5, §5 (intro, §5.2.1, §5.4, §5.6, §5.7 whole), §6.3.4, §6.3.6, §13.5.6, §15.1.3, §15.2.1–§15.2.3, §15.3.1,
§15.3.6, §15.4.3, §15.4.4, §15.6 (Addresses, Holders), §15.6.1, §15.6.2, §16.3.1, §23.1.1–§23.1.3, Appendix E; STATUS row "Loans
and returned references"; COMPILER_ARCHITECTURE "Closed vocabularies"; `tests/xUnitTest/DiagnosticCorpus.cs`,
`MirDifferenceTest.RequestedCorpus`, `tests/diagnostics/spec-cases.json`. Nothing was built, run, edited or staged.

## Verdict: ACCEPT WITH FIXES

The SPEC intake states OQ-R1 (a) and OQ-R2 (a) as decided, with no narrowing beyond the owner's allowlist, and the Authority
error still refuses a write through a shared formed intersection. The three placement choices are sound. Three blocking findings
remain, each a one-line text fix: a wrong illustration in normative SPEC text (B1), a superseded DECISIONS entry that does not
name its replacement (B2), and a stale INTEGRATED sentence (B3).

Checks that pass: `git diff --check` clean; no BOM, no CR (byte count 0 in all five files); PLAN 223 lines (≤ 225); the moved
file's relative links resolve from `draft/Changes/`; the old path survives only in the headers of `review-3.md` and
`review-rp2.md`, which are historical records (the Lifecycle Receivers move left them the same way); STATUS untouched is right
(its row "Loans and returned references" already says SPEC §5.7's raw Origins are not implemented, and nothing became supported);
no record claims support; SPECJ and GUIDE are correctly left alone; the frozen proposal changed only its status line.

## 1. SPEC §5.7 against the decided options

- **"Writing a Field" vs OQ-R1 (a).** Allowlist exact: reads, shared borrows, `null` writes; every other write, compound
  assignment and a `static` source named; exclusive borrows including `exchange`/`swap`/`replace` arguments; Case payloads
  `null`; the repair through an exclusive borrow and a claim; classified as Origin relation errors (§15.6.1). The first sentence
  (through a dereference, the Origin reading gives) is the proposal's §2.1 last line and §3 mapping. No form beyond the decided
  list is made an error, apart from the Move gap in N4.
- **Constructor/`drop` exception.** Correct and consistent with "Origins": §6.3.4 and §16.3.1 make constructor and `drop` `self`
  an exclusively writable root (no dereference), and "Origins" makes `self` there the value constructed or destroyed, so
  `self.data = Raw.allocate<T>(n)@(raw/T during self)` (§5.6's `Buffer.init`) and `exchange(self.data@uniq, with: null)` in
  `drop` keep their route. The antecedent of "the Type" is loose (N6).
- **"Origins" vs OQ-R2 (a).** Matches §15.3.1 ("on `raw` it is no intersection" now reads as the written form), "Reading a
  Field" and "Typed paths" (both give "a borrow's outer Origin", now of any form), "Calls" ("the borrow that Origin belongs to")
  and "Authority": `*dataOf(choose(x, y)) = v` forms `raw/T during (x and y)` as the outer Origin of the `ref` result of `choose`,
  which is "the outer Origin of a `ref` … borrow", so the write is still refused. The illustration is wrong (B1), and the
  sentence is inexact for the raw-pointer and slot routes (N1, review-rp2 N10 not adopted).

## 2. Blocking

**B1 (SPEC §5.7 "Origins"): the `choose` illustration states a wrong Origin.** `x@ref` borrows the slot `x` itself (§13.5.6),
so each argument's Origin is a new borrow's inferred local region (§15.6: "Local regions are inferred"), not `x`. If `x` and `y`
are owned locals, `(x and y)` is not an Origin at all (§15.2.1: "A value without an outer safe-borrow Origin is not an Origin").
If they are borrows, `x@ref` is `ref/(ref/T)`, and the result is `raw/(ref/T)` during the meet of two unnamed regions. The
statement holds only for borrow inputs passed by value, as in §15.2.4's `pass(local)`. Also, "source cannot write" reads
ambiguously next to §5.7's slot named `source`.

Replace:

> with `choose` of [§15.3.6](15-ownership-and-lifetime-analysis.md#1536-origin-inference), `choose(x@ref, y@ref)@follow@raw` is a `raw/T during (x and y)`, which a local's inferred Type shows but source cannot write.

with:

> for inputs `x: ref/T` and `y: ref/T` and `choose` of [§15.3.6](15-ownership-and-lifetime-analysis.md#1536-origin-inference), `choose(x, y)@follow@raw` is a `raw/T during (x and y)`, which a local's inferred Type may state but no written Type can.

(The same slip is in R4-M105's sources: review-3 RP-B2 and the proposal §1/§2.2 wrote `choose(x@ref, y@ref)` and
`(x and y)`. Those are frozen records; only the SPEC needs the fix.)

**B2 (DECISIONS R4-M95): the superseded entry does not name its replacement.** AGENTS' Documentation table: "a superseded entry
stays and names its replacement", and R4-M104's commit marked R4-M94 that way ("superseded by R4-M104", five places). R4-M105
names R4-M95 and the reason that no longer holds, but R4-M95's "Raw Origins" bullet still says plainly "no intersection".

In R4-M95, replace `raw Place borrows and `Raw.slice` lie within it; no intersection; the pointer's uses` with
`raw Place borrows and `Raw.slice` lie within it; no intersection (for formed Origins superseded by R4-M105); the pointer's uses`
(rewrap as needed).

Recommended in the same edit, for traceability: in R4-M95's "Representations" bullet, after `Binding's optional raw Origin with
`self` are approved`, add ` (the address amended by R4-M105: an own-step marker)`. In its "Rejected" list, change
`a static Move check now` to `a static Move check now (a Warning adopted by R4-M105 for G160-4)`. R4-M105's Warning takes up
G160 §7's "later precision candidate", which R4-M95 rejected for now.

**B3 (INTEGRATED, end of the 2026-10-22 section): a stale, now false statement.** The section's closing bullet still reads "Only
the lifecycle receivers' root is integrated (SPEC §15.6.5); no support is claimed.", but 2.1 A and 2.2 A are now integrated into
§5.7.

Replace with:

> - Integrated: the lifecycle receivers' root (SPEC §15.6.5) and the raw pointer rules' 2.1 A and 2.2 A (SPEC §5.7); no support is claimed.

## 3. Placement choices (item 2)

- **(a) OQ-R3's Warning sentence deferred to G160-4.** Sound. The condition needs the own-step marker in SPEC terms (review-rp2
  N18: an address with a marker is not a SPEC notion yet). A Warning changes no acceptance, so deferring narrows nothing (AGENTS).
  R4-M105 and INTEGRATED record where the sentence goes (§5.2.1 condition 3) and who writes it. Correctly recorded.
- **(b) OQ-R4 as a DIAGNOSTICS Information code, not SPEC text.** Sound, and within the proposal's own §3 variant ("both as
  implementation diagnostics only"). No SPEC chapter requires an Information diagnostic, and codes are implementation-defined
  (§23.1.1). The stated reason, "23.1.1 states required warnings", is imprecise: §23.1.1 lists none, it says the SPEC requires
  some warnings (see N8). DIAGNOSTICS is correctly left until BU's unit adds the code.
- **(c) §2.1's sketch not added as an example.** Sound. `DiagnosticCorpus.SpecBlocks` reads every `kimi` block under `docs/spec`,
  `MirDifferenceTest.RequestedCorpus` checks them all (R4-M17's tier 1), and an added block in §5.7 can disturb a
  `spec-cases.json` anchor's uniqueness. Until G160-0 the parser also rejects `during` on a raw layer with Language errors.
  A documentation-only unit should not add a corpus case. No SPEC rule requires an example per bullet.
- **Closing as 取り込み済み with transfers.** Right under AGENTS: something was incorporated (2.1 A, 2.2 A), the file moved to
  `draft/Changes/` with only its status line replaced, and the remaining decided content is held in full by DECISIONS R4-M105
  (authoritative) and assigned (G160-4, BU's unit). This is the Lifecycle Receivers form. Static Slots stayed open only because
  its package content had no other home, whereas here no proposal text remains to be taken into SPEC (2.3 has no sentence text).

## 4. DECISIONS R4-M105 (item 3)

Faithful. The header quotes the owner verbatim, as in the standing request. OQ-R1 through OQ-R4 match the options the owner
chose, and the proposal's §2 and §4. The supersession names the reason that no longer holds (G160 records none for D18; the
reconstructed one fails for formed Origins; checked: D18 has no rationale in the G160 design). The OQ-R3 approval is recorded
exactly as the option read ("approves a change to the owner-approved address representation") and leaves any closed-vocabulary
entry to the owner. OQ-R4 claims no code-name approval. The format (header with sources, one bullet per decision, Rejected)
matches R4-M99, R4-M103 and R4-M104. Non-blocking precision: N8, N9, N10.

## 5. Non-blocking (open issues or optional edits)

- **N1 (SPEC "Origins", precision; review-rp2 N10).** "The outer Origin of the borrow it is formed through" fits a `self` read
  and a typed path through a borrow. It does not fit a route through a raw pointer, where the formed Origin is the Origin that
  pointer states (possibly `static`), or slot substitution, where it is the slot's binding. That binding may be a meet kept by
  several Loan Fields (a Type with two `Loan<ref/T during source>` Fields constructed from two borrows) and is no single borrow's
  outer Origin. An optional exact form that keeps OQ-R2 (a) and B1's fix:
  > An Origin that a raw layer obtains by reading a Field, by a typed path or by substituting a slot or input may be of any form, an intersection included, and belongs to what the Origin it comes from belongs to: for inputs `x: ref/T` and `y: ref/T` and `choose` of [§15.3.6](…), `choose(x, y)@follow@raw` is a `raw/T during (x and y)`, which a local's inferred Type may state but no written Type can.
- **N2 (SPEC "Writing a Field", the Place's Type through a dereference).** The first sentence covers writes. An exclusive borrow of
  the layer's Place through a dereference (`r.data@uniq`, `exchange(r.data@uniq, …)`, which the allowlist leaves valid) gets its
  Referent Type from the rule only by implication. The proposal's "the target's Origin" (§2.1) and §4 (`r.data` reads `r`'s outer
  Origin with `r`'s mode) cover it. Optional replacement for the first sentence:
  > Through a path that dereferences a borrow or raw pointer, a raw layer that states `self` has the Origin that reading the Field through that path gives: a source written there must fit it, and a borrow of its Place states it.
- **N3 (enclosing Tuple and fixed-array Places; recommended before BU's C1 sites are fixed).** A write of an enclosing Tuple or
  fixed-array Field (`b.pair = (q, 1)` with `pair: (raw/u8 during self, i32)`) writes the layer's Place, so the allowlist covers
  it on a careful reading. Read as a fit of the Tuple instead, it takes the `outlives` route that RP2-B1.1 excluded (a `static`
  element fits). An exclusive borrow of the enclosing Place (`b.pair@uniq`, `exchange(b.pair@uniq, with: (q, 1))`) is not "an
  exclusive borrow of the Place", yet it has the same problem as `b.data@uniq`: `uniq` is invariant in its target, and no body
  Type can state own storage (RP2-B1.2). If an implementation types it `uniq/(raw/u8, i32)`, safe code stores an unstated pointer
  into own storage (D5). Suggested sentence after the allowlist:
  > A Tuple or fixed-array Place that holds such a layer within the same holding value is written and borrowed as that layer's Place is.

  This extends the listed narrowing to exclusive borrows of such enclosing Places. Either record it in R4-M105 as the planning
  session's reading of OQ-R1 (a), confirmed with the owner, or open a PLAN issue and keep those borrows the located
  `Unsupported_Kd` until decided (AGENTS Diagnostics). Either way BU's U3 sites (proposal §4) should cover the enclosing forms.
- **N4 (a Move of the layer's Place).** "Reads, shared borrows and writes of `null` only" leaves `b.data@move` neither admitted
  nor in the error list. That is a Partial Move of a Copy raw pointer (§3.5, §15.1.3), valid today when restored before a
  whole-value use. It is harmless (the restoring write must be `null`), but the owner's option says "only". Ask, then write either
  "reads (a Move included)" or the error, or record an open issue.
- **N5 (SPEC "Writing a Field", wording).** "Holder" is a defined term (§15.6, Appendix E: a local, parameter or temporary).
  "The storage the holder owns" means the holding value's own storage (§5.7's table). "Since no Origin names …" is also the
  naming reason RP2-B1.1 said decides claims, not fits. Optional replacement for the last two clauses:
  > …, since no Origin covers the storage the holding value owns there: it is an own step, not a region, so no fit by `outlives` applies. Such a write goes through an exclusive borrow of the holding value or of a Place containing it (`let r = b@uniq`), whose Origin a claim can name.
- **N6 (exception's antecedent).** "The Type's constructors and `drop`" has no antecedent in this bullet. With a nested holding
  value (`self.inner.data` in Outer's constructor) the two readings give different verdicts, although both are sound. Optional:
  `except through `self` in the constructors and `drop` of the struct or enum whose inline storage holds the layer`.
- **N7 (Case sentence category).** R4-M105 calls a non-null Case payload an Origin relation error, but the SPEC sentence names no
  category. Optional: `A Case whose payload has a `during self` layer is constructed with `null` in that layer; any other payload there is an Origin relation error too.`
- **N8 (R4-M105 OQ-R4 and INTEGRATED, rationale and code).** Replace `(23.1.1 states required warnings, not Information)` with
  `(the SPEC requires warnings only; other diagnostics and their codes are the implementation's, 23.1.1)` in both records.
  Precision only: replace `BU's implementing unit brings its code name to the owner` with `BU's implementing unit decides its
  code; a `BindingFailure` entry beyond the cap needs the owner (R4-M95, G160 §9.2)`. Diagnostic codes are not a closed
  vocabulary (COMPILER_ARCHITECTURE; review-rp2 N24).
- **N9 (OQ-R4's repair for enum Case payloads; open issue for BU's unit).** The repair "state `during self` and add the claim at
  each constructor fill" cannot apply to an enum's Case payload. Enums have no constructors, and after the repair OQ-R1's Case rule
  makes every non-null Case construction an Origin relation error, which a claim does not lift. Offering it would break
  DIAGNOSTICS rule 11 and §23.1.3. Add to R4-M105's OQ-R4 bullet: `for an enum's Case payload, no repair candidate (OQ-R1's Case
  rule)`. Alternatively, limit the trigger to Fields, as the owner's words say ("Field").
- **N10 (R4-M105 OQ-R3, precision; review-rp2 N15, N16).** "Compared by 15.6.2's overlap rules" sits beside the condition's "the
  owning value's Place or an ancestor". Optional: `A5's address gains an own-step marker carrying the owning value's Place, which
  the Warning compares with the operation's Place by 15.6.2's steps (the Place or an ancestor); for Loan checking an address still
  conflicts with nothing.`
- **N11 (PLAN).** The G160 row now names the Warning for G160-4, but it names no unit for R4-M105's Origin relation error (BU's U3
  sites) or the Information diagnostic. Optional, in the row: `G160-1 and G160-2 (raw Origins and their retention in Binding, one
  design, full process; with R4-M105's write rule, formed intersections and Information diagnostic)`. BU's line 35 still cites
  "B+; the owner's Q-O1, Q-O2, Q-O4–Q-O6", whose Q-O4 and Q-O5 answers R4-M105 replaces; that line is the BU session's to update.
- **N12 (INTEGRATED, cosmetic).** Line 1018 is 181 characters. Rewrap it to the file's ~130 columns. The section heading
  ("Static slots, layout requirements and lifecycle receivers") could add "and raw pointer rules".

## 6. Summary for the planning session

Fix B1 (one clause in §5.7), B2 (one parenthesis in R4-M95, plus two recommended marks) and B3 (one INTEGRATED bullet). No
redesign. Before BU fixes U3's sites, decide or record N3 (enclosing Tuple and array Places) and N4 (Move). Give N9 to BU's
Information-diagnostic unit.
