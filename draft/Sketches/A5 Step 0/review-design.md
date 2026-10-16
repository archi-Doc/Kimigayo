# Step 0 design review: A5 designs A and B

Reviewer: independent, read-only. Base `e32dc0ef`. Inputs: plan §4–§7 and Appendix A, the three audits, the census,
the prototype (rounds 1–3, re-read after the coordinator's note), design-A.md, design-B.md and choice.md. `A:n` and
`B:n` are lines of design-A.md and design-B.md; `P:n` is a line of prototype.md; `S15:n` is a SPEC line in the worktree.

**Verdict: revise.** Keep A as the base and take graft 4 (text only), graft 2 (narrowed) and graft 3 (accounting
only). Do not take graft 1. Fix findings 1–3 before the design goes to the owner (OD13).

## Why A stays the base

- **Same core.** Both designs share the core the prototype tested: S1′ naming, deep Move and borrows, syntactic
  Places, materialized parents and a check-time closure. Their `hits` rules are identical: A:172 and B:151 both meet
  an access *through* a reference with a Loan on that reference.
- **A is smaller.** A has about 2,270 new lines against B's 2,900. A also changes four MIR representations (RawSlice
  operand, `MirCallee.Arg`, the `EndBorrow` meaning and a `MirLocalRole` member; graft 2 removes the role). B changes
  eight: the TwoPhaseUniq meaning, descriptor rows, a Retains column, the `MirStatic` key, `MirCallee` columns, G3
  Path, G4 rows and G7 flow-closure.
- **A is no slower.** Per-root holders make Hf smaller than B's move paths.
- **A's extra loss is cheap.** P1 costs lib 0 and ms 0. My spot grep checked the candidates: `Milestone33.kimi:70`
  stores two Loans in `limits`, but `low` and `high` are read only after `limits` dies; `Milestone16.kimi:36`
  retains both Loans by its clause.
- **No finding favors B.** None of the blocking findings needs a representation change. Each is a rule-text
  correction inside `MirBorrows`. B states the same check timing no more precisely (B:293-297: "replay forward and
  check each access").

## Findings

### 1. BLOCKING: self-exclusion by Loan id is unsound under site identity

**Location:** design A §5, at A:204 (`LS(ℓ)` built from σ "after ℓ" over `live_out`), A:208 ("L not issued at ℓ")
and A:218 (`L≠R`).

**Cause.** Under S1′, one Loan id stands for every evaluation of its site. A Loan carried from an earlier iteration
therefore has the same id as the one being issued, and A excludes it. Counterexample:

```kimi
var v = 1
var w = 2
var prev = w@uniq
while more()
    let cur = v@uniq    // SPEC and S1′: Error from the second iteration (S15:502). Rust rejects it too.
    inspect(prev)       // prev still carries this site's Loan from the previous iteration.
    prev = cur
```

A accepts this program. The variant `let cur = first(v@uniq)`, with `first(x: uniq/i32) -> uniq/i32 during x`, is
accepted twice over:

- the `L≠R` activation exclusion skips the earlier incarnation;
- `Act \= {L}` at the reissue (A:184) flips the earlier, activated incarnation back to `ref`.

**Why the prototype missed it.** The coordinator's point 6 says the two timings coincide on the prototype's programs.
That holds only because no case keeps a site's earlier Loan live at its reissue. The A mode also uses entry sets with
live-in (`proto.cs:374`), not A's text, and it still excludes by id at activation (`proto.cs:385`). Its reservations
are straight-line positions (`activeAt`, `proto.cs:386`), so it cannot express the reserved variant.

**Fix.** Exclude by holder, never by Loan id:

- Check an access against the σ *on entry* to ℓ, over the holders live *after* ℓ, leaving out the destination's
  overwritten value.
- Check activation against `live_in`, excluding R only where the call's own argument temp carries it.
- Delete "L not issued at ℓ".
- Entry with live-in, as `proto.cs` does, is over-strict for `y = h@follow + 1` with `h = y@ref`.
- Add both loop programs, and that read-then-write program, to `draft/Sketches`. Model reservations as forward state.
- S1's proposal should say that a Borrow is checked against its own site's Loan when an earlier evaluation's Loan is
  still live.

### 2. BLOCKING: a child Loan issued where its base is destroyed in place must end there

**Location:** design A §5, at A:184 and A:190.

Round 3 found that the in-place traversal `cursor = cursor.children[0]@uniq` (19a) is rejected unless the issue rule
has `!ended` (P:420-430). A:190's "including values flowing at ℓ" does not order the issue before the kill inside one
location. This rejects a common SPEC-valid pattern, and no listed loss covers it.

**Fix:** add the prototype's clause to the issue rule.

### 3. BLOCKING: losses missing from the OD13 list (plan §5.3)

**(a) P3 lists only half of what A's rules reject.**

- A:172's first disjunct `within(A, X)` meets any access *through* r with a Loan *on* r. So
  `let z = r@ref; r@follow = 5; inspect(z)` is rejected. The SPEC accepts it: "Inline parts exclude referents"
  (S15:500).
- B lists this as L2(b) (B:28). The old path also rejects it (`OwnedStoredReferenceTest.cs:230-235`), so it adds no
  ledger row, but it still needs a row.
- Fix: replace P3's text with B's L2 text, both forms, and B's counts.

**(b) The `replace` narrowing is A's own loss, not S1′'s.**

- A:46-49 files it under S1′. The committed S1 exempts a `replace` target.
- Round 2 shows the exemption is needed and sound (15a, 15b; P:352-353, P:370-372).
- Fix: list it as A's row. Recommend that the owner decide between this row and dropping the exemption in the S1
  proposal. Under "simple first", the row avoids a `replace` fact. If the owner keeps the exemption, implement it as
  an intrinsic descriptor column ("destroys its target in place"), not as an `IntrinsicId` case. That fix must cover
  both the formation (P3 deep sborrow) and the activation.

### 4. NON-BLOCKING: the soundness argument for P1 and P10 is not literally monotone

**Location:** design A §2, at A:51, A:176 and A:177.

`content` and `refs` add E only "if Φ_E∈S or no L∈S is named". That clause is anti-monotone: when P1 merges, or P10
adds, a named Loan, E drops out.

In safe code nothing reaches it:

- Static storage holds only static-Origin shared Loans, which never conflict: `Owned` (S15:196-200) plus no `uniq`
  Origin bound to `static` (S15:196).
- A reference result whose Origin is not static must retain an input.

That leaves Opaque values and unsafe code. A:12's reason ("the Origin stratum refutes it") is the wrong one.

**Fix:** issue Φ_E for every reference value of unknown provenance and drop the "no named" fallback. Monotonicity then
proves "P1/P10 only add errors" as written.

### 5. NON-BLOCKING: two unconfirmed relaxations

- **Row 8.** Already raised at A:442.
- **Distinct Downcasts.** A's prefix `within` (A:167) treats distinct Downcasts of one Place as disjoint. B states the
  same in passing (B:148). The SPEC makes Case payloads overlap ("share a byte", S3:253), and S15:490-497 has no Case
  row. It is sound in practice, because one runtime variant is active. Both relaxations go to the owner together, and
  A's text should state them.

### 6. NON-BLOCKING: the bound misses terms, and no linear form is simple

**Location:** design A §6.2, at A:285.

**Missing terms.**

- **Kills.** The kill removes K from every σ (A:191): Σ kill points × holders.
- **Copy chains.** ρ_f grows with in-loop copy chains: `loop { r1 = r2; …; rn = x@ref }` takes n passes (B's K1,
  B:321).

**The Σ|node| term.** A's Σ|node| term (A:305-308) is linear per dimension, as R4-M20's dense terms are (DEC:550).
B's argument does not remove it. Dense ⌈L/64⌉ sets with memoized union turn it into memo misses × ⌈L/64⌉ and
K·⌈L/64⌉ memory.

**No simple linear form.**

- Checking only candidate Loans costs candidates × live nodes.
- Per-Loan counts by differences is R4-M24's code.

**Fix: the owner restates V16 for A5 (plan §4.6).**

- Work ≤ c·(input + output + Dense).
- Dense = heads × flow holders + Σ accesses × distinct live-node sizes + Σ kills × holders + passes × N.
- `MirBorrows.Work` counts these terms.
- The 2× and 4× scaling tests add the K1 shape and a k-overlap shape.

### 7. NON-BLOCKING: the writeback code mapping is inverted

**Location:** design A §4.1 Update (A:135) and the A6 mapping (A:243).

The old path codes PlacementActivationConflict as an activation not at an invocation (`OwnershipModel.cs:238`). That
is the conflict *at the write*. S13:595 says "protected like a call reservation… The exclusive Loan becomes live at
the write".

A's shared guard ends before the store, so the conflict at the write gets the ordinary code. A maps Placement to
in-window conflicts instead. See graft 2 for the fix.

### 8. NON-BLOCKING: the Origin boundary is acceptable but under-recorded

**Location:** design A §7, at A:352-367.

A's split (A5 takes the old Analysis's role; Binding keeps fixed–fixed body judgments) reports each failure once by
partition. It needs no new rows and deletes nothing in Binding, so under "simple first" it beats B's. But:

- **Not OJ judgments.** Fixed–fixed body fits (B1, B2, B4, B9) are not OJ1–OJ7 (PLAN10 §5.5, lines 316-322). PLAN10's
  deletion row lists `CallRelations` and `ResultPremises` judgment parts for "R6, R7". Record in DEC that plan §3's
  "chains inside bodies" means chains through an inner node, and that those Binding parts go at R7.
- **Ledger.** The R4-M4 compared set gives all of `Binding.OriginRelation` to A5 (DEC:470-471). Split it by owner, or
  every fixed–fixed record stays a permanent `missing` row.
- **T1.** Add B's T1 rule: Binding leaves clauses it proved or failed as Constraints out of the rows (audit-origins
  §7.1).
- **Unowned items.** The local region for no principal candidate (S15:348, `Binding.OriginProof.cs:443-449`) and the
  unresolved-obligation residue (`A/OwnershipAnalysis.Borrows.cs:45-47`) are open issues at A:369-373. They need units
  before 2a.

### 9. NON-BLOCKING: per-declaration `Retains` against the instantiated contract

**Location:** design A §4.3 (A:157).

A computes `Retains` "independent of the Subst". S15:529 decides retention by the instantiated public contract, and
"an unproven absence of retention never permits access".

**Fix:** prove that the per-declaration rows contain the instantiated ones (Type-parameter carriers), or key them by
(callee, Subst) as B:131 does. An under-approximation would accept programs the SPEC rejects.

### 10. NON-BLOCKING: state the destroy exclusion as a partition rule

**Location:** design A, at A:209 and A:452. B has the same exclusion (B:175).

`!(destroy ∧ body root ∧ kept only by _0)` is sound only if every Loan that reaches `_0` exactly passes a fit to a
fixed Origin. Loans that over-approximation adds are harmless to exclude.

**Fix:** state the lemma and record the rule in DIAGNOSTICS. Otherwise it reads as duplicate suppression.

### 11. NON-BLOCKING: units against plan §6.2

**Location:** design A §10 (A:420-438).

- **Ledger.** The step-0 ledger re-baseline is missing. Add it first; 0a's "ledger unchanged" needs it.
- **Prototype first.** 0f gates the design, so run it first. It lands in `draft/Sketches` with the step-0 records
  (plan §5.2).
- **Split 0e.** 0e bundles step-1 facts (C1 and C8 `Retains`) with the Binding views of other families: C2 (F7), C5
  (F6), OriginObligations (F4) and failure keys (A6). The plan orders "Binding fact unit(s) → rule unit" per family.
  Move C1 and C8 directly before 1a, and each other view before its family unit.
- **Boundary units.** The Origin boundary table is delivered as design (§7), but its Binding units are missing
  (finding 8).
- **Step 1.** Step 1 matches the plan: F1, F2, F3 and F10 come together, and R4-M12 applies from the end of 1a.

### 12. NON-BLOCKING: errata and records

- **base_op.** Round 2's finding (K over Places, P:365-369) is a plan §4.3 erratum. A's `hits` already ranges over
  Places, which explains 9h's earlier error.
- **Case count.** The count correction (39, not 41) changes no verdict. It corrects plan §5.2 ("41 programs"), A's 0f
  and B's U1b. Use the current 60, plus finding 1's cases. The census's 41 SPEC blocks are unrelated.
- **Depth-32 limit.** A4's depth-32 `Unsupported_Kd` (A:457) is plan S5. Cite S5 rather than opening a new question.
- **G3.** G3 "closed by design" departs from plan §3, which restores A2's drop-row path. Record the departure in DEC.
- **Graft 4.** Graft 4's "interned projection chains" must not mean A5-side interning, which DEC:583 rejects for
  analyses.

## Soundness hypotheses (task 3)

- **Syntactic access without authority sets.** It holds on all 60 prototype cases (P:394-400; 9i errs earlier at
  `z = r@ref`), with finding 1's fix and P3 as widened in finding 3. The argument at A:76-80 is Rust NLL's, and Kimi
  adds no aliasing path it misses: rc and arc payloads are read-only past a shared layer (S15:509), and raw anchors
  are unsafe.
- **Check-time closure without feedback.** It holds, because borrowing T is a use of T (4b, 17a, 17b). A Loan reaches
  a holder only from a live holder, and E is always live.
- **P1 and P10 only add errors.** True in safe code. The literal argument needs finding 4's Φ_E.
- **Counterexamples found.** Finding 1, in two forms. No other: statics cannot hold conflicting Loans (finding 4).

## Graft decisions

1. **Descendant Loan: not taken.** It is not shorter: A's Φ_c with its Disturbed set matches B's Ld with its
   complement lane. Worse, B issues Ld for every inner-retaining call, including concrete callees, so a concrete
   Iterator's kept result conflicts at the next call unless the callee is Preserves (B:198, B:210-213). That is an
   unlisted loss against S15:517, which uses the callee's effects. A must still state Φ_c's place (receiver referent)
   and mode.
2. **TwoPhaseUniq restatement: taken, narrowed to writeback.** It does not remove G5 or G6 work; B has the same
   contracts. It does remove A's `UpdateGuard` role and fixes finding 7. The contract: TwoPhaseUniq activates at the
   Call it is an argument of, or at the writeback write through it; `EndBorrow` never activates. One restriction: a
   write through `$w` ends no Loans (S4). A Loan-carrying compound destination therefore becomes a listed loss. Count
   it; I expect lib 0 and ms 0.
3. **Bound argument: accounting only.** Take B's pass-count term and K1 shape into A's formula. Do not take dense
   bitsets as a "fix"; they relabel the term (finding 6). The owner restates V16.
4. **Overlap text: taken as text.** Use B's step classes (borrow, own and raw, with raw anchors compared only with
   derived Places, S15:496) and its Index rule for A's `within`. Compare MIR projections directly, with no A5-side
   interning. State the Downcast relaxation (finding 5).

## Owner decisions the final design needs

1. **OD13 loss list.**
   - Needed: P1, P3 (widened, finding 3a), P4 and P10.
   - Optional: P5, P6 and P9.
   - The `replace` row, or dropping the exemption in S1 (finding 3b).
   - The writeback-destination row, if graft 2 is taken.
2. **Relaxations:** row 8 and distinct Downcasts (finding 5).
3. **SPEC clarifications:** c1 (inferred); c2 is dissolved by P6. S3, S4 and S5 cover the A4 depth limit.
4. **V16 restatement** for A5 (finding 6).
5. **Origin boundary.** A5 takes the old Analysis's role at R6-check. Binding's fixed–fixed body judgments move at R7,
   which supersedes PLAN10's R6 share of its deletion row. The R4-M4 compared set is split (finding 8).
6. **MIR contracts:** the TwoPhaseUniq writeback activation (graft 2), the RawSlice backing operand, `MirCallee.Arg`
   for Value callees, and the restated `EndBorrow` meaning.
7. **Plan departures recorded in DEC:** G3, G4 and G7 closed by design; Relate and Clauses consumed only inside
   Binding's `Retains`.
8. **DIAGNOSTICS:** the destroy partition rule (finding 10), the guard `ended` code, and the E5 policy.
9. **Not needed:** no DEC O1 closed-vocabulary entry. ARCH's six MIR vocabularies are unchanged, and graft 2 also
   removes the `MirLocalRole` member, which DEC:586 treats as vocabulary.
