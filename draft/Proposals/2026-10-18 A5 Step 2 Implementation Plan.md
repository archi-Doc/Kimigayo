# R4 A5 Step 2, A6 and R4's Exit: Implementation Plan

Date: 2026-10-18

Status: Adopted working plan. On 2026-10-19 the owner adopted it as R4's formal plan for A5 step 2, A6 and R4's exit
(OQ1 (a)) and answered all of OQ1–OQ13 at once, packet 2 before its triggers (§2.2 records the outcomes; DECISIONS
R4-M74). OQ3 is conditional on round 2x-P6, OQ6's fifth item on R5's acceptance (AR-7), and OQ10 prefers (b). The
text below is otherwise the 2026-10-18 proposal, written by the R4 planning session from nine read-only audits of 2026-10-18
(families A5-F4, A5-F5, A5-F7, A5-F8, A5-F9, A5-F6, the exit, the ledger census and the cross-cutting constraints). The owner
asked for a detailed implementation plan for step 2, cut into small verifiable units in the format of the adopted R5/R6 plan,
with unit tables, dependencies, sizes and owner questions. Revised after six independent reviews (SPEC and soundness, unit
granularity, dependencies and holds, size and cost, process, completeness); each finding is applied or rejected with a reason
in the planning session's change record. It changes no SPEC, compiler or test.

Base: `dev` at `7ecef035` (1c-1 and 1c-2 merged, DECISIONS R4-M67). Coordination facts are refreshed against `origin/dev`
`522139c7`: the owner paused all R4, R5 and BU work on 2026-10-18, and no unit of this plan starts before the pause is lifted.
Ledger counts are from the a5-1b5-b1 run at `806110fc` (2,961 rows, 263 signatures, 440 open blocking rows); they predate 1c,
G148 and R5's F12–F15, so 2x-T2 re-measures them. Every count below is a planning figure; a unit's completion condition names
"the rows 2x-T2 assigned to it", with the old-base count as (H) context.

Conventions:

- A5P: [2026-10-16 Complete Polonius A5 Plan](2026-10-16%20Complete%20Polonius%20A5%20Plan.md); A3: `draft/Sketches/A5 Step 0/design-A3.md`;
  R5P: [2026-10-17 R5 and R6 Implementation Plan](2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md); DEC, ARCH, PLAN,
  STATUS, VERIFICATION, DIAGNOSTICS, CODEMAP: the documents in `docs/dev/` and `docs/`.
- PD1–PD31 are this plan's planning-session decisions (§2.1) and OQ1–OQ13 its owner questions (§2.2); §2.3 records how
  contradictions between the audits were resolved.
- External IDs keep a prefix so they never collide with this plan's unit IDs: `A5-F4` (an A5 family; R5 has units F0–F17),
  `R5-F10`, `R5-K5`, `R5-Q17`, `R5-Q21` (R5P units), W1/W2 (R5's builder windows), `A-4` (R5P's asks to R4), `AR-n` (this
  plan's asks to R5), `AB-n` (its asks to the Binding (BU) session), `1c-5` (step 1's units), `G147` (PLAN), `R4-M72` (DEC).
  Errata and prototype rounds are named by content (ER-rows, ER-effects, ER-closure-stores, ER-defaults, ER-preserves,
  ER-derived) and numbered at adoption (PD4). V16 shapes are named by family ("F7 captures") until adoption; A3 §6.3's
  numbered shapes (1)–(9) keep their numbers.
- Paths: `B/` = `src/Kimi/Compiler/Binding/`, `M/` = `src/Kimi/Compiler/Mir/`, `T/` = `tests/xUnitTest/Tests/`,
  `S2/` = `draft/Sketches/A5 Step 2/` (new).
- Verification rows (AGENTS): **Doc** diff and reference review; **Comp** unit checks of the named classes plus a Session at the
  end of the working session (`KimiLibraryShapeTest` added for library changes); **Tool** affected workflow checks; **Bench**
  Session with the whole-solution build plus the named measurement; **Evid** evidence only, no source change; **Draft** a
  prototype run recorded in `prototype.md` with no unsound acceptance, no unlisted rejection and earlier cases unchanged.
  Suffixes: **+cen** the census test 2x-T1 (zero MIR problems of every analysis, A1–A6, in the library, the 44 milestones,
  the 31 `tests/mir` goldens and the accepting SPEC variants; R4-M12); **+led** a ledger run (mir Functional with
  `KIMI_MIR_DIFF`) in which the unit's listed rows pair or are classified and no new unclassified signature appears; **+V16**
  the named scaling shape at 2× and 4× on its Work counter, with its VERIFICATION row added in the same commit; **+warm**
  `MirBorrowsTest.AWarmRunAllocatesNothing` (or the component's own warm-run test) extended with the family's forms;
  **+cold** a cold Bind measurement over the library and one milestone (time and allocations, pinned interleaved A/B,
  compiler id checked), the new view's retained bytes and the affected `Purpose=Allocation` regressions updated (AGENTS
  Performance); **sel** the sel pending check, and from R5's G3 the affected native families and O2 kernels (ask A-10,
  DECISIONS R5-M8); **bp** VERIFICATION's behavior-preserving triple; **snap** the diagnostic snapshot difference classified
  record by record; **both** under `-Pipeline legacy` and `mir`.
- Review: **FP** full process (two independent designs, design review, final review); **D+R** one design and one review
  under an owner-approved full-process design recorded in DEC (DEC Q6 form 1; the design is named); **R** one review;
  **G:name** the shared review of a group an owner-approved design names (DEC Q6 form 2; no merge into dev before it passes;
  each unit still verified and pushed): G:A5-F4, G:A5-F5, G:A5-F7, G:A5-F8, G:A5-F9, G:A5-F6, G:A6; **final** the end-to-end
  review of R4-M35; **—** none (evidence, or a DEC Q6 form 3 record).
- Hold: **W1 S-x** a slot in R5's window W1 (`MirBuilder.Calls*`, `Control`, `Operators`, `Drops`, the `$abort` site,
  `MirValidator*`, `tests/mir` goldens, and for R5-Q21 then Q16 `MirBuilder.cs`, `.Places`, `.Formatting`, `.Defaults`,
  `.Match`, `.Expansions`, Q16's `.Values` site); **W2** R5's `MirProgram.cs` window; **adv** a PLAN §3 hold line for an R5-M7
  file (`TyCtxt.Retention.cs`, `TyCtxt.Effects.cs`, `BindingModel.cs`, `MirDifferenceLedger.cs`, ...); **drv** the shared
  driver `M/Analysis/MirAnalysis.cs` (R5 never edits it, R5P PD6); **sh** another shared Mir file outside R5's windows;
  **BU** a merge-first line with the Binding session's branches; **D5** an old-Analysis edit (owner exception); **no**
  R4-owned or new files (ARCH: R4 owns `Mir/Analysis` except the driver). The planning session records each hold line in
  PLAN §3 before its unit starts; the unit's merge removes it.
- Difficulty: 1 mechanical; 2 small with a clear rule; 3 contract fixed, correctness needs care; 4 a new representation, a
  cross-phase move or an open design choice; 5 open algorithm (none in this plan).
- Sizes: code `+added/−removed`; `t` test lines; `move +a/−b` a verbatim move by responsibility.
- "(H)" marks an estimate or an inference not checked by running code. Every size and difficulty is (H).

---

## 1. Scope and what exists

**Scope.** A5P §6.2 items 3–5 and A3 §10's rows 2a0–2g and Exit: step 2's families, each as "Binding fact unit(s) → rule
unit" (A5-F4, A5-F5, A5-F7, A5-F8, A5-F9, A5-F6), A6's completion (2g), and R4's exit tasks (A5P App. B U10–U12 and E9, the
final end-to-end review). Out of scope: step 1's remainder (a gate, §3.4), R5's units, the BU session's G129/G130/G139/G145/
G152, G127 (owner, unscheduled, R4-M64) and G128 (R7).

**What exists at the base:**

- The joint core (A5-F1, A5-F2, A5-F3, A5-F10 rules) runs in the driver's step 4 in shadow since 1b-5. `VerifiedSpecExampleTest.Landed`
  is Binding, A1, A3, A4, F1, F3: F2 waits for 1c's second code and F4's 15.5/0. No spec case has family F10; rule 22's
  assumed-state conflicts have one MIR test (`MirBorrowsTest.LoansFlowOverJoinsLoopsAndCheckOnlyCode`), the abandoned
  comparison none (2x-T7).
- Binding views: only C1+C8 (`TyCtxt.Retains`, R4-M51, R4-M63, BU-1, BU-3). C1-F4 (`OriginObligations`, `OriginAtom`,
  `UniversalRelations`), C2 (`TyCtxt.Closure`), C5's preserves rows and E4/E5 keys do not exist; G124's Transferable exists
  nowhere (STATUS:102). Over the 5,000-token limit and touched by step 2: `Binding.cs` 13,208, `Binding.Diagnostics.cs` 12,601,
  `Binding.FunctionTypes.cs` 7,522, `Binding.OriginDeclarations.cs` 5,975 (read only), `Binding.Closures.cs` 5,911.
- MIR: no Origin stratum, no effect rule, no F6 code, no producer of `DefaultArgumentAccess_Kd`/`DefaultArgumentBorrow_Kd`; a
  temporary environment-to-result rule for closure calls (`MirBorrows.Calls.cs:16-34`); template calls take unknown-callee
  rows (R4-M57, R4-M63), whose count is quadratic in the argument count (`Calls.cs:75-87`).
- Prototype (`A5RulesPrototype.cs`, rounds 1–13 with case 75's correction recorded, 332 cases) models no Origin stratum,
  statics, closure contracts, defaults or match Subjects; writeback is "argued, not run"; F6's excuses are hand-given. G148's
  round 14 is in scratch only.
- Tracking: `MirAnalysisTest.TheLibraryHasNoProblems` tracks R4-M12 for the library (every analysis); the milestone, golden
  and SPEC-variant census is scratch. No OldRule target-side format exists (ask A-4). VERIFICATION lists no V16 shape.
- Ledger: 440 open blocking rows: 182 unclassified U rows (G106) and 258 (b) rows (a5-old-loans 108, a5-old-origins* 86,
  A5-F6 40, A5-F9 15, a5-old-effects 9); 1,899 non-blocking open (b) rows; 15 unclassified signatures. Codes outside its
  comparison: TransferRequired (A-13), control-flow lints, `OwnershipCaseLimit` and the effect records `IsEffectRecord`
  filters (`MirDifferenceLedger.cs:45, 280`).
- Coordination at `522139c7` (paused): R5's W1 is open (F11, F0, F8, F9, F12–F15 landed; F10, F17, F2b, K9a/b, K10b/c and
  Q17 pushed but unmerged; R5-F7, Q21 and Q16 follow in W1, and Q21/Q16 extend W1 over `MirBuilder.cs`, `.Places`,
  `.Formatting`, `.Defaults`, `.Match`, `.Expansions`; F17 adds `MirBuilder.Drops.cs`); W2 waits for R5-F10; R5's K lane holds
  `TyCtxt.cs`, `Binding.ClosureInstances.cs` and one line of `Compilation.Pipeline.cs`; R5's C lane holds `BindingModel.cs`
  (F2b); R5-Q17 holds `MirDifferenceLedger.cs`. R5's F14 and F15 closed G147 (4)(5). The BU session (paused) has `bu1`
  merged into the base, `bu2-g129` and `bu2-closure-results2` on branches, G147 (2) on `claude/bu4-index-uniq` (review round 2
  pending), unit 0c held for A5-F7, and G129's MIR unit U5 to come (`MirBuilder.Values.cs`, `MirBuildTest.cs`).

**Step 1's remainder (gates, not step-2 units).**

- **G148** (a call's store rows past the first layer; designs in scratch `g148-a`, `g148-b`). Expected at the plan's writing:
  one A5 function W over exclusive layers with route exclusion X, an owner question on a SPEC 15.6.4 sentence, and listed
  losses P13/P14. Its design review (`rv-g148`, partial: stopped by the pause) found both designs' exclusions, and both proposed
  SPEC sentences, unsound (probe `s1`: a callee that takes the layer-1 reference out with `exchange` and stores a reference into
  the same struct). In its union prototype (382 cases) SPEC 15.6.4's letter as written (rule G: a call's store reaches every
  holder the argument reaches, in any mode, with design b's Type guard and no exclusion) is sound, and keeps Milestones 13, 24,
  27 and 29 at zero problems; GXS (X only where the target's Type cannot refer into itself: a new `MirTypes` fact sharing the
  Holding walk with `HoldsLoans`/`HoldsRaw`) is sound with fewer rejections but is a third design that takes the full process.
  Under G no SPEC change is needed; the corpus and ledger check under G is the review's open item. This plan names W "with
  G148's adopted exclusion" (none under G).
  A5-F7's capture stores (2c-3) and A5-F6's desc(c) into storage (2f-3) reuse W; A5-F8's collection verdicts follow it;
  2b-5's `MirTypes` change and 2e-1's unknown-callee rows wait for it.
- **1c** (adopted after its design review, `rv1c-design/review.md`; 1c-3 and 1c-4 under way at the pause): 1c-3 the
  guard-view skip; 1c-4 the bp split of `MirReport` (→ `MirProblem.cs` + `MirReport.cs`), `MirBorrows.Checks` (→ `.Checks` +
  `.Reports`) and `.CheckSets` (→ gather + `.Candidates`); 1c-5 activation conflicts only with shared-mode Loans (R4-M68, with
  a condition for A5-F6); 1c-6 A1's `DefaultArgumentMove_Kd` and the amended Inlined rule (R4-M69); 1c-7 the default taint per
  pending slot D until A5-F9, and option P: an Opaque value carries only `{Φ_E}` (R4-M70); 1c-8 `MirProblem.Body`, key (code,
  subject, body) (R4-M71); 1c-9 records with R4-M72's A6 table (err(effect) is `ComparisonLoanConflict_Kd` at the call;
  `CallEffectConflict_Kd` is A5-F6's Φ_c at the later call; `DefaultArgument Access/Borrow` are A5-F9's; E7/E12's merge moves
  to 2a). Two owner questions of 1c are pending (one final review for 1c-5–1c-8; whether a one-argument propagation counts
  for the hotspot rule). After 1c, `.Reports.cs` has taken three changes (1c-6, 1c-7, 1c-8) and is due (§3.7).

---

## 2. Decisions

### 2.1 Planning-session decisions

Each is recorded in DEC by the commit that first implements it (AGENTS "Record decisions"); the unit is named in §4 and §7.
† marks a departure from an owner-approved text; it is repeated in OQ6.

| ID | Decision | Reason | Deletes or avoids |
| --- | --- | --- | --- |
| PD1 | **Order, chain and lanes.** Units that edit `MirBorrows*.cs`, `MirReport*.cs` or the driver form one chain and merge in this family order: A5-F5, A5-F9, A5-F4, A5-F7, A5-F6, then A6's 2g-1, 2g-4b, 2g-5 (§3.2). A5-F8 adds no code to those files. Within the chain a unit stacks on its predecessor's pushed branch, also across groups; groups merge into dev in chain order. The planning session may move a ready unit ahead of units that have not started, recording the new order in PLAN §3. Binding views, A5-F8's builder and test work, tooling and drafts run in parallel lanes on disjoint files (§3.1) | A5P §6.2 lets the audit change the order; A5-F5 and A5-F9 have the shortest preludes, A5-F4's waits for OQ11/OQ12 and two BU-reviewed Binding units, A5-F7 needs A5-F4's stratum (2c-6) and G148, A5-F6 needs A5-F4 and C2; one known order makes the hotspot counts decidable | Rebases on shared files; an undecidable hotspot trigger |
| PD2 | **Step-1 gates.** No step-2 unit that edits `MirBorrows*.cs`, `MirReport*.cs`, `MirEffects*.cs`, `MirTypes*.cs` or `classification.tsv` starts before 1c-9 and G148 merge. Drafts and designs need only the adopted designs: 1c's (R4-M68–R4-M70) and G148's decision | 1c and G148 serialize on those files and fix the codes and W that step 2 builds on; a draft needs the rules, not the code | Double rebases; codes changing under finished tests; drafts waiting for code |
| PD3 | **Prototype first.** One round per family before its rule unit (2x-P4–2x-P9), plus 2x-P10 for A6's derived rules and A2's drop-`self` Move, each run and reviewed. G148's round 14 and 1c's `ActivationRefOnly+PK` variant enter the tracked draft first (2x-P0); rounds on `A5RulesPrototype.cs` complete after 2x-P0 in the order 2x-P5, 2x-P9, 2x-P7, 2x-P6, 2x-P8 (a round may be drafted earlier on a branch, then rebased and rerun with earlier cases unchanged); 2x-P4 and 2x-P10 are separate files | The owner's standing rule; prototype parity at X-5 needs every round on one model | Mid-unit discoveries of unlisted losses; verdicts from a stale model |
| PD4 | **Names.** The audits each proposed "E15" (F4 row shape, F5 effects, F9 step class, G148) and "round 15" (F8, F6). Errata and rounds are named by content and numbered in DEC at adoption, after G148's | Parallel sessions collide on numbers (PLAN G-number precedent) | Renumbering commits |
| PD5 | **Review weight.** One FP design round per family that adds a representation or departs from an approved contract: 2a0-D, 2b-D, 2c-D, 2e-D, 2f-D, 2g-D. Each design unit completes only when the owner has approved its package (OQ2) and DEC records the approval naming the group's units (R5-M18's pattern); its Binding views then take D+R and its rule units the group. C2 is FP (A3 §4.3's row covers only receiver mode and rows). Transferable, A4's Keys classes (G93) and C1-F4's row shape are decided inside their family's round. A5-F8 needs no design round: its setter writeback is D+R under R4-M37, its rule units are coverage tests under A5P §9.3 (OD6). † G93's classes depart from A3 §4.2's `bool` (R4-M38); the row shape extends A3 §4.3's C1-F4 tuple | DEC Q6 and A5P §9.3; R5-M18/R5-M23's package pattern | About 12 separate double designs; units starting under weights that are not yet valid |
| PD6 | **Binding units: R4 or BU.** R4 implements the views A5 is the first consumer of, each in a new partial file with a BU reviewer: C1-F4 (2a0-2), the Origin boundary (2a0-3a, 2a0-3b), Transferable (2b-4), the receiver predicate G147 (3) (2c-1), C2 (2c-2), C2's bound result (2c-5), C5 (2f-1, 2f-2). BU implements Binding acceptance rules and its held units: 0c (2c-4), G147 (2) (`bu4-index-uniq`, adopted by 2d-9), G100's static-Borrow record (2b-6), drop-`self` Take check (2x-E4a), destruction-receiver helper borrow (2x-E6) and Subject getter-result restriction (2x-E7). Disagreements go to the owner | One producer per fact, first consumer A5 (A5P §7); BU owns Binding rules and its branches; PLAN gave G147 (3) to BU, but C2 publishes the same predicate | Two producers; idle waits on another session |
| PD7 | **Codes and owners from R4-M72.** err(effect) is `ComparisonLoanConflict_Kd` at the call; `CallEffectConflict_Kd` is A5-F6's Φ_c. So signature 5e1a449cc7bf's 9 blocking rows (filed "F5 (unit 2b)") are A5-F6's (7 rule-1/rule-3 forms) and (a) (2 old over-strict, H); A5-F5 closes no blocking row. SPEC 8.11.4 rule 1 falls under A5P App. A row 10 (8.11.3–8.11.6) and is A5-F6's | The A5-F5 audit's recommendation (`CallEffectConflict_Kd` for every effect) is superseded by R4-M72; rule 1 shares Φ_c and carrier facts with rule 3 | A wrong F5 metric; a second effect code |
| PD8 | **A5-F4's boundary.** The timing of moving `RecordMismatch`'s inner chains (relations through local regions or inferred ends) is the owner's (OQ11): A3 §7 (owner-approved item 7) gives inner chains to A5 but deletes Binding's gate `B/Binding.Diagnostics.cs:985` only at R6-check, and A5P §6.2 says each failure is reported once "after R6-check". 2a0-3a implements the answer. 2a0-3b implements A3 §7's unowned items (an unresolved body inference is a local region; elsewhere Binding's `MissingOriginBinding_Kd`) and the "no principal candidate" record as SPEC 15.3.6's relation judgment, with S3's wording if OQ12 adopts it | No A3 basis for changing default-pipeline records now; the principal-candidate record is a defect against SPEC 15.3.6 | Two producers without a removal stage; a vanished default-pipeline rejection |
| PD9 | **A5 follows SPEC where Binding differs.** No port of the preserves-results containment skip (`Binding.ArgumentOperations.cs:618-668`, no SPEC basis); a raw Borrow is an inferred region (SPEC 15.6.5), not Binding's top-like Anchor. A library or milestone verdict change found by 2a0-0 is an O3 consultation (OQ7) before 2a-1a; a default-pipeline acceptance SPEC 15.6.5 refutes is a miscompile (A5P §9.2) that opens a Binding repair unit or a PLAN issue | SPEC is the authority; R4-M12; D5 allows miscompile fixes | A third reading of containment; an unrouted unsound acceptance |
| PD10 | **Reason in the key, only if needed.** If 2a0-0 (c) finds nodes with several failing chains, Origin problems carry (longer, shorter) as a fact and key part (2a-3), under R4-M11's (code, subject, Reason) merge and 1c-8's body | DIAGNOSTICS:159 (each chain is a problem); R4-M11 already decided the key | A blocking class of "second chain" rows |
| PD11 | **E12 merge contract (2a-2, 2g-5).** A6's merge drops only the `destroy` (storage-end) conflict on a Loan `_0` carries whose Borrow the Origin stratum refuted for that escape, and E7's `err(ended)` for a guard candidate whose `γ_H` reaches `_0`; never by Loan membership; every other conflict of the same Loan stays (`let r = x@ref; x = 1; return r` reports the write and the relation). For a PreparedInput-rooted Borrow that A5-F9 reports as `DefaultArgumentBorrow_Kd`, the Origin-stratum problem is the one dropped (2g-5) | One failure, one code (A3 E12, E7); SPEC 23.1.2 keeps independent errors; SPEC 7.2.3's specific rule (A5-F9 audit Q7) | A merge by Loan membership; a lost independent conflict; two codes for one default failure |
| PD12 | **A5-F5.** (i) G100's static Borrows: Binding reports the located `Unsupported_Kd` instead of `InvalidAssignment_Kd` (AGENTS: never a Language error for an unimplemented valid form); acceptance stays P42's after R6-check (R5-M13 (b)), so A5-F5 is tested on implicit static Loans, and the two F5 SPEC examples land with recorded (e) MIR differences naming P42 (2b-8, OQ9). (ii) staticEnsure under P5: a located `Unsupported_Kd` only where a live Loan on the ensured Field would decide, exact once P42 puts Type arguments on the operand. (iii) An erased destruction of a static's own old value conflicts with the destruction's exclusive Loan (SPEC 16.3.1 with 16.3.3). (iv) G136's static items close by a recorded lemma that a prototype case checks. (v) G93's key storage is measured before the design (2b-2 before 2b-D) | SPEC's letter; no new loss or SPEC change; pulling P42's check part into R4 would change R5-M13's timing | A shape gate; a new loss row; G93 guesswork; an unreachable exit gate |
| PD13 | **A5-F7.** The strongest receiver stays; a conflict whose only witness is a receiver Loan stronger than its view's case admits is a located `Unsupported_Kd`; per-case receivers are computed by C2 (2c-2) under 2c-D, and per-case callee acquisition is recorded in PLAN in case a program needs it (library 0, milestones 0, H). Capture stores reuse G148's W. Binding's `CallLocalOrigin` stays until R6-check's H11. No interim capture-storage rule (planning, 2026-10-18) | SPEC 7.7.3 per case; one store function; the default pipeline reports through `CallLocalOrigin` until R6-check | A builder vocabulary change; a second store rule; code before its design |
| PD14 | **A5-F8.** † U8p ("Place and backing dependency", A5P §7) is recorded superseded by R4-M40's `RawSlice` backing operand (in 2x-A0, with OQ6's answer); no Binding view. G147 (1): `placeEntry`'s declaration takes the destination Dictionary, so declaration retention (E14) stores its entries (decided with G148's design, same family). A5-F8 lands while G147 (2) is open (its rows block R4's exit, not A5-F8). G127 stays unscheduled (R4-M64) | One producer per fact; retention stays by declaration with no intrinsic row; G147 (2) is a selection defect, not an F8 rule | A second producer; an intrinsic-table row |
| PD15 | **A5-F9.** (i) `y: ref/i32 during a = x@follow@ref` is valid by SPEC 15.6.3 (a Borrow past a shared Deref forms no Loan), the old path over-strict (d). (ii) Inline copies: R-a recommended: SPEC 7.2.3's "Inspection Loans end before callee entry" as an A5 ending, at the omitting call, of every inspection Loan its defaults form (on PendingSlot roots and on reserved explicit arguments), which deletes R4-M70's default taint. The audit's (a), keeping the slot marker as a permanent drop, would turn the taint's every-set imprecision (1c review's j1) from a rejection into an acceptance; 2e-D compares both. (iii) P9 rows take SPEC's letter for reserved inputs. (iv) A default storing a slot borrow into storage the body does not own is `DefaultArgumentBorrow_Kd` (DIAGNOSTICS amended by 2e-2). (v) The related parameter is an Anchor fact, no new fact kind. † M39's Binding part (A5P §7) is void after OD8/OD9's non-adoption | SPEC's letter; R4-M70 makes the taint temporary until A5-F9; SPEC 23.1.2 | A permanent derived-drop rule; a fact kind; a rejection SPEC 15.6.7 allows |
| PD16 | **A5-F6 off the critical path as far as possible.** 2f-S1 (a sketch) starts after the pause; 2x-P6 and 2f-D start once 2x-P0 is done, during the step-1 gate. Rule I (2f-1–2f-3: P12's desc(c), G113's stores, rule 1, a located `Unsupported_Kd` where only rule 3 could excuse) lands without S2; rule 1 by exact carrier reach if round 2x-P6 shows it sound (otherwise a Place phantom with a listed loss, OQ8). Whether a stored Φ_c is ever excused is a SPEC reading (2x-S1, SPEC 8.11.3's "Loans kept after transfer"); until it is read, a conflict only a stored Φ_c causes is a located `Unsupported_Kd`. R4-M68's condition (a shared-receiver Φ_c takes the receiver's mode, or the variant reruns) is a 2f-D input | A5-F6 is last, and its conformance half needs S2; no planning-session SPEC reading without its round | R4's exit waiting for S2 longer than necessary; a conservative error |
| PD17 | **A6.** Under R4-M70's option P, A5 has no Opaque-derived Loan problems, so E5 narrows to A1, A3 and A4 problems rooted at Opaque sites. † Each keeps one cause site on the problem and H3 reads `Binding.FailureCauses` at publication, instead of A3 §4.3's "E4/E5 failure keys per Opaque site" in `MirOpaque`; R5's planning session reviews 2g-D and accepts H3's contract change (AR-7). G105's A1/A3 derived rules read Opaque access kinds (G136) and a completion-undecided flag the builder publishes in one W1 unit (2g-2a). Primary locations are decided once, after the last reporting family (2g-4a, 2g-4b). E4 (kept children) is R5-Q21 (ask A-14) | Shorter, no MIR column; the causes always publish; A6 last avoids reopening locations | A MIR key column; repeated location work; a silent change to R5's H3 |
| PD18 | **Ledger tooling (proposed; the owner decides in OQ5 (iii)).** A "restated" kind (2x-T3): an unpaired old row is restated only when a paired MIR record in the same body names the same Loan anchor or Move Path (A5) or the same A1 subject Place, so it states a problem the MIR reports; any other unpaired row stays `missing`, and a body where the MIR reports nothing still blocks. It would supersede R4-M47's unsound clause (a tooling-session decision). 2g-4b and X-1 classify each restated row as the same failure at another location or a missed independent error (a (b) defect of its family with a regression test); X-5 samples restated rows with the paired failure removed and checks that MIR still rejects. The 149 mutable-static U rows are classified (a) now and re-checked by 2b-8 against 2b-3. G106 is split by re-triage only; the old gate's identity in the raw row needs a D5 exception (OQ10, unit X-1a) | Plan §8's letter; 74 rows can never close otherwise (65 DUP rows, H); SPEC 23.1.2 keeps a missed independent error visible | An R4-M17 row-shape change; a frozen-path edit; a weakened gate decided without the owner |
| PD19 | **Tests.** One target-side helper over `MirDifferenceLedger.Sides` (R4-M45's one split) with per-row parameters; R4-M50's trait extended as `OldRule = "ledger:<source hash>:<line>"`, a key that a code change does not alter, for (a) rows the default pipeline still rejects; each family migrates the tests that assert its rules (A5P §8, OD3), H8a takes the residue; each family's MIR tests go into a new partial test file (`MirOriginsTest.cs`, `MirBorrowsTest.<Family>.cs`), not the 12.7k-token `MirBorrowsTest.cs` | xUnit v3 rows carry no traits (R4-M60); parallel lanes would conflict in one file; signatures include the code, which 1c-5 changes | A second ledger reader; churn at H5; stale markers |
| PD20 | **R4-exit dependencies.** G147 (2) (BU's `bu4-index-uniq`, 9 rows, with its regressions) and 0c are accepted as R4-exit dependencies with asks AB-2/AB-3; if G147 (2) has not landed when X-1 starts, R4 takes it under a hold line. G147 (2)'s validator half goes to R5's W1 (AR-8). G147 (4)(5) landed with R5-F14/F15 (2x-T2 checks their rows). Milestone 41's A1 rows do not hold the exit once 2g-2 derives them (R4-M47; PLAN G105) | Plan §8 does not let these rows wait for R6-check; R4-M47 already exempts Binding-incomplete compilations | An undecided exit dependency |
| PD21 | **Hotspots.** Every file a step-2 unit edits is counted at the unit's start against AGENTS' rule (three consecutive changing units since its last restructure); a due file gets a bp split by responsibility, or the planning session's recorded ruling that no split exists (as 1c did), before the change. §3.7 lists the files due or about to be due and their units (2x-R1–2x-R11, 2a0-1, 2c-0, 2d-1). Binding files over 5,000 tokens may not grow: each Binding unit is net ≤ 0 tokens in them (counts in the commit) or splits first (2x-R11 for `Binding.FunctionTypes.cs`). R5 restructures the W1 files whose next change is R5's (`MirBuilder.cs`, `.Places`, `.Defaults` before Q21/Q16); 2d-1 is the `.Expansions` split | AGENTS' three-consecutive-units rule; ARCH structure limits | Overlapping restructure units; growth of over-limit files |
| PD22 | **Census as a tracked check (2x-T1).** R4-M12's condition (no problem of any MIR analysis in the library, the 44 milestones, the 31 goldens and the accepting SPEC variants) becomes a selectable test; `MirAnalysisTest.TheLibraryHasNoProblems` moves into it; VERIFICATION names it for every unit marked +cen (a check-selection change the owner adopts with OQ1) | Today only the library is tracked; the rest is scratch | Unverifiable completion claims; a duplicate library assertion |
| PD23 | **SPEC readings (2x-S1).** G147 (8ii) (A4-box-ok, A4-field-ok), af43dcd8ffce (a write through an `insertOrReplace` result), 47f0722c876b (a Callable replaced between calls, 8.11.6), 59e2289ce4f4 (a concrete generic helper calling `take`) and PD16's stored Φ_c are read with prototype cases; an outcome that needs a SPEC revision or a new loss goes to the owner (OQ8) | Each decides a classification row or a family test | Rows left unclassified at the exit |
| PD24 | **Holds.** R4 asks R5 for W1 slots (AR-1), records its own hold lines for shared Mir files outside the windows and for the driver, takes `MirDifferenceLedger.cs` after R5-Q17 (2x-T3, 2a0-4, 2g-1; X-1a if needed), and never edits the K lane's files (new partials; C2 reads `ClosureSignature` and `SubstOfClosure` only) | ARCH's protocol and R5-M7 | Silent conflicts with R5 |
| PD25 | **Ask A-3 is met** (`T/MirBorrowsTest.cs:1022-1071`); the CROSS audit's unit for it is dropped | The exit audit found the rows | A duplicate unit |
| PD26 | **Non-family repairs belong to step 2:** G103-E9a and E9b's non-jump parts with G95 (ask A-6, which gates R5's G0 and X1), G122, G100's drop `self` and destruction-receiver and Subject items, G136's V16 clearing (2x-E1–2x-E7). E9b's jump part is R5-F13's (G143) | Plan App. B E9 "before the R4 exit"; no family owns them; R5P §3.4 | Unowned exit and R5 blockers |
| PD27 | **A5-F2 and A5-F10 landing.** A5-F2 joins Landed with 2a-4 (its 15.5/0 variant is A5-F4's); A5-F10 is completed by MIR-layer rule tests for App. A rule 22 (2x-T7), and joins Landed only when an F10 variant exists; a gap stops for the full process | Both are step-1 families not yet in Landed; no spec case has family F10 | An exit claim on unchecked families |
| PD28 | **The exit's Unsupported census (X-2)** lists every MIR `Unsupported_Kd` producer; every unit that adds one that remains registers it (a STATUS row with its SPEC form); no family-wide gate may remain; the remaining SPEC rules left Unsupported and their removal stages go to the owner as one list (OQ9) | DEC O4; AGENTS (postponed stages need owner approval) | Piecemeal exception requests |
| PD29 | **Groups and holds.** A unit that holds a W1, adv or BU file is outside its family's group and takes its own review (2b-5, 2c-2, 2g-1, 2g-2a; 2d-2 and 2d-3 already), so no R5 or BU hold lasts until a group review. Driver edits stay in their groups (an R4-only file serialized by PD1). Records units belong to their group and land in its dev merge | ARCH removes a hold only at the unit's merge; a group merges at its end | R5's Q16 or BU's 0c waiting for whole families |
| PD30 | **Interim rule for undecided SPEC text (R4-M39, A5P §5.3).** Between an owner decision and its SPEC integration, a program valid under the current SPEC that a new rule (S2, S5, an OQ8 loss) rejects is a located `Unsupported_Kd`, and tests of the affected SPEC examples expect it; the unit after the SPEC session switches it to the Language code (2f-7 for S2) | The tests follow SPEC, never the reverse | Language errors for valid forms |
| PD31 | **Facts per code.** Every new MIR problem code publishes the fact rows DIAGNOSTICS, A3 §6.4 and R4-M3a name (A5-F4's relation and chain ends, A5-F5's static key and call, A5-F6's Φ_c call, A5-F9's Anchor parameter), checked by a test in its first producing unit | R5's Q18 and H2 read them; the adapter may not re-derive them | Facts missing at R6-check |

### 2.2 Owner questions

"Blocks" names the first unit that cannot start. Packet 1 is sent with the plan; packet 2 is asked only when its trigger
fires.

**Outcomes (owner, 2026-10-19; DECISIONS R4-M74).** OQ1 (a); OQ2 (a); OQ3 (a) if round 2x-P6 shows S2 sound, else (b);
OQ4 (a); OQ5 (i)(a), (ii), (iii)(a); OQ6 (a), its fifth item once R5 accepts H3's contract change (AR-7); OQ7 (a); OQ8 (a);
OQ9 (a); OQ10 (b) preferred; OQ11 (b); OQ12 (a); OQ13 (a).

**Packet 1: send now.**

| ID | Question | Options | Recommendation | Blocks |
| --- | --- | --- | --- | --- |
| OQ1 | Adopt this plan as R4's implementation plan for step 2, A6 and the exit (this document in `draft/Proposals`, step-2 design records in `S2/`, one PLAN §3 pointer line), with its exit gate (§5.4), and the VERIFICATION change that names the census test (2x-T1) for every unit marked +cen? | (a) adopt; (b) adopt with changes | (a) | 2x-A0 and every code unit except 2x-T1's class (drafts and probes may start) |
| OQ2 | Approve each family design package (2a0-D, 2b-D, 2c-D, 2e-D, 2f-D, 2g-D) once, after its design review, as the owner-approved design under which its Binding views take D+R (DEC Q6 form 1) and its rule units the group G:A5-F4 … G:A6 (form 2)? | (a) per package after review (the R5-M18, R5-M23 pattern); (b) every unit its own review, cross-phase units FP | (a): about 20 fewer reviews (H); each package passes the full process first | each design unit's completion, so every D+R and group unit |
| OQ3 | A5P-S2: adopt "Preserves Results by Receiver Place" as revised by the 2f-S1 sketch (receiver-Place relation, invalidation, Move carry, conformance as an ordinary body with an earlier result), decided on round 2x-P6's evidence, and instruct the proposal's revision from the sketch and its SPEC integration in a separate session (2f-S2)? Until 2f-S2, forms S2 rejects that the current SPEC accepts are a located `Unsupported_Kd` (PD30) | (a) adopt whole; (b) conformance half first, caller rule 3 left a located `Unsupported_Kd` at R4's exit (an O4 exception, R1g's status quo, library/milestone uses 0, H); (c) reject: rule 3 and conformance stay Unsupported, and after R6-check's H5 the 12 library Iterator conformances would be unchecked | (a), fallback (b); (c) is not viable | 2f-4, 2f-5, 2f-S2; R5's ask A-7 and H5 |
| OQ4 | When Binding binds receiver-dependent closure-call results (2c-5), may the default pipeline keep its verdict through a gate in the old Analysis at the same call (`Ownership.Unsupported`, deleted at R6-check)? It is a D5 exception that adds no feature | (a) the gate; (b) keep Binding's `Unsupported_Kd` until R6-check: A5-F7 incomplete at R4's exit (an O4 item), the 15.8.2 example and ≈ 15 forms unchecked on MIR; (c) open both pipelines: the old Analysis was never verified on the form (against D5) | (a) | 2c-D, 2c-5 |
| OQ5 | R4's exit criteria (§5.4): (i) open (b) rows; (ii) the proof; (iii) the restated kind as PD18 narrows it (a row is restated only when a paired MIR record in its body names the same Loan anchor, Move Path or A1 subject), superseding R4-M47's clause | (i)(a) blocking 0 and no open (b) row naming an R4 unit or R4 G issue, any other (b) row naming a non-R4 owner with your approval; (i)(b) blocking 0 only, (b) rows to R6-check's H3b; (ii) the mir ledger measurement at the exit head (0 open blocking, 0 unclassified signatures, Landed = 14 families with A5-F5's recorded (e) variants, A6's contract test), the codes the ledger does not compare with their stand-in tests, the census test and the final review; (iii) (a) confirm; (b) no restated kind: the rows stay blocking until each pairs | (i)(a), (ii), (iii)(a) | 2x-T3, X-1, X-6; H3b's scope |
| OQ6 | Confirm the departures from owner-approved texts as one list (the OD13 pattern): A4's Keys classes replace A3 §4.2's `bool` (R4-M38; PD5); C1-F4's row tuple gains Equality, Source, Clause and destination Type (A3 §4.3; PD5); U8p superseded by R4-M40 (A5P §7; PD14); M39's Binding part void (A5P §7; PD15); E5's cause site on the problem instead of keys in `MirOpaque` (A3 §4.3; PD17, with R5's acceptance AR-7) | (a) confirm all; (b) per item | (a) | 2b-1, 2a0-2, 2x-A0's U8p record, 2e-4, 2g-1 |
| OQ11 | A5-F4's boundary (PD8; the A5-F4 audit's Q1): Binding's `RecordMismatch` judges relations through local regions or inferred ends, which A3 §7 gives to A5 while deleting Binding's gate only at R6-check. When does Binding stop recording them? | (a) now: the relation becomes a row only, and on the default pipeline the old reporter judges it (it skips `CallBorrows` rows and maps Unrepresentable to `Ownership.Unsupported`, so a rejection may vanish; 2a0-3a's done condition blocks that); (b) at R6-check: Binding adds the row and keeps its record, re-labelled with A5's requirement `Ownership.UnprovenOrigin` (code and span unchanged), until H11; the ledger compares the record with A5's problem; (c) at R6-check without the row: A5 unverified on these chains in shadow | (b): A3 §7's and A5P §6.2's letter, no default-pipeline acceptance change, A5 verified in shadow; the temporary twin is listed for H11 | 2a0-D, 2a0-3a |
| OQ12 | S3 (A5P §5.2 and §12: "S3 and S4 go to the owner as clarifications"): request the SPEC 15.3.6 clarification "A variable's bounds are followed through other variables to fixed or finite Origins and `static`. If none is reached, the variable has no candidate" as a SPEC revision before 2a0-3b? | (a) request it (a short SPEC session; no verdict change, H); (b) no revision: 2a0-3b follows A3 §7's approved reading, and a variable bounded only by variables is a located `Unsupported_Kd` until S3 | (a) | 2a0-3b, 2a-1b |

**Packet 2: triggered.**

| ID | Question (trigger) | Options | Recommendation | Blocks |
| --- | --- | --- | --- | --- |
| OQ7 | DEC O3: a step-2 unit rejects a library or milestone program (census 2a0-0 for the containment skip and raw anchors; 2b-3; 2b-5's Transferable reads; 2d-2/2d-3; 2f-5's conformances; 2g-2/2g-3; 2x-E4a), or changes default-pipeline acceptance outside PLAN10 App. B (2a0-3a under OQ11 (a), 2a0-3b, 2c-1, 2x-E4a, 2x-E6, 2x-E7; each is a SPEC-conformance fix whose commit names its clause) | (a) SPEC's letter with a re-spelling (R4-M64's pattern); (b) a precision refinement through OQ8; (c) a hold | (a) unless the rejection rests on a listed loss | the unit's merge |
| OQ8 | OD13 and SPEC revisions: a prototype round reports a new loss (candidates: rule 1 as a Place phantom if carrier reach fails, any 2x-P4/2x-P7 loss), or a reading (2x-S1, PD15 (i)) needs a SPEC revision. Until the revision lands, PD30 applies | (a) approve the loss as a SPEC clause, integrated in one SPEC session per family before the unit that reports its Language code; (b) a representation instead; (c) a hold | (a) only for losses with zero library/milestone count, as one list per family | that family's rule unit's Language code |
| OQ9 | DEC O4 and stages, at X-2: the SPEC rules left as a located `Unsupported_Kd` at R4's exit and the removal stage of each remaining MIR `Unsupported_Kd` producer (candidates: per-case closure receivers, staticEnsure, G100's static Borrows and A5-F5's two SPEC examples until P42, G128 at R7, growing effect atoms per OQ13 (S5), the destruction-walk bound, a multi-signature Callable input (2f-2), a diverging update if 2d-0 decides one (2d-5u), a stored Φ_c if 2x-S1 leaves one, rule 3 if OQ3 (b), builder sites under A-6; every unit's registered producers, PD28) | (a) approve as one list; (b) per item | (a) | X-2, X-6 |
| OQ10 | D5 exception: only if re-triage (X-1) leaves a G106 signature mixed, add the old Unsupported gate's identity to the raw row (`OwnershipAnalysis.cs:2119` and ≈ 50 sites; an R4-M17 row-shape change), unit X-1a | (a) the exception; (b) classify the whole signature with per-row notes | (b) if every row's verdict is known, else (a) | X-1a, X-1 |
| OQ13 | S5 (A5P OD7; trigger: 2x-S5's proposal reviewed): an effect atom on an associated projection that grows under polymorphic recursion | (a) adopt S5 (it widens to an unclassifiable effect); (b) a depth limit whose excess is a Resource code; (c) undecided at R4's exit: growing atoms stay a located `Unsupported_Kd` (an O4 item in OQ9) | (a) | X-2 (OQ9's S5 item) |

**Where the audits' 53 questions went.** A5-F4: Q1→PD8/OQ11, Q2→PD9/OQ7, Q3→PD10, Q4→PD8/OQ12, Q5→PD5/OQ6, Q6→PD9/OQ7.
A5-F5: Q1→PD7 (decided by R4-M72), Q2–Q5→PD12, Q6→PD5/OQ2. A5-F7: Q1→OQ4, Q2→PD13, Q3→the G148 gate, Q4→PD6, Q5→PD13.
A5-F8: Q1→PD14/OQ6, Q2→PD5, Q3→PD14, Q4→PD14, Q5→PD14 (R4-M64). A5-F9: Q1→PD15, Q2→PD15, Q3→PD15, Q4→PD15, Q5→PD5,
Q6→PD15, Q7→PD11. A5-F6: Q1–Q2→OQ3, Q3→PD16/OQ8, Q4→PD16/PD23, Q5→PD7. Exit: Q1→OQ5, Q2–Q3→PD19, Q4→PD18/OQ10,
Q5→PD17/OQ6, Q6→PD20, Q7→PD17/PD24. Ledger: Q1→PD18/OQ5, Q2→PD18, Q3→PD20, Q4→PD14, Q5→PD23. Cross: Q1→PD6,
Q2→PD5, Q3→PD5, Q4→PD3, Q5→PD5, Q6→PD19, Q7→OQ3.

### 2.3 Contradictions between the audits

| Topic | Audits | Resolution | Why |
| --- | --- | --- | --- |
| Static-effect code | A5-F5 (`CallEffectConflict_Kd` everywhere) vs ledger and R4-M72 | `ComparisonLoanConflict_Kd` at the call (PD7) | R4-M72 is decided; DIAGNOSTICS and spec-cases agree |
| Owner of a5-old-effects (9 rows) | Ledger (A5-F5) vs A5-F5 and A5-F6 audits | A5-F6 (7) and (a) (2) | The sources are rule-1/rule-3 forms; the code is A5-F6's |
| Derived Loan problems in 2g-1 | Exit audit (keep τ_s-found problems with a cause) vs R4-M70 (option P) | 2g-1 narrowed to A1, A3, A4 (PD17) | Under P no Loan problem is found through an Opaque value |
| A5-F9 inline copies | A5-F9 (keep the marker as a drop) vs 1c review (j1) | R-a recommended; 2e-D decides (PD15) | A permanent drop over set-level taints can accept a program SPEC rejects |
| Origin boundary timing | A5-F4 audit (move now) vs A3 §7 (Binding's gate deleted at R6-check) | Owner question OQ11, recommendation (b) (PD8) | A departure from an approved text is the owner's |
| Merge order | A5P §6.2 (F4, F5, F7, F8, F9, F6) vs the audits' readiness | F5, F9, F4, F7, F6 on the chain (PD1) | A5P lets the audit change the order; readiness and dependencies |
| 1c numbering | Audits' 1c-3/1c-4/1c-6 | Adopted 1c-6 (A1 DAM, Inlined), 1c-7 (default taint, P), 1c-9 (records) | The design review renumbered |
| Restructurings | Six audits proposed overlapping splits of `MirBorrows*`, `MirReport` and `.Calls` | 1c-4 plus §3.7's schedule (PD21) | One bp unit or ruling per due file |
| Ask A-3 | CROSS (to do) vs exit audit (landed) | Landed (PD25) | `MirBorrowsTest.cs:1022-1071` |
| G147 (2) owner | A5-F8 and ledger (R5 W1) vs PLAN notes | BU's `bu4-index-uniq` for the selection, R5's W1 for the validator half (PD6, PD20) | The owner column was updated on 2026-10-18 |
| G147 (4)(5) | Ledger (open, R5 W1) | Landed with R5-F14/F15; 2x-T2 checks | R5 merged them |
| G147 (3) owner | PLAN (BU) vs A5-F7 (R4) | R4's 2c-1 with BU's agreement (PD6) | C2 publishes the same receiver predicate |
| G147 (1) unit | Proposed by the A5-F8, cross-cutting and ledger audits | One unit, 2d-3, decided with G148 (PD14) | Same cause |
| Census and milestone check | Proposed by the cross-cutting and exit audits | One unit, 2x-T1 (PD22) | Same check |
| Target-side format | Proposed by the cross-cutting and exit audits | One unit, 2x-T5 (PD19) | Same helper |
| G106 split | CROSS (gate identity now) vs exit and ledger (re-triage, restated kind) | Re-triage plus the restated kind (OQ5); the gate identity only by OQ10 (X-1a) | No frozen-path edit unless needed |
| G93 | A5-F5 (a G:F5 unit) vs CROSS (an erratum with two designs) | Inside 2b-D, after 2b-2's measurement (PD5, PD12) | A departure from A3 §4.2 needs the full process |
| A5-F4's prototype | A5-F4 (a round) vs CROSS (ask the owner) | Required, no question (PD3) | The owner's standing rule |

---

## 3. Structure and order

### 3.1 Lanes

| Lane | Content | Files | Can start (after the pause) |
| --- | --- | --- | --- |
| **C** Chain | The rule units on shared A5 files in PD1's order: 2b-3 (A5-F5); 2e-1–2e-3 (A5-F9); 2a-1a, 2a-1b, 2a-2, 2a-3 (A5-F4); 2c-3, 2c-6 (A5-F7); 2f-3–2f-5 (A5-F6); 2g-1, 2g-4b, 2g-5 (A6); the restructurings between them | `M/Analysis/MirBorrows*.cs`, `MirReport*.cs`, `MirProblem.cs`, `MirOrigins.cs` (new), the driver; chain units outside groups (restructurings, 2g-1) merge in chain order after the group before them | After 1c-9, G148 and 2x-R1/2x-R2 (PD2) |
| **O** Origins prelude | 2a0 | `B/Binding.Diagnostics.cs` → `Binding.OriginFits.cs`, `OriginProof`, `OriginDeclarations`, `LocalRegions`, new `TyCtxt.Origins.cs`, `Binding.OriginRows.cs`; `MirDifferenceLedger.cs` (2a0-4) | 2a0-0, 2x-P4 now; 2a0-1 after OQ1 (`bu1` is in the base) |
| **B** Binding views | C2, C5, Transferable, G147 (3), 2c-5 | `Binding.Closures.cs` → `Binding.ClosureReceivers.cs`, new `TyCtxt.Closures.cs`, `TyCtxt.Preserves.cs`, `TyCtxt.Transferable.cs`, the retention files, `Binding.ValueCalls.cs`, `Binding.FunctionTypes.cs`, `Binding.EffectObligations.cs` | 2c-0 after `bu2` merges |
| **U** Updates | A5-F8 | `MirBuilder.Expansions.cs` → `Updates.cs`, `Literals.cs` (W1); `StorageOperations.kimi`, `Dictionary.kimi`; goldens (W1); `MirBorrowsTest.Updates.cs` | 2d-0 after G148 and 1c-5; 2d-1 in W1 slot S-c |
| **T** Tooling and repairs | adoption, census, ledger, target sides, records, E9 lane, repairs | `T/` new census class, target-side helper, `classification.tsv`, `MirDifferenceLedger.cs`, VERIFICATION; builder files in W1 slot S-a | 2x-T1's class now; the rest after OQ1 |
| **D** Drafts | prototype rounds, the six design rounds, the S2 sketch, S5's proposal, readings | `A5RulesPrototype.cs` (rounds complete in PD3's order), `S2/`, `draft/Proposals` (S5 only) | Now |

`classification.tsv` and `VerifiedSpecExampleTest.cs` (`Landed`) are shared by every records unit: each merges dev before
editing them. A6 (2g) runs last on lane C and on its own files (`MirInit.cs`, `MirLints.cs`).

### 3.2 Critical path

```
GATES   G148: design review completed (corpus, ledger under G) ─► owner (losses; a SPEC sentence only with GXS) ─► G148 unit (W)
        1c: 1c-3 ─► 1c-4 ─► 1c-5 ─► 1c-6 ─► 1c-7 ─► 1c-8 ─► 1c-9            [design facts: R4-M68–R4-M70 adopted]
NOW     drafts: 2a0-0, 2x-P4, 2x-P10, 2x-S1, 2x-S5, 2f-S1, 2b-2 · 2x-T1 (class) · after OQ1: 2x-A0, 2x-T5, 2x-T7, 2a0-1,
        2x-R6, 2x-R10 ─► 2x-E5, 2x-E3 · BU: 2b-6, 2x-E4a, 2x-E6, 2x-E7 · after G148 decided: 2x-P0 ─► rounds (PD3)
C       [1c-9, G148] 2x-R1 ─► 2x-R2 ─► 2b-3 ─► 2e-1 ─► 2e-2 ─► 2e-3 ─► 2x-R5 ─► 2a-1a ─► 2a-1b ─► 2a-2 ─► (2a-3)
          ─► 2x-R3 ─► 2c-3 ─► 2c-6 ─► 2f-3 ─► 2f-4 ─► 2f-5 ─► 2g-1 ─► 2g-4b ─► 2g-5
O       2a0-0 ─► 2x-P4 ─► 2a0-D (FP, OQ2, OQ11) ─► 2a0-2 ─► 2a0-3a ─► 2a0-3b (OQ12) ─► 2a0-4 ─► [2a-1a]
F5      2b-2 ─► 2x-P5 ─► 2b-D (FP, OQ2) ─► 2b-1 ─► [2b-3 on C] ;  2b-4 ─► 2b-5 (G148, 2x-R8, W1 S-e) ─► 2b-8 ;  2x-R6 ─► 2b-7 (W2)
F9      2x-P9 ─► 2e-D (FP, OQ2) ─► [2e-1 on C] ─► 2e-4
F4      2a-4 ─► 2a-5 after 2a-2
B       2c-0 ─► 2c-1 ;  2x-P7 ─► 2c-D (FP, OQ4) ─► 2x-R4 ─► 2c-2 ─► [2c-3 on C] ─► 2c-4 (BU) ─► 2c-5 ─► [2c-6 on C] ─► 2c-7
U       2d-0 ─► 2x-P8 ─► 2d-1 (W1 S-c) ─► 2d-2, 2d-3 (W1 S-d) ─► 2d-4..2d-6 ─► 2d-7 ─► 2d-8 ─► 2d-10 ;  AB-3 ─► 2d-9
F6      2f-S1 ─► OQ3 ─► 2f-S2 (SPEC session) ─► 2f-7
        2x-P6 ─► 2f-D (FP, OQ2) ─► 2f-1 ─► 2f-2 ─► [2f-3..2f-5 on C] ─► 2f-6
A6      2x-P10 ─► 2g-D (FP, OQ2, AR-7) ─► 2g-2a (W1 S-b) ;  [2g-1 on C] ─► 2g-2 ─► 2g-3 ─► 2g-6 ;  2g-4a ─► [2g-4b on C]
        ─► [2g-5 on C] ─► 2g-7
X       [all families, 2f-7, 2x-E1/E2, BU G147 (2)] (X-1a) ─► X-1 ─► X-2 (OQ9) ─► X-3 ─► X-4 ─► X-5 (final) ─► X-6
```

- **The longest chain** is lane C after the step-1 gate: about 20 units in sequence (two restructurings, the A5-F5, A5-F9,
  A5-F4, A5-F7 and A5-F6 rule units, A6's three chain units), then A6's records and the exit (X-1–X-6). Its first unit
  needs 1c-9 and G148 merged; G148 still needs its design review completed (and a third design under the full process only
  if GXS is chosen over G), so the gate may be the largest single wait.
- **Preludes run beside it.** Each family's design, prototype and Binding views must be done before its first chain unit:
  A5-F5's and A5-F9's are short; A5-F4's (2a0, with OQ11 and OQ12) runs during A5-F5 and A5-F9's chain units; A5-F7's (2c-D,
  C2) and A5-F6's (2x-P6, 2f-D, 2f-1, 2f-2) run during A5-F4's. If a prelude is late, PD1 lets the next ready family move up.
- **Owner turnarounds on the path:** OQ2 per package, OQ11 and OQ12 before A5-F4's chain units, OQ3 before 2f-4, OQ4 before
  2c-5, OQ5 (iii) before 2x-T3. They are excluded from the calendar.

Rough calendar in working sessions (one session lands 2–4 small or 1 large unit, review latency included; H):

| Chain | Estimate |
| --- | --- |
| Step-1 remainder (G148's review completion, its owner question and unit, a third design only with GXS; 1c-3–1c-9): a gate, outside this plan | 4–7 |
| Lane C: the rule chain with its restructurings | 10–14 |
| Lane O: A5-F4's prelude (2a0) and tests/records (2a-4, 2a-5), beside lane C (owner turnaround on OQ2, OQ11, OQ12 excluded) | 5–7 |
| Lane B with A5-F7's view and bound result, beside lane C (OQ4 excluded) | 4–6 |
| Lane U: A5-F8, beside lane C (W1 slots) | 3–5 |
| A5-F6's prelude and tail (2x-P6, 2f-D, 2f-1, 2f-2, 2f-6, 2f-7; OQ3 excluded) | 4–6 |
| A6's off-chain units and records (2g-D, 2g-2a, 2g-2, 2g-3, 2g-4a, 2g-6, 2g-7) | 3–4 |
| Exit (X) | 2–3 |
| Lane T and drafts (≈ 26 units, ≈ 2,400 prototype lines), spread over the lanes | 7–10 |
| Effort, the gate included | ≈ 42–62 |
| Calendar: gate + lane C + A6's records + exit, with the other lanes beside them | ≈ 18–27 |

### 3.3 What can start now

Nothing starts before the owner lifts the 2026-10-18 pause. Then, before any owner answer: the drafts and probes 2a0-0,
2x-P4, 2x-P10, 2x-S1, 2x-S5 (A5P OD7 instructs S5's proposal), 2f-S1 (a sketch; the proposal file is untouched), 2x-P5 and
2x-P9 drafted on branches (they complete after 2x-P0), the measurement 2b-2, and 2x-T1's census class (it asserts R4-M12, an
adopted decision; its VERIFICATION selection waits for OQ1). After OQ1: 2x-A0, 2x-T5, 2x-T7, 2a0-1, 2x-E3, 2x-R6, 2x-R10
then 2x-E5; BU's 2b-6, 2x-E4a, 2x-E6, 2x-E7 on BU's schedule. After 1c-9: 2x-T6. After the G148 decision: 2x-P0 and the
rounds. After the gates: everything else in dependency order.

### 3.4 Gates from step 1, R5 and BU

- **Step 1:** G148 decided before 2x-P0, 2x-P7, 2x-P9, 2e-D and 2f-D; G148 merged before 2x-R1 (lane C), 2b-5, 2c-3, 2d-0,
  2d-3, 2d-6, 2e-1 and 2f-3; 1c's adopted design before 2x-P0, 2x-P9, 2x-P10, 2e-D and 2g-D; 1c-9 merged before lane C,
  2x-T2–2x-T4 and 2x-T6; 1c-5's codes before A5-F8's tests; R4-M68's A5-F6 condition into 2f-D.
- **R5:** R5-Q17 before 2x-T3, 2a0-4, 2g-1 and X-1a (`MirDifferenceLedger.cs`); R5-F10 before 2b-7 (W2); R5-F2b before
  2c-2 and 2f-1 touch `BindingModel.cs`; R5-Q21 (A-14) before 2g-2a; R5's restructure of `MirBuilder.cs`, `.Places` and
  `.Defaults` before Q21/Q16 edit them; W1 slots for 2x-E1, 2x-E2, 2d-1, 2d-2/2d-3, 2b-5 and 2g-2a (AR-1). **R4 gates R5:**
  2x-E1 and 2x-E2 (ask A-6) gate R5's G0 and X1 (R5P §3.4), so they take W1's first R4 slot.
- **BU:** `bu1` is in the base; `bu2` before 2c-0; 0c after 2c-3 in the same group merge; `bu4-index-uniq` (with its
  regressions) before 2d-9 and X-1; U5 (`MirBuilder.Values.cs`, `MirBuildTest.cs`) before 2x-E1 and 2g-2a.

Asks to R5 (new):

| ID | Ask | Needed by |
| --- | --- | --- |
| AR-1 | R4 slots in W1, in this order: S-a (2x-E1 then 2x-E2, first: they gate R5's G0 and X1); S-c (2d-1, the `MirBuilder.Expansions.cs` split, before Q16 edits that due file, or Q16's own bp prestep with the same parts); S-d (2d-2 and 2d-3's builder hunks and goldens); S-e (2b-5's `MirBuilder.Places.cs:175` hunk, after Q16's restructure of `.Places`); S-b (2g-2a, right after R5-Q21 at the same Opaque site). R5 restructures `MirBuilder.cs`, `.Places` and `.Defaults` before Q21/Q16 edit them (all due, §3.7) | 2x-E1, 2d-1, 2d-2, 2b-5, 2g-2a |
| AR-2 | An R5 reviewer for 2c-D and 2c-2 (R5's closure-erasure shims consume C2, A5P §7), 2d-2 (the setter receiver is acquired at locate), 2d-3 (`placeEntry`'s call shape), 2b-5 (`MirStatic`'s field replacing `RawHolding`) and 2x-E1 (the Aggregate contract) | those units |
| AR-3 | `MirDifferenceLedger.cs` order: R5-Q17, then 2x-T3, then 2a0-4, then 2g-1 (and X-1a if OQ10 (a)); Q17 reads 1c-8's `MirProblem.Body` | 2x-T3 |
| AR-4 | H0's inputs: X-3's handoff list (`S2/r6-check-handoff.md`) and the deletion-list additions of §7 | X-3 |
| AR-5 | R5-F10's `MirProgram.StaticInit`, which A4's copy then reads (2b-7) | 2b-7 |
| AR-6 | R5-Q21 (ask A-14) placed in W1 before 2g-2a, with an R4 reviewer | 2g-2a |
| AR-7 | R5's planning session reviews 2g-D and accepts H3's contract change (H3 reads `Binding.FailureCauses` at publication, PD17) before OQ6's answer is applied | 2g-D |
| AR-8 | G147 (2)'s validator half (a write or exclusive borrow past a shared Deref is a validator finding) and G136's `lent` validation (one Ref per argument temp) as W1 validator rules | X-1 |

Asks to the BU session (new):

| ID | Ask | Needed by |
| --- | --- | --- |
| AB-1 | Merge `bu2` before 2c-0; agree 2x-R4's split of `Binding.Retention.cs` before 0c and C2 | 2c-0, 2x-R4 |
| AB-2 | 0c (2c-4) starts from 2c-3's pushed branch and lands in G:A5-F7's dev merge, after it; R4's planning session merges the group | 2c-4 |
| AB-3 | G147 (2) (`bu4-index-uniq`) carries its regressions (`entries[1]@follow = 99`, `entries[1]@follow@uniq`, `change(entries[1])`, a user `UniqIndexable`), each failing before, and lands before X-1 | 2d-9, X-1 |
| AB-4 | G100's static-Borrow `Unsupported_Kd` (2b-6), the drop-`self` Take check (2x-E4a) and the destruction receiver's helper borrow (2x-E6) as BU units | 2b-6, 2x-E4a, 2x-E6 |
| AB-5 | A BU reviewer for 2a0-2, 2a0-3a, 2a0-3b, 2b-4, 2c-1, 2c-2, 2c-5, 2f-1, 2f-2 and 2x-R11; BU's retention changes run the census and a ledger run (cross-stream regression) | those units |
| AB-6 | G100's getter-result restriction on a Subject binding whose pair layer is Exclusive in the owner case (15.1.6-subject-modes, (b) on both paths) as a BU unit (2x-E7) | 2x-E7 |

R5's asks to R4, status: A-1 met by the common rule (every builder unit merges dev first); A-2 met by 1c-2; A-3 met (PD25);
A-4 by 2x-T5 and each family's records unit; A-5 by 1c-1, 1c-8 and PD31's fact rows; A-6 by 2x-E1/2x-E2 (first in W1, gating
R5's G0 and X1) and X-2; A-7 by 2f (OQ3); A-8's jump part handed to R5 (G143); A-9 landed (H); A-10 in every builder, MirTypes,
MirInit and MirDrops unit (sel, §4 common rules); A-12 (optional: A4's `Witnesses` read `TyCtxt.Comparison`) declined for
step 2, since no step-2 rule reads A4's `Witnesses`, so ARCH's legacy comparison composition stays until R7; A-13 recorded by
X-1 and X-3; A-14 by AR-6. R5's Q19 (ledger readings after A5-F6 and A6) is notified by 2g-7.

### 3.5 Hold schedule

| Window or line | Files | Holder | Step-2 units | Timing |
| --- | --- | --- | --- | --- |
| W1 (open; paused) | builder call/control/operator/drop files, `$abort`, `MirValidator*`, `tests/mir` goldens; for Q21/Q16 `MirBuilder.cs`, `.Places`, `.Formatting`, `.Defaults`, `.Match`, `.Expansions`, Q16's `.Values` site | R5 | S-a: 2x-E1, 2x-E2; S-c: 2d-1; S-d: 2d-2, 2d-3; S-e: 2b-5's `.Places` hunk; S-b: 2g-2a | AR-1 sent at adoption, so W1's order includes them; each slot unit has its own review (PD29) |
| BU U5 | `MirBuilder.Values.cs`, `MirBuildTest.cs` | BU | 2x-E1, 2g-2a merge after it | after U5 merges |
| W2 | `MirProgram.cs` | R5 (R5-F10) | 2b-7 after W2 closes | — |
| K lane | `TyCtxt.cs`, `Binding.ClosureInstances.cs`, `Compilation.Pipeline.cs` | R5 | none (new partials) | — |
| C lane | `BindingModel.cs` | R5 (F2b) | 2c-2, 2f-1 if they touch it, after F2b merges | — |
| adv | `MirDifferenceLedger.cs` | R5-Q17, then R4 | 2x-T3, 2a0-4, 2g-1, X-1a; 2x-T5 if it touches the file | one hold line per unit |
| adv | `TyCtxt.Retention.cs`, `TyCtxt.Effects.cs` | R4 | 2x-R4, 2c-2 (if touched), 2f-1 (if touched) | one hold line per unit; these units are outside groups |
| drv | `M/Analysis/MirAnalysis.cs` | R4 | 2b-3, 2x-R5, 2a-1a, 2f-5 | one hold line per unit; R4-only, serialized by PD1 |
| sh | `MirBody.cs`, `MirTypes.Holding.cs`, `MirDescriptors.cs`, `MirDump.cs` | R4 | 2x-R8, 2b-5, 2g-2a | one hold line per unit; merge dev after each W1 merge |
| BU | `Binding.Diagnostics.cs`, `Binding.ArgumentOperations.cs`, `Binding.Closures.cs`, `Binding.ControlFlow.cs` (`bu2`), `Binding.Retention.cs` (0c), `Binding.FunctionTypes.cs` | BU | 2a0-1, 2a0-3a, 2a0-3b, 2c-0, 2c-1, 2c-5, 2x-R4, 2x-R11, 2c-2 | merge first (AB-1) |
| D5 | the old Analysis | owner exception | 2c-5's gate (OQ4); X-1a (OQ10) | per answer |

### 3.6 Binding units by session

| Unit | Fact or rule | Implementer | Reviewer |
| --- | --- | --- | --- |
| 2a0-1, 2a0-2, 2a0-3a, 2a0-3b | C1-F4 view and the Origin boundary | R4 | BU |
| 2b-4 | Transferable view (G124) | R4 | BU |
| 2c-0, 2c-1, 2c-2, 2c-5, 2x-R11 | receiver predicate (G147 (3)), C2, the bound result, the FunctionTypes split | R4 | BU (2c-2 also R5) |
| 2f-1, 2f-2 | C5 preserves rows; abstract, descent and carrier facts | R4 | BU |
| 2x-E3 | G122 Literal row | R4 | BU |
| 2c-4 | 0c (G147 (8i)) | BU | R4 (G:A5-F7) |
| 2d-9's fix | G147 (2) (`bu4-index-uniq`, with regressions) | BU | R4 |
| 2b-6, 2x-E4a, 2x-E6, 2x-E7 | G100's static Borrows; drop-`self` Take check; destruction receiver's helper borrow; Subject getter-result restriction | BU | R4 |

### 3.7 Hotspot schedule

Counted at `522139c7` plus 1c's adopted units (git log per file; a restructure resets the count). A unit in the last column
has the response unit as a prerequisite; a ruling is recorded in that unit's commit.

| File | Changes since its last restructure | Next step-2 editor | Response |
| --- | --- | --- | --- |
| `MirBorrows.cs`, `.Flow.cs` | pending-records' count, 1c-3, 1c-7 | 2b-3 (chain start) | 2x-R1 |
| `MirBorrows.Reports.cs` (from 1c-4) | 1c-6, 1c-7, 1c-8 (due by 1c's adopted design) | 2b-3 | 2x-R2 (unconditional); `MirReport.cs` with it if due at its start |
| `MirBorrows.Calls.cs` (from `8213c83f`) | 1c-7, G148, 2e-1 | 2c-3 | 2x-R3 |
| `MirAnalysis.cs` (from `5ef517a6`) | 1b-5a, 1c-2, 2b-3 | 2a-1a | 2x-R5 |
| `MirBorrows.Checks.cs`, `.Candidates.cs` (from 1c-4) | 1c-5; then 2b-3's and 2e-2's hooks | the fourth by count | counted at each unit's start (PD21); a 2x-R row is added when due |
| `MirBorrows.Activations.cs` | E13b, G135, 1c-5 (due whatever 1c's one-argument answer) | 2f-4 | 2x-R9 (ruling expected: 124 lines, one responsibility, H) |
| `MirEffects.cs` | 0c `6d344cb0`, 0f, `c010aeba` | 2b-7 (2f-1 if it edits the file) | 2x-R6 |
| `MirInit.cs` | G94, G94 follow-up, 1c-1, 1c-6 | 2g-2 | 2x-R7 |
| `MirBody.cs` | E13a, E14, 0e remainder, 1c-6 | 2b-5, 2g-2a | 2x-R8 |
| `MirLoanSets.cs` | 1b-1, the 1b-2 fix, V16 | 2x-E5 (2f-D's operation if it adds one) | 2x-R10 |
| `TyCtxt.Retention.cs`, `Binding.Retention.cs` | 1a–1a4, BU-1, BU-3 | 2c-2, 0c | 2x-R4 |
| `MirBuilder.Expansions.cs` | G123, G94, G94 follow-up, G92a | Q16 (R5) or 2d-1 | 2d-1 in W1 slot S-c, before Q16's edit |
| `MirBuilder.cs`, `.Places`, `.Defaults` | G94f, G91, G92a, E13a; G123, G94, G92b, G94f, G91; four units (CROSS) | R5-Q21/Q16 | R5 restructures first (AR-1); R4's later hunks (2b-5, 2x-E2, 2g-2a) follow in slots |
| `MirBuilder.Values.cs` (4,316 tokens) | F8b, F12, then U5 | 2x-E1 (if after U5) | the slot holder splits it first if due at the slot's start |
| `MirBuilder.Match.cs` | three units (CROSS) | none: no step-2 unit edits it | — |
| `Binding.FunctionTypes.cs` (7,522 tokens, over the limit) | — | 2c-5 | 2c-5 net ≤ 0 tokens in it, else 2x-R11 first |
| `Binding.cs` (13,208 tokens, over the limit) | — | 2a0-2's reset | one per-pass `TyCtxt` reset shared by every view (2a0-2), net ≤ 0 per unit |
| `Binding.Diagnostics.cs`, `Binding.Closures.cs` (over the limit) | — | 2a0-3a, 2c-1 | 2a0-1, 2c-0 |

---

## 4. Units

**Common to every unit.**

- **Worktrees and merging.** Each unit runs in its own worktree, is committed after its checks with verify-commit evidence
  and pushed. Within a group a unit starts from its predecessor's pushed branch; on lane C a unit stacks on the previous
  chain unit's branch, also across groups (PD1); any other prerequisite in another group means merged into dev. A group
  merges into dev only after its review, in chain order (G:A5-F5, G:A5-F9, G:A5-F4, G:A5-F7, G:A5-F6, G:A6; G:A5-F8 when
  ready). A redone design or group invalidates the branches stacked on it; they restart from the redone branch. A BU unit in
  a joint merge (0c) starts from the R4 unit's branch, and R4's planning session merges the group.
- **Records with the change.** The unit that first implements a decision writes its DEC entry, its DIAGNOSTICS contract
  change and its ARCH canonical row in the same commit. Records units keep classification, PLAN, CODEMAP, STATUS, the spec-case
  target sides (A-4) and `Landed`, and land in their group's dev merge. The unit that changes R4's position (2x-A0, each
  family's records unit, 2g-7, X-6) updates PLAN §3 and ARCH's R4 row in the same commit. Hold lines are recorded per unit
  by the planning session before the unit starts and removed in its merge.
- **Rule units** test at the MIR-problem layer in the family's new partial test file, each rule with a valid and an
  invalid case, each new test failing before; coverage units (2d-4–2d-6) pass on arrival, and a failing row is a gap that
  stops for the full process. Rule units name +cen, +led, +V16 with a named shape and its Work terms, and +warm; a unit that
  adds per-head state states its memory term in `MirDataflow.Prepare`'s 64 MiB limit (R4-M15a) with a test at the limit. A
  new problem code publishes its fact rows with a test (PD31). PD30's interim rule applies to every rule the SPEC does not
  yet state.
- **Binding view units** are immutable after Bind, add their `PublishedBindingTypes` row, keep warm rebinds at 0 B
  (`Purpose=Allocation`), take +cold, use the one per-pass `TyCtxt` reset, stay net ≤ 0 tokens in over-limit files, start
  their new partial file with its contract, name +cen, and keep the default pipeline bp unless the unit states a behavior
  change.
- **Builder, `MirTypes`, `MirInit` and `MirDrops` units** merge dev before their first edit (A-1) and take sel.
- **Unsupported.** A valid form no rule covers is a located `Unsupported_Kd`, never a conservative Language error; a unit
  that adds one that remains registers it for X-2 (PD28). Every +cen unit has 2x-T1 as a prerequisite.

### 4.1 Cross-cutting (2x)

**Adoption, tooling, census and ledger**

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2x-A0 | Adoption records | DEC entry for the plan's adoption with packet 1's answers (OQ1–OQ6, OQ11, OQ12) and U8p's supersession (PD14 †); `draft/INTEGRATED.md` intake (一部取り込み, as R5P's); PLAN §3's pointer line and ARCH's R4 row in one commit; VERIFICATION's census selection (OQ1); PLAN's §7 rows (net ≤ 0 lines) | OQ1 | Doc. Done when DEC, INTEGRATED, PLAN §3 and ARCH name the plan and its answers and PLAN's line count does not rise | docs +30 | 1 | R (DEC, ARCH) | no |
| 2x-T1 | Census test (R4-M12 as a check, PD22) | A tracked class (name H: `T/MirCensusTest.cs`) asserting zero MIR problems of every analysis over the library, the 44 milestones, the 31 goldens and the accepting `spec-cases.json` variants, writing a census TSV; `MirAnalysisTest.TheLibraryHasNoProblems` moves into it; in the normal suite if its time stays under a recorded bound, else env-gated; replaces the scratch `ScratchA5CensusTest.cs` | — (VERIFICATION's selection: OQ1, in 2x-A0) | Comp + Tool: passes on dev; a seeded rejection in a scratch milestone copy fails it; time recorded. Done when each input set runs and the library assertion exists once | t+150/−6 | 2 | R | no |
| 2x-T2 | Ledger re-baseline at the step-2 base | mir Functional with `KIMI_MIR_DIFF` after 1c-9 and G148; the cause census rerun; each open blocking row mapped to one owner unit of §5.1; new and stale rows listed for 2x-T4 (R5-F13's 8 jump U signatures, the stale `a1-result` row, a5-old-loans' stale (6.4.5; 1) and (16.2.2; 1) counts, G147 (4)(5)'s rows) | 1c-9, G148 merged | Evid `artifacts/verify/mir-diff/step2-base/`. Done when every open blocking row has exactly one owner unit | 0 | 1 | — | no |
| 2x-T3 | Restated kind and `BodyOf` (PD18) | `MirDifferenceLedger.Differences`: an unpaired old row is `restated` only when a paired MIR record in the same body names the same Loan anchor or Move Path (the old row's related borrow site equals a fact site of the MIR record) or the same A1 subject Place; else `missing`; a body with no MIR record stays `missing`; `BodyOf` maps an old node outside its document tree to its document's body (a229d8f4dca0); DEC superseding R4-M47's clause. If old rows carry no related site, the kind is not added and the finding goes back to OQ5 | 2x-T2, AR-3, OQ5 (iii) | Tool + Comp: `MirDifferenceTest` (missing; restated with the same Loan; an unrelated paired record keeps missing; location; out-of-tree node); re-aggregation of 2x-T2's raw rows. Done when exactly the rows meeting the predicate move (≤ 74 on the old base, H) and no other signature changes | +30/−4, t+45 | 2 | R | adv |
| 2x-T4 | Classification of the MIR-right gates | `classification.tsv`: u-static (a, SPEC 22.4, 15.6; re-checked by 2b-8), u-temporary-part (a), u-accessor-storage (a), u-disjoint-reservation (a, 15.6.2, 15.6.7), u-check-only and u-never regexes extended, a5-old-loans split by form (Conversion (d) R4-M52; MemberAccess (a) 4.7.4) with its stale counts corrected, a (b) A6 row for restated rows; R5-F13's 8 jump signatures ((d) or derived, SPEC 23.1.2, 14.5.2, 14.7, checked on the fresh ledger); `a1-result` deleted; the old-path-wrong facts as (a) rows with clauses: round 12's x1 `inner11(v, peek(v@follow@ref))`, `func [u, v]` capture order (SPEC 7.7), `inner11(a, k)` (15.6.7), E13 review 2's u1/u2, rf1/rf2 and `q = Q2(...)`; the `LocalOriginClauseTest` `wrap` row re-triaged; reproducers per row (73204fa40e52, 7a5120932a3a, 0f9fecd9ef66, 644a243ec151, d983df6c85a2, d529e050ac5f, 2041079ddfb5, 44b60ad32855) | 2x-T3 | Comp: `MirDifferenceTest` (parse, matching); re-aggregation. Done when the rows 2x-T2 assigned are classified (186 open blocking left on the old base, H), no milestone row is open and the only unclassified blocking signature is U Invocation (A5-F6) | data +22/−2 rows | 2 | R | no |
| 2x-T5 | Target-side helper and OldRule marking (A-4, R5-Q20; PD19) | The helper over `MirDifferenceLedger.Sides` with per-row expectations; `OldRule = "ledger:<source hash>:<line>"`; applied to the six A5-F1/A5-F3 OldRule methods, `ContentUpdateTest.UpdatesTransferOnlyTheOldOrNewContentsLoans`, `ReplacementEndsOldDependenciesOnLoopBackEdges`, `BorrowedInputResultTest.APartBorrowBesideAStoredReferenceStaysPrecise`, the five Function Item tests and FiniteRegionComposition row 5; DEC refining R4-M50/R4-M60; a VERIFICATION procedure row | OQ1 | Comp: touched classes both; an `ArchitectureRulesTest` row checks every OldRule method calls the helper. Done when a mutated MIR verdict fails each target line and a code change of a row leaves its key unchanged | +40, t±160 | 2 | R | adv if it edits `MirDifferenceLedger.cs` |
| 2x-T6 | Step-2 records | PLAN: G110, G119 closed, G147 (6) done; VERIFICATION: A3 §6.3's V16 shapes (1)–(8) and E13 (5)'s N-operand shape, each with its existing test (each later shape is added by the unit that adds its test) | OQ1, 1c-9 | Doc. Done when every listed shape names an existing test and PLAN has no row for landed work | docs ±30 | 1 | — (form 3) | no |
| 2x-T7 | A5-F10 and A5-F2 landing (PD27) | MIR-layer rule tests for App. A rule 22 (SPEC 14.10.3): conflicts in the assumed state (the existing `LoansFlowOverJoinsLoopsAndCheckOnlyCode` row plus an invalid and a valid twin) and an abandoned comparison ending its Loans; F10 variants for 14.10.3's Loan parts where the example has them; STATUS's R1 row (checking of unreachable source) updated where MIR recovers it; G106's check-only row probed; `Landed += F10` only when an F10 variant exists | OQ1 | Comp: `MirBorrowsTest`, `VerifiedSpecExampleTest`. Done when rule 22's tests pass on MIR (a gap stops for the full process) and STATUS states the R1 boundary on MIR | t+50, json +20 | 2 | R | no |
| 2x-S1 | SPEC readings (PD23) | G147 (8ii), af43dcd8ffce, 47f0722c876b, 59e2289ce4f4 and PD16's stored Φ_c (SPEC 8.11.3's "Loans kept after transfer or Reborrow of that result"), each read with prototype cases (2x-P6 separates a call's own store rows from a caller's transfer of the result); outcome: a classification row with clause, a family test, or OQ8 | — | Doc + Draft. Done when each item has an outcome and an owner | doc ~80 | 2 | R | no |
| 2x-S5 | S5 proposal: effect atoms widen (A5P OD7, §12) | A SPEC proposal in `draft/Proposals`: an effect atom on an associated projection that grows under polymorphic recursion widens to an unclassifiable effect, as §15.6.4's open key widens static keys; the alternative (a depth limit with a Resource code); the census of growing atoms (step 0's `census.md`); principles; until decided, growing atoms are a located `Unsupported_Kd` (A5P §5.3) | — (OD7 is the instruction) | Doc. Done when the proposal passed its review and went to the owner (OQ13) | doc ~120 | 3 | R (proposal review) | no |

**Prototype rounds** (PD3: rounds on `A5RulesPrototype.cs` complete after 2x-P0 in the order 2x-P5, 2x-P9, 2x-P7, 2x-P6,
2x-P8, each merging the tracked file first; each new loss goes to OQ8 before its rule unit)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2x-P0 | Tracked draft catches up | G148's adopted round 14 and 1c's `ActivationRefOnly+PK` variant into the tracked prototype; E8's verdict table corrected where still stale (case 75's correction is already recorded) | G148 decided, 1c design adopted (R4-M68–R4-M70) | Draft (332 + round 14). Done when the tracked file reproduces both rounds | ~+150 draft | 1 | R | no |
| 2x-P4 | A5-F4 Origin stratum | `S2/A5OriginsPrototype.cs`: atoms by kind, rows with Equality and case masks, the E4 containment cut, meet splitting, Refuted BFS, Unknown by fixed bitsets and a premise oracle, the E12/E7 merge against the Loan verdicts (the destroy conflict only, PD11); cases audit-origins §3, 15.5 UniqView, P6's `current`/`leak`, 15.3.6's demo and a variable bounded only by variables (S3), a raw-anchor chain, a guard γ reaching `_0`, 36f without its write, a Refuted Borrow with an independent write conflict | 2a0-0 | Draft. Done when every case has its SPEC verdict | ~600 draft | 3 | R (in 2a0-D) | no |
| 2x-P5 | A5-F5 effects | Static roots and key classes; R1 keys, R2 the Call set E13 (1)(b) with W6 and the Drop set val(X), R3 a destruction's own exclusive Loan (S16:220), R4 staticEnsure per PD12; G124's A4 rule (a confined witness reading an immutable static of a base-object or runtime-Contract view, a common Function Type or abstract `T` errs; `Array<i32>` is valid); cases e1–e16 (e16: G136's lemma) and g1–g5 | 2x-P0 (completion) | Draft | ~+180 draft | 3 | R (in 2b-D) | no |
| 2x-P7 | A5-F7 closures | C2 rows, the receiver slot per case, Shared/Exclusive/Consuming, stores through W; cases f7a–f7n (622, 1379, 575, 576, 15.8.2, a valid `[t@ref]`, row 18, A3-closure, A9-keep-ok, nested `[x@uniq]`, a Shared call against an outer write, per-case receivers, var-capture snapshot, G148's exclusion on capture stores) | G148 decided, 2x-P0 | Draft | ~+250 draft | 3 | R (in 2c-D) | no |
| 2x-P8 | A5-F8 updates | Writeback run, not argued (TwoPhaseUniq at locate, activation at the write, P11); the setter receiver reservation; match/for Subjects; SPEC 15.7's operations and 15.7.3; SPEC 4.9.6's dependency cases; the Dictionary literal under each G147 (1) option; a diverging RHS or target (2d-0's outcome) | 2x-P0, 2d-0 | Draft | ~+250, ~30 cases | 3 | R | no |
| 2x-P9 | A5-F9 defaults | PreparedInput step, R26-borrow at Return and E, R26-access, P9 rows, R-a and the Inlined-drop option with a two-view generic caller and a j1-shaped holder; an inline default inspecting a reserved explicit argument (SPEC 7.2.3, 15.6.7); a raw-pointer input a default copies and writes through (G148's open issue 1, SPEC 5.2.1); ≈ 45 cases | 1c design adopted, G148 decided, 2x-P0 (completion) | Draft | ~+280 draft | 3 | R (in 2e-D) | no |
| 2x-P6 | A5-F6 preserves results | S2's rules as derived facts (no hand-given excuses): earlier calls, invalidation, Move carry, rule 1 by carrier reach and by Place phantom, conformance with an earlier result, delegation tags, desc(c) into stores; cases 33a–33e, 36a–36h, takeTwo, takeAcross, moved, S2's losses 1–5, the 5e1a449cc7bf family, Drain, Merge, the 12 + 3 conformance shapes, Choose/Branch/StoredPair, a static-rooted receiver, a shared-receiver Φ_c inside a reservation window (R4-M68), a stored Φ_c against a caller's transfer of the result (2x-S1), a Φ_c past a shared Deref (E3's residue) | 2x-P0, 2f-S1 | Draft | ~+480 draft | 4 | R (in 2f-D) | no |
| 2x-P10 | A6 derived rules and A2's drop-`self` Move (ER-derived) | `S2/A6DerivedPrototype.cs`, a small separate model: A1 at Opaque sites (an Opaque write completes the written Place's Move Path only; an Opaque read is no Move; a sibling part stays uninitialized), A3's derived `RequireFallthrough_Kd`, A2 and Binding rejecting a Field Move out of drop's `self` (SPEC 16.3.1); a probe over the corpus's Binding-rejected programs listing A1/A3 records before and after | 1c design adopted | Draft + Evid. Done when every case has its SPEC verdict and the probe lists each changed record | ~+200 draft | 3 | R (in 2g-D) | no |

**Restructurings** (each bp; done when the bp triple is equal, ledger raw rows are equal, each new file owns one
responsibility named in its contract, the next unit's change touches one of the files, and ARCH and CODEMAP list the files;
a ruling instead records why no responsibility split exists; §3.7 gives the counts. The family splits 2a0-1, 2c-0 and 2d-1
take the same done condition.)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2x-R1 | `MirBorrows.cs` and `MirBorrows.Flow.cs` | By the responsibilities present (inputs and constructor, view loop, transfer per statement kind) | 1c-9, G148 merged | Comp: `MirBorrowsTest`, `MirLoanSetsTest`, `MirAnalysisTest` both; bp | move +200/−190 | 1 | R | no |
| 2x-R2 | `MirBorrows.Reports.cs` (and `MirReport.cs` if due) | Codes and witnesses apart from the cause and merge rules; due by 1c's adopted design | 2x-R1 | as 2x-R1 plus `AWarmReportAllocatesNothing` | move +60/−55 | 1 | R | no |
| 2x-R3 | `MirBorrows.Calls.cs` | Contract rows (`Rows`, `Retaining`) apart from the transfer (`Call`), before the fourth change since `8213c83f` (1c-7, G148, 2e-1, then 2c-3) | 2a-2, or 2a-3 if it runs (chain) | as 2x-R1 | move +60/−55 | 1 | R | no |
| 2x-R4 | `TyCtxt.Retention.cs`, `Binding.Retention.cs` | Walk, stores, pair layers and limit, verbatim, before C2 and 0c | G148 merged, AB-1 | Comp: `RetentionTest`, `TyCtxtTest`, `FixedOriginRetentionTest`, `MirBorrowsTest` (mir); bp incl. `KIMI_SEMANTIC_HIR` | move +300/−290 | 1 | R (BU reviewer) | adv, BU |
| 2x-R5 | The driver | By responsibility before its fourth change since `5ef517a6` (1b-5a, 1c-2, 2b-3, then 2a-1a) | 2e-3 (chain) | as 2x-R1 | move +50/−45 | 1 | R | drv |
| 2x-R6 | `MirEffects.cs` | Split by responsibility (keys and spread apart from the solve's entry, H) or a ruling | OQ1 | Comp: `MirEffectsTest` both; bp | move +150/−140 | 1 | R | no |
| 2x-R7 | `MirInit.cs` | Split (A1's transfer apart from its recording rules, H) or a ruling; 1c's pending one-argument answer does not change that the file is due | 1c-9 | Comp: `MirInitTest` both; bp | move +120/−110 | 1 | R | no |
| 2x-R8 | `MirBody.cs` | Split (the body record apart from the builder-facing API, H) or a ruling | 1c-9 | Comp: `MirBuildTest`, `MirDumpTest`, goldens equal; bp | move +200/−190 | 1 | R | sh |
| 2x-R9 | `MirBorrows.Activations.cs` | Ruling expected (124 lines, one responsibility); a split if 2f-D adds the caller relation there | 1c-9, 2f-D | Comp: `MirBorrowsTest` both | 0 or move | 1 | R | no |
| 2x-R10 | `MirLoanSets.cs` | Split (interned sets apart from the stamps and clearing, H) or a ruling | OQ1 | Comp: `MirLoanSetsTest`; bp | move +100/−95 | 1 | R | no |
| 2x-R11 | (Conditional) `Binding.FunctionTypes.cs` | Closure-call result fixing into its own partial, only if 2c-5 cannot stay net ≤ 0 tokens in the file | 2c-2 | Comp: `ClosureResultOriginTest`, `ClosureEnvironmentOriginTest`; bp | move +200/−190 | 1 | R (BU reviewer) | BU |

**Repairs before the exit** (PD26)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2x-E1 | G103-E9a: generic fill | One fill Aggregate whose length is the instance's length operand (`MirBuilder.Values.cs:308-359`). The unit first records whether the existing Aggregate operand contract and validator admit the length operand; if a contract or validator rule must change, it takes FP with an R5 design reviewer (a shared representation), and a vocabulary entry is DEC O1 | AR-1 slot S-a (first), BU U5 | Comp: `MirBuildTest`, `MirValidatorTest`, `FixedArrayFillTest` under mir, goldens; sel; +cen. Done when the b-fill (e) rows are gone and the test passes under mir | +40/−15, t+40 | 3 | R (FP if the contract changes) | W1 |
| 2x-E2 | G103-E9b's non-jump parts with G95 | A node Binding or control flow rejected is built as its derived Opaque value with no record: raw reads of invalid C layouts, unresolved receivers, `let zero: T = 0` in a generic group, a stray `s is ref or uniq`, rejected index Types (`r: Range<isize>`) | 2x-E1 | Comp: a `MirBuildTest` or `MirValidatorTest` row per listed reproducer, each failing before; goldens; sel; +cen; +led. Done when ledger rows b-derived and b-finding have no row, and no MIR construction `Unsupported_Kd` or validator finding remains at a node Binding or control flow rejected | +40/−15, t+80 | 3 | R | W1 |
| 2x-E3 | G122: Literal row in `Binding.HasFacts` | A lexically invalid literal published Error; the builder trusts the row | OQ1 | Comp: `HirColumnsTest`, `MirBuildTest`. Done when no Literal without its constant reaches `MirBuilder.Rejected` unrecorded | +15, t+20 | 2 | R | BU |
| 2x-E4a | G100: drop `self`'s Field Moves, Binding (BU) | Binding's Take check treats the destruction receiver's Fields as reached through exclusive access (SPEC 16.3.1) | AB-4, 2x-P10 | Comp: a Binding test failing before, the ExclusivePathTake tests; snap; +cen; OQ7's trigger. Done when a Field Move out of drop's `self` reports Binding's code on both paths | +15/−5, t+20 | 2 | R (R4 reviewer) | BU |
| 2x-E4b | G100: A2's drop-body Move case | A2's drop-body Move case and `MirDropTest.ADestructorBodyDestroysItsLayersParts`' expectation go, the SPEC revision named in the commit | 2x-E4a | Comp: `MirDropTest`, the 16.3.1 Move variant both; sel; +cen. Done when the 16.3.1 Move variant is not (b) on either path and A2 has no drop-body Move case | +5/−20, t+10 | 2 | R | no |
| 2x-E5 | G136 V16: per-view clearing | `MirLoanSets.Clear` and `MirMoveData.Build` clear only what a view used, under R4-M53's tables | 2x-R10 | Comp: `MirLoanSetsTest`, scaling shapes; Bench: a many-view body, cold/warm, pinned interleaved; warm 0 B; +V16 "per-view clearing" with its VERIFICATION row. Done when a view's clearing work is proportional to its own entries | +30/−15, t+30 | 3 | D+R (R4-M53) | no |
| 2x-E6 | G100: the destruction receiver's helper borrow (BU) | Binding's `InvalidAssignment_Kd` for `let whole = self@ref` in a destruction body becomes acceptance per SPEC 16.3.1, or a located `Unsupported_Kd` where a rule is missing | AB-4 | Comp: Binding tests failing before; spec-case 16.3.1's base variant both; snap; +cen; OQ7's trigger. Done when 16.3.1's base variant is not (b) on either path | +10/−5, t+20 | 2 | R (R4 reviewer) | BU |
| 2x-E7 | G100: the Subject getter-result restriction (BU) | Binding records the getter-result restriction (SPEC 11.5) for a binding of a Subject whose pair layer is Exclusive in the owner case (SPEC 15.1.6) | AB-6 | Comp: a Binding test failing before; spec-case 15.1.6-subject-modes both; snap; +cen; OQ7's trigger. Done when 15.1.6-subject-modes' base variant is not (b) | +15, t+20 | 2 | R (R4 reviewer) | BU |

### 4.2 Binding boundary for A5-F4 (2a0)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2a0-0 | A5-F4 boundary census | Probes and `S2/f4-census.md` over the library, milestones, corpus and fixtures: (a) bodies where `PublishesIndependentResults` skips containment and which of the 12 library Iterator `next` bodies gain an edge; (b) inner-chain records via `LocalRegionSources`; (c) nodes with several Origin records of one code; (d) CallBorrows rows; (e) T1 occurrences; (f) relations with an `Anchor` end, and any default-pipeline acceptance SPEC 15.6.5 refutes; (g) the U rows that are the Unrepresentable gate (`OwnershipAnalysis.Borrows.cs:74-79`) | — | Evid + Doc. Done when (a)–(g) each have a count and an example, every library or milestone verdict change is listed (OQ7) and each refuted acceptance has a Binding repair unit or a PLAN row (PD9) | doc ~150 | 2 | — (form 3) | no |
| 2a0-D | A5-F4 design | Two designs and a design review of ER-rows (the C1-F4 tuple with Equality, Source, Clause, destination Type, CaseMask; alias rows; T1 exclusion), `OriginAtom.Of`, `UniversalRelations.For` over R1–R4 with one owner of fixed–fixed entailment (`ProvesOriginOutlives` reading the memoized closure, or `Entails` replacing it on fixed ends; if two forms remain, a DEC entry with removal stage R7 and a permanent `ArchitectureRulesTest` agreement row), 2a0-3a per OQ11 and 2a0-3b per OQ12, the stratum order in step 4, the merge contract (PD11), Reason in the key (PD10), atom identity between MIR Types and rows, A3 §7's cost terms and the memory term, shape (9) with mask classes, and the S1015 sections it implements (§15.3.5, §15.3.6, §15.6.1, §15.6.4) with their MIR tests | 2x-P4, 2a0-0, OQ11 | Doc. Done when the review leaves no blocking finding, the owner approved the package (OQ2) and DEC records the approval naming G:A5-F4's units | doc | 4 | FP | no |
| 2a0-1 | Split Binding's Origin fit code (bp) | The Origin part of `RecordMismatch` and its helpers (`Binding.Diagnostics.cs:920-1193`; the file is 12,601 tokens and may not grow) into `B/Binding.OriginFits.cs`; `ValidateOriginRelations` (`Binding.OriginProof.cs:394`) split into local (rows, or Binding's fixed–fixed record) and declaration (OJ1) parts | OQ1 | Comp: `LocalOriginClauseTest`, `LocalRegionInferenceTest`, `CallOriginRelationTest`, `OriginObligationVerdictTest`, `DeclaredOriginSlotTest`; bp. Done when snapshots are byte-equal and CODEMAP and ARCH list the file | move +280/−270, +15/−10 | 2 | R | BU |
| 2a0-2 | C1-F4 view | New `B/TyCtxt.Origins.cs` and `B/Binding.OriginRows.cs`: `TyCtxt.OriginObligations(definition)`, rows built on the first read and memoized for the pass (nothing runs under `-Pipeline legacy`), read from Binding's obligation list after its in-Bind compactions (`ConstraintValidation:417-431`, `ImplicitConstructors:216-250` and `Binding.cs:1005-1011` run inside Bind and stay, so the view removes nothing), including CallBorrows rows and alias edges (`OriginDeclarations.cs:742`), excluding Definition-deadline, Length and T1 rows; `OriginAtom.Of` (Fixed, FiniteBody, FiniteStatic, Inferred); `UniversalRelations.For` per 2a0-D's entailment owner; the one per-pass `TyCtxt` reset; DEC (ER-rows, the C1-F4 view) | 2a0-1, 2a0-D, OQ6 | Comp: new view tests (equal rows across a warm rebind; resolved ends; alias rows; T1 exclusion; CaseMask per pair binder; an atom per SPEC 15.6.5 bullet), `OriginPremiseCatalogTest`, `OriginPremiseLifecycleTest`; warm 0 B; +cold; default pipeline bp; +cen. Done when every OriginOutlives obligation and alias edge appears once in its definition's rows and `Binding.cs` does not grow | +280, t+180 | 3 | D+R (2a0-D) | no |
| 2a0-3a | Origin boundary (PD8, OQ11) | (b, recommended) Binding adds a row for each relation with a local-region or inferred end (`JudgeCallPosition`'s pattern, `Binding.CallRelations.cs:313-317`) and keeps its `RecordMismatch` record, re-labelled with A5's requirement `Ownership.UnprovenOrigin` (code and span unchanged), listed for H11; (a) Binding defers the relation to the row only. DEC (the boundary, superseding R4-M9b's view list where it applies); DIAGNOSTICS' Origin-relations paragraph | 2a0-2, OQ11 | Comp: `LocalRegionInferenceTest`, `LocalRegionCompositionTest`, `LocalOriginClauseTest`, `CallOriginRelationTest`, `EnumLocalOriginTest`, `OriginObligationVerdictTest`, `PairFollowTest:358-386`; a failing-before test of the row; snap; +cen; +led with the expected interim rows listed. Done when every inner chain has a row, Binding's twin is the only other judgment (b) or none (a), and no corpus, fixture or SPEC-variant program goes from rejected to accepted on the default pipeline unless the change is a recorded SPEC reading ((a) with clause); a vanished rejection is blocking | +60/−20 (b) or +90/−70 (a), t+80 | 4 | D+R (2a0-D); G:A5-F4 under OQ11 (a), so dev never holds it without 2a-1a | BU |
| 2a0-3b | SPEC 15.3.6 items (A3 §7's unowned items) | "No principal candidate" becomes SPEC 15.3.6's relation judgment (a row); an unresolved body inference is a local region, with S3's wording if OQ12 adopts it; outside a body Binding records `MissingOriginBinding_Kd`; under OQ11 (b) Binding's record stays as the twin | 2a0-3a, OQ12 | Comp: failing-before tests for each (c8c1f3f534b0 as (a) 15.3.6; a no-principal-candidate twin; a variable bounded only by variables), `LocalRegionInferenceTest`; snap; +cen; +led. Done when the reproducers give SPEC 15.3.6's verdict and no default-pipeline rejection vanishes unrecorded | +40/−30, t+50 | 3 | D+R (2a0-D); G:A5-F4 under OQ11 (a) | BU |
| 2a0-4 | Ledger split of `Binding.OriginRelation` | `MirDifferenceLedger.cs:69-70`, by owner (R4-M4, A3 §7): under OQ11 (b) Binding's twin records join A5's compared set (old side) and its fixed–fixed records stay Binding's; under (a) Binding's Origin records leave A5's set; a5-old-origins-binding removed or re-pointed; R4-M4/R4-M17 note | 2a0-3b, 2x-T3 | Tool: `MirDifferenceTest`; +led. Done when every `Binding.OriginRelation` row has one owner | +3/−4, tsv ±3 | 1 | R | adv |

### 4.3 A5-F4 Origin relations (2a)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2a-1a | `MirOrigins`: frame, rows, containment, Refuted | New `M/Analysis/MirOrigins.cs` with its contract: per body (inline copies are their template's, R4-M69) and per distinct case-mask class; nodes are row atoms and Ref destinations; edges from rows (Equality both ways; a meet split at the longer end, an inferred node at the shorter); containment per borrow Deref layer up to and including the first shared layer (E4; pair layers by `MirCases.Layer`); Refuted by a multi-source BFS from finite nodes with witnesses (`UnsatisfiedOriginRelation_Kd` at the last edge's site, with its relation facts, PD31); step 4 runs it before `MirBorrows` per body; no preserves skip, no top-like Anchor (PD9); spec-case 15.5/0 relabelled F4; DEC (MirOrigins as a canonical component, the stratum order) and ARCH's canonical row; DIAGNOSTICS' Origin owners | 2a0-2, 2a0-3b, 2a0-4, 2x-R5 (chain) | Comp: new `T/MirOriginsTest.cs` (audit-origins §3 rows 7, 9c, 19 with twins; SPEC 15.3.4, 15.6.1, 15.6.5 and 15.5 variants; atom identity per Ref form), `VerifiedSpecExampleTest`, `MirAnalysisTest`; MirOrigins' warm-run 0 B test; +V16 "F4 Refuted" (N_o nodes, E_o edges; BFS work ≤ c·(N_o + E_o)); +cen; +led. Done when the Unsatisfied rows 2x-T2 assigned pair or are (a)/(d) | +200, t+250 | 4 | G:A5-F4 | drv |
| 2a-1b | `MirOrigins`: Unknown | Unknown by Tarjan and fixed-source bitsets (`UnprovenOriginRelation_Kd`, requirement `Ownership.UnprovenOrigin`) with relation facts; Anchor and Use facts; the Work counter over A3 §7's terms Θ(N_o + E_o·⌈F/64⌉·ρ + Ft·Pm); its bitsets' memory term in `MirDataflow.Prepare`'s limit with a test at the limit (`OwnershipStorageLimit_Kd`) | 2a-1a, OQ12 | Comp: `MirOriginsTest` (rows 13, 20; SPEC 15.3.6; a 64-view pair body; a mask class shared by views); +V16 A3 §6.3 (9) with k mask classes; +warm; +cen; +led. Done when the Unproven rows 2x-T2 assigned pair or are (a)/(d) | +140, t+200 | 4 | G:A5-F4 | no |
| 2a-2 | E12/E7 merge (A6 part, PD11) | `MirReport` keeps per body the Borrows the Origin stratum refuted for an escape into `_0` and the guard candidates whose `γ_H` reaches `_0`; `Add` drops only the `destroy` (storage-end) conflict on a Loan `_0` carries whose Borrow is one of them, and E7's `err(ended)` for such a candidate; never by Loan membership; every other conflict of the same Loan stays; DEC (the merge contract) | 2a-1b, 2x-R2 | Comp: `bad`'s `return local@ref` (one Unsatisfied, no Comparison), p11 e3, p12 guardReturn, 36f without its write keeps its Comparison, `let r = x@ref; x = 1; return r` reports the write conflict and the relation, another Borrow's conflict stays, nothing dropped without a Refuted twin; +led. Done when the a5-new storage-end rows 2x-T2 assigned go (≈ 13, H) and their Unsatisfied rows pair | +35, t+80 | 3 | G:A5-F4 | no |
| 2a-3 | (Conditional) Reason in the key (PD10) | Only if 2a0-0 (c) finds multi-chain nodes | 2a-2 | Comp: a meet's two failing operands give two problems; a repeated chain merges; +led. Done when no missing row is a second chain at a paired node | +40/−5, t+40 | 3 | G:A5-F4 | no |
| 2a-4 | A5-F4 tests and target sides | The 13 test files reading `Binding.Obligations`, `JudgeOriginObligation` or `LocalRegionSources` (grep at the base, H) gain MIR twins or OldRule markers (2x-T5); `PairFollowTest`'s detach rows expected on MIR; target sides; `Landed += F4, F2` | 2a-1b, 2x-T5 | Comp: migrated classes both, `VerifiedSpecExampleTest`. Done when no A5-F4 rule is asserted only through old internals and the ratchets do not rise | t±200 | 2 | G:A5-F4 | no |
| 2a-5 | A5-F4 records and ledger close | ARCH's R4 row and PLAN §3 (A5-F4 landed); CODEMAP; PLAN (G56's "a Binding-level relation stops ownership analysis"); STATUS; `classification.tsv`; the R6-check deletion list (AR-4) | 2a-1a–2a-4 | Doc + Evid: +led. Done when the A5-F4 blocking rows 2x-T2 assigned are gone (85 on the old base, H) and 9b64b783b09b, cd1535825466, 53d750a49d82, e3312717bde0, bc801917a239, 6b46c86d05a2, c8c1f3f534b0, 1720228ea1c3 report their codes on MIR | docs ~40, tsv ±6 | 1 | G:A5-F4 | no |

### 4.4 A5-F5 effects and static anchors (2b)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2b-D | A5-F5 design | Two designs and a design review of ER-effects (err(effect) per key, Unknown, Limit, Derived; R4-M72's code; the Call set with W6; the Drop set val(X); a destruction's own exclusive Loan on a static; staticEnsure per PD12; G136's lemma; growing atoms under PD30 until OQ13), A4's Keys classes (G93, †) with any representation 2b-2's measurement calls for (a listed unit before 2b-3), the Transferable contract (SPEC 15.2.4), and the S1015 sections it implements (§8.11.2, §15.2.4, §15.6.4, §16.3.3) with their MIR tests | 2x-P5, 2b-2 | Doc. Done when the review leaves no blocking finding, the owner approved the package (OQ2) and DEC records the approval naming G:A5-F5's units | doc | 3 | FP | no |
| 2b-1 | A4 `Keys` returns its classes (G93 a) | `MirEffects.Keys(..., out byte classes)` in `MirEffects.Keys.cs`: Unknown, Limit and Derived stay apart; DEC (the classes, superseding R4-M38's `bool` †) | 2b-D, OQ6, 1c-9 | Comp: `MirEffectsTest` (a site per class, a plain key). Done when the classes are distinguished and the other Keys tests pass unchanged | +6/−3, t+30 | 1 | G:A5-F5 | no |
| 2b-2 | G93 key-storage measurement | F mutable statics × N bodies (a chain of 1,024, as R4-M38's probe): cold A4 allocation and time with and without statics | — | Bench/Evid `artifacts/benchmarks/`. Done when the 2×/4× growth is recorded and any representation it calls for is handed to 2b-D | t+40 | 2 | — | no |
| 2b-3 | A5-F5 effect rule | New `M/Analysis/MirBorrows.Effects.cs`: ER-effects' rules; a pre-pass skips views that borrow no mutable static (V16); Limit → located `Unsupported_Kd`; Derived → no problem (R4-M70); growing atoms → located `Unsupported_Kd` (PD30, OQ13); `ComparisonLoanConflict_Kd` at the call with the static key and call as facts (PD31); an activation and an effect conflict on one Loan stay two independent failures; `MirBorrows` receives `MirEffects`; DEC (the rule, W6, the Drop set, R3, staticEnsure, the lemma); DIAGNOSTICS and ARCH rows | 2b-D, 2b-1, 2x-R2 (chain) | Comp: new `T/MirBorrowsTest.Effects.cs` on implicit static Loans (e1–e15), `MirAnalysisTest`; +V16 "F5 effects" (N calls × M static Loans; K keys); +warm with a static; +cen (Milestones 42, 43); +led. Done when the verdicts equal the round's | +120/−5, t+220 | 3 | G:A5-F5 | drv |
| 2b-4 | Transferable view (G124 a) | New `B/TyCtxt.Transferable.cs`: Proven/Refuted/Unknown per SPEC 15.2.4 (rc, Weak of rc, raw pointers outside the exempt collections and iterators, common Function Types, runtime-Contract and base-object views refute; abstract → Unknown); DEC (the Transferable view) | 2b-D | Comp: a view test (each refuting and exempt Type, a recursive Type, an abstract T), `KimiLibraryShapeTest`, `ArchitectureRulesTest`; warm 0 B; +cold; +cen. Done when every listed Type answers per SPEC | +80, t+80 | 3 | D+R (2b-D) | no |
| 2b-5 | A4 reads Transferable (G124 b) | The builder stores the verdict in `MirStatic` in place of `RawHolding` (`MirBuilder.Places.cs:175`, `MirBody.cs:216`); `MirDescriptors.cs:379` marks an environment read unless Proven; MirDump prints the verdict in place of ` raw` (no golden prints a static's ` raw` today, checked; a golden that would change goes into slot S-e); `HoldsRaw`'s readers are recounted on G148's head, which shares the Holding walk: `Raw` is deleted only if none remains | 2b-4, G148 merged, 2x-R8, AR-1 slot S-e, AR-2 | Comp: `MirEffectsTest` (a confined witness reading an immutable static of obj/Base, `() -> ()` or abstract T errs; an `Array<i32>` static is valid; each failing before), `MirBuildTest`, goldens; sel; +cen; OQ7's trigger. Done when `MirStatic` carries the verdict and `RawHolding` has no reader | +10/−45, t+50 | 2 | D+R (2b-D), outside G:A5-F5 (PD29) | sh, W1 |
| 2b-6 | G100 static part (BU) | Binding's `InvalidAssignment_Kd` for a Borrow of group storage becomes the located `Unsupported_Kd` (PD12) | AB-4 | Comp: the Binding tests; spec-cases' two A5-F5 Old records change; snap; STATUS row. Done when no static Borrow gives `InvalidAssignment_Kd` | +10/−5, t+20 | 2 | R | BU |
| 2b-7 | A4 reads `MirProgram.StaticInit` | Replace A4's copy (`MirEffects.cs:173-174`) | AR-5, 2x-R6 | Comp: `MirEffectsTest`. Done when a grep finds one helper | +1/−2 | 1 | R | no |
| 2b-8 | A5-F5 records and re-triage | `Landed += F5`: the static-Borrow variants of 15.6.4-static-anchors and 16.3.3-erased-destruction carry a MIR difference record ((e), the clause, a note naming P42 and R5-M13 (b)) expecting 2b-6's located `Unsupported_Kd`; `classification.tsv` (a5-old-effects split per PD7; u-static and u-confined-static re-checked: each source's validity argument named, MIR still accepts it after 2b-3, a rejection reclassifies); PLAN (G93, G124, G136's static items, W6); CODEMAP; STATUS statics row; ARCH's R4 row and PLAN §3 | 2b-3, 2b-5 merged, 2b-6 merged | Doc + Comp: `VerifiedSpecExampleTest`; +led. Done when `Landed` lists F5, no signature cites "F5 (unit 2b)", every u-static source has its argument and Milestone 42 (73204fa40e52) has no A5 problem | doc +40, json ±10 | 1 | G:A5-F5 | no |

### 4.5 C2 and A5-F7 closures and escape (2c)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2c-0 | Split closure receiver classification (bp) | `ClosureEffects` (`Binding.Closures.cs:680-843`; 5,911 tokens) into `B/Binding.ClosureReceivers.cs` with its contract (SPEC 7.7.3) | AB-1, OQ1 | Comp: `ClosureReceiverClassificationTest`, `CaptureDiagnosticTest`, `ArchitectureRulesTest`; bp. Done when the file is under 5,000 tokens and CODEMAP and ARCH list the new file | move +170/−165 | 1 | R | BU |
| 2c-1 | G147 (3): one exclusive-use predicate | One predicate over the Place reached from a capture replaces the outer arms and `UsesReferentExclusively` (`:767-771`, `:802-841`, one cause); a direct `n@uniq` of a capture, or `n` as a uniq argument or receiver, makes the call Exclusive. No per-case output (2c-2's, under 2c-D) | 2c-0 | Comp: classification rows; reproducer 24028725b625 errs at `let q = n` (fails before); a dump row; snap ((a) SPEC 7.7.3); +cen; OQ7's trigger. Done when 13972.1438 pairs | +10/−15, t+40 | 2 | R (one rule replaces two copies) | BU |
| 2c-D | C2/A5-F7 design | Two designs and a design review: `ClosureContract` (captures, receiver per admitted case, environment Type, a CallRetention over receiver slot and parameters), the per-case receiver computation (PD13), the environment walk in `Binding.Retention`, the gate (OQ4), ER-closure-stores (W with G148's exclusion), closureResult edges against 2a0-D, `ValueCalls.cs:541`'s owner, the S1015 sections it implements; an R5 reviewer for C2's second consumer (AR-2) | 2x-P7, 2a0-D, OQ4 | Doc. Done when the review leaves no blocking finding, the owner approved the package (OQ2) and DEC records the approval naming G:A5-F7's units | doc | 4 | FP | no |
| 2c-2 | C2 view `TyCtxt.Closure(k)` | New `B/TyCtxt.Closures.cs`: the store output into the receiver is the environment's writable storage (Exclusive: Env and captured exclusive referents; Consuming: captured referents; Shared: none); the receiver per admitted case; an inferred result's EnvironmentSlot atoms bounded by the receiver's outer Origin; memoized per (closure, context, scope); reads only `ClosureSignature` and `SubstOfClosure`; DEC (the C2 contract) | 2c-D, 2c-1, 2x-R4 | Comp: `RetentionTest` closure rows (622 `0 Value store recv`; 575 reaching the captured referent; `recv Value result`; `recv Content result`; Consuming; per-case receivers), immutability, warm 0 B; +cold; +cen; bp both (no consumer yet). Done when the view gives the design table's rows for f7a–f7n | +170, t+120 | 4 | D+R (2c-D; BU and R5 reviewers), outside G:A5-F7 (PD29) | adv, BU |
| 2c-3 | A5-F7 rule | `MirBorrows.Calls`: a Closure-typed Value callee takes C2's rows, the callee operand in the receiver position; stores into the receiver through W (layer 1 the Closure holder for a reference receiver; deeper layers the captured referents; G148's exclusion); a Consuming call stores into what its moved value names; the temporary rule (`Calls.cs:16-34`) deleted; Function-typed and Callable-bound values keep C8's retention; G136's Value-callee arity checked against the token's inputs; DEC (A5-F7's rule, the deletion) | 2c-2, G148 merged, 2x-R3 (chain) | Comp: `MirBorrowsTest.Closures` (4037bfa952e2, 40f3cbe34df1, a46e125d3817, 358748d9dfcc error; 130a–130g, f7f, f7g stay; an arity row); +V16 "F7 captures" (N captures × M calls); +warm with a closure; +cen (Milestone34); +led. Done when the 4 rows pair and no temporary rule remains | +45/−15, t+150 | 3 | G:A5-F7 | no |
| 2c-4 | 0c: no self-store row for owner-or-uniq pair carriers (BU) | `Binding.Retention.cs` gives a by-value owner-or-uniq pair carrier no `i Value -> i` row; from 2c-3's branch, in G:A5-F7's merge (AB-2) | 2c-3 | Comp: `RetentionTest`; pair-cases on mir accept A2-put-ok, A6-keep-ok, A9-keep-ok, B4-chained, B5-result-put, B6-for, B6-remove, B7-second in both views, A3-closure stays rejected at `v@follow = x`; +cen; +led. Done when the 16 (8i) rows are gone | +10/−20, t±20 | 2 | BU's R + G:A5-F7 | BU |
| 2c-5 | Receiver-dependent closure results bound | `Binding.ValueCalls.cs:531-543` and `ResultFixedForCall`'s `dependent` (`Binding.FunctionTypes.cs:294`): environment Origins replaced by the receiver's Origin at the call (SPEC 15.8.2; Consuming refuted as today); the old-Analysis gate per OQ4; DEC (the D5 gate); STATUS's closure-result row | 2c-3, OQ4, 2x-R11 if needed | Comp: default pipeline `ClosureResultOriginTest`, `ClosureEnvironmentOriginTest` keep code and span; mir: the 7 forms by SPEC; 15.8.2's `let moved = get@move` gives `ComparisonLoanConflict_Kd`; snap; +cen; +led. Done when no Binding `Unsupported_Kd` remains at these calls and `Binding.FunctionTypes.cs` does not grow (token counts in the commit) | +50/−20, gate +15, t+60 | 4 | G:A5-F7 | BU, D5 |
| 2c-6 | Closure-result edges in `MirOrigins` | Each returned source of a Closure with an inferred result gets an edge to its fixed result Origin; Binding's `CallLocalOrigin` stays until H11 (PD13) | 2a-1b, 2c-2, 2c-3 (chain) | Comp: `ClosureResultOriginTest`'s refutation theory on mir; both 15.8.2 variants; +cen; +led. Done when no Origin row on Closures is unclassified | +30, t+40 | 3 | G:A5-F7 | no |
| 2c-7 | A5-F7 records and ledger close | ER-closure-stores' record; CODEMAP; PLAN (G147 (3), (8i); G75's residuals where verified); STATUS 50–51 where verified; `classification.tsv`; pair-case target sides (A3-closure, A8-capture, A9-*, B1-*); the two closure `Unsupported` families outside A5-F7 (`MirBuilder.Values.cs:412-415`, `Binding.Closures.cs:463-467`) registered for X-2; `Landed += F7`; ARCH's R4 row and PLAN §3 | 2c-3–2c-6 | Doc + Evid: +led. Done when the 5 blocking rows 2x-T2 assigned are gone and no closure signature is unclassified | doc | 1 | G:A5-F7 | no |

### 4.6 A5-F8 updates, Subjects, guards and collections (2d)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2d-0 | Entry probe | `SpecVariants` widened to F8 in scratch, plus ≈ 40 probes (15.1.6 follows-bindings pairs; `for` Subjects; S14:344-351; S13:600-603, `x += stop()`, `a[stop()] += 1`; 15.7.1–15.7.3; S4:570-575; tryGet, insertOrReplace, tryInsert's Err, truncate and removeAll with an observing T; G147 (1)/(2)); decides the diverging-update outcome (no rule after G92a, or a located `Unsupported_Kd` as its own unit 2d-5u) | G148 merged, 1c-5 | Evid `artifacts/verify/a5-2d-probe/`. Done when each form has MIR against SPEC verdict, each gap a cause class, and the diverging-update outcome is recorded; a rule gap stops for the full process | 0 | 2 | — | no |
| 2d-1 | Split `MirBuilder.Expansions.cs` (bp) | Update/Writeback (`:127-256`) into `MirBuilder.Updates.cs`, Collection and Dictionary literals (`:390-458`) into `MirBuilder.Literals.cs` (names H); before Q16 edits the due file | OQ1, AR-1 slot S-c | Comp: `MirBuildTest`, `MirDumpTest`, goldens equal; bp. Done when each new file owns its contracted responsibility, CODEMAP and ARCH list them, and 2d-2/2d-3 each touch one file | move +210/−200 | 1 | R | W1 S-c |
| 2d-2 | Writeback through setters (SPEC 13.7.2) | With a setter (`plan.Second`), one TwoPhaseUniq reservation of the receiver where it is located; the getter reads through it; the setter takes it as its receiver input, so its Call activates it; DEC amends R4-M37's Open | 2d-1, 2x-P8, G:A5-F5 merged, AR-1 slot S-d | Comp: goldens (`holder.item += delta`; increment on a computed Property), `MirBorrowsTest.Updates` (S13:602 valid; a getter result live across set errs; an effect conflict), `MirValidatorTest`; sel; +cen. Done when goldens change only for setter updates | +40/−10, t+70 | 3 | D+R (R4-M37) + R5 reviewer | W1 S-d |
| 2d-3 | G147 (1): Dictionary literal entries | `placeEntry` takes `target: uniq/Dictionary<K, V>`, so E14's declaration retention stores K and V; the builder passes the borrow it forms for `findEntry`; `StorageOperations.kimi`, `Dictionary.kimi:45,56` (PD14) | G148 merged, 2d-1, 2x-P8, AR-1 slot S-d | Comp: `MirBorrowsTest.Updates` (`[1: value@ref]` then `value = 99` errs; a valid twin), `DependentDictionaryLiteralTest`, `DependentPointerEmissionTest`, `DictionaryLiteralEmissionTest`, `KimiLibraryShapeTest`, a golden; native fixtures `-Fixtures 'Dictionary*.ll'` and an O2 comparison of a Dictionary literal; sel; +cen. Done when the 10 rows pair with no intrinsic row added and the O2 code of the literal does not regress | +15/−5, lib +4, t+40 | 3 | R + R5 reviewer | W1 S-d |
| 2d-4 | Coverage: match and for Subjects, guards | 15.1.6 (follows bindings; replacing the Place in an arm without a live borrowed binding; a borrow through a borrowed Subject; an owned Subject's payload borrow cannot escape); `for` Subjects; 14.8.3 Subject protection incl. Wildcards and referents through references; a transfer out of a guard (16.2.1); guard false keeps changes | 2d-0, 2x-P8 | Comp: tests pass on arrival. Done when every row has SPEC's verdict and code, none duplicating 1b-4's γ tests | t+120 | 2 | G:A5-F8 | no |
| 2d-5 | Coverage: compound, increment, whole-value updates | S13:600-603; P11; a diverging RHS or target per 2d-0's outcome; 15.7.1's variants; swaps (31a, 31b; fixed-array distinct ConstIndex valid; Array via indexUniq errs); exchange keeps only its own dependencies; 15.7.3's slot example; a5-replace-loop as (d) | 2d-2 | Comp: tests pass on arrival; `ContentUpdateTest` unchanged on the old path. Done when every row has SPEC's verdict | t+120 | 2 | G:A5-F8 | no |
| 2d-5u | (Conditional) A diverging update's `Unsupported_Kd` | Only if 2d-0 decides one: a located `Unsupported_Kd` at the update, registered for X-2 | 2d-0, 2d-2 | Comp: a test per form; +cen. Done when the form reports it and STATUS lists it | +10, t+20 | 2 | G:A5-F8 | no |
| 2d-6 | Coverage: collections and destroying calls | S4:570-575; append then clear subtracts nothing; tryGet's storage Loan; tryInsert's Err joins; a removed uniq/T keeps its dependency; truncate, removeAll, Dictionary clear and indexed replacement with an observing T; insertOrReplace's unused input; G127's shape as a listed loss; af43dcd8ffce per 2x-S1; the destroying-row subsumption recorded | 2d-3 | Comp: tests pass on arrival; `DictionaryLibraryTest`, `DynamicArrayCostTest` unchanged. Done when rows without a Destroys catalog row are shown subsumed or reported as a gap | t+100 | 2 | G:A5-F8 | no |
| 2d-7 | SPEC examples and scaling | `SpecVariants` adds F8; missing A5-F8 blocks in `spec-cases.json` with codes and Old sides; scaling (N compound updates under a live Loan, N guarded matches, N disjoint swaps) as V16 "F8 updates" with VERIFICATION rows | 2d-4–2d-6, 2x-T5 | Comp: spec theory, scaling, `VerifiedSpecExampleTest`, DiagnosticCorpus. Done when every variant gets exactly its codes and work grows linearly | t+40, json +60 | 2 | G:A5-F8 | no |
| 2d-8 | A5-F8 test migration | ≈ 55 `.Ownership.Bodies` reads in ≈ 21 files (H) to MIR observations or H8a tags; OldRule target sides for `ContentUpdateTest` and `DictionaryStoredReferenceTest` | 2d-7 | Comp; the ratchet lowered. Done when no A5-F8 test reads old internals without an H8a tag | t±400 | 2 | G:A5-F8 | no |
| 2d-9 | G147 (2) adoption | After `bu4-index-uniq` with its regressions (AB-3): the 9 rows reclassified | AB-3 | Comp; +led. Done when the 9 rows pair or are classified by SPEC 4.7.1 | tsv ±9 | 1 | R | no |
| 2d-10 | Land A5-F8 and records | `Landed += F8`; `classification.tsv`; PLAN (G147 (1)); A5-F8's old files for R6-check (AR-4); CODEMAP; ARCH's R4 row and PLAN §3 | 2d-2–2d-8, OQ6 | Comp: `VerifiedSpecExampleTest`, `MirDifferenceTest`; +led. Done when summary.md lists F8 and only G147 (2)'s rows remain open until 2d-9 | +1, tsv ±20, doc +20 | 2 | G:A5-F8 | no |

### 4.7 A5-F9 defaults (2e)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2e-D | A5-F9 design | Two designs and a design review of ER-defaults: the PreparedInput Deref's step class Inline; R26-borrow; R26-access; P9 rows (including a raw-pointer input); the copy disposition (PD15: R-a over every inspection Loan the omitting call's defaults form, against the Inlined drop, judged on the j1-shaped case; the builder option would edit `MirBuilder.Defaults.cs`, a W1 file R5 restructures first); the code for stores into storage the body does not own; the Anchor fact; precedence over E12 (PD11); the S1015 sections it implements | 2x-P9, 1c design adopted (R4-M68–R4-M70), G148 decided | Doc. Done when the review leaves no blocking finding, the owner approved the package (OQ2) and DEC records the approval naming G:A5-F9's units | doc ~150 | 3 | FP | no |
| 2e-1 | P9 template-call rows | A template call (`Fn`, `Arg > 0`) gets one Content → Result row per input, untied, no store rows (SPEC 7.2.3), with a raw-pointer input's row per 2e-D; G148's unknown-callee rows (`Calls.cs:73-91`) and R4-M70's taint for template calls deleted; DEC (ER-defaults; superseding R4-M57's and R4-M63's "unknown callee until F9") | 2e-D, G148 merged, 2b-3 (chain) | Comp: new `T/MirBorrowsTest.Defaults.cs` (`RecursiveDefaultTest`'s Generic f(4, 7), Closure, Reference give none; `let r = f(3, n@ref); n = 4; _ = r@follow` errs; the reserved-input case; a raw-pointer input; p06 gives none); +V16 "F9 template calls" (N arguments: rows linear, quadratic before); +cen; +led. Done when a5-defaults and u-defaults have 0 rows | +10/−45, t+60 | 2 | G:A5-F9 | no |
| 2e-2 | PreparedInput rules | One row in `MirBorrows.Places`; new `M/Analysis/MirBorrows.Defaults.cs` with R26-borrow and R26-access; a hook in `.Checks.cs`; requirements `Ownership.DefaultArgumentAccess`/`Borrow` with the Anchor parameter as a fact (PD31); Moves stay A1's (R4-M69); DIAGNOSTICS :108 amended (PD15 (iv)) | 2e-1 | Comp: `DefaultAccessTest` :25-32 (Access ×2, Borrow ×6; :28 valid per PD15), :16-20 and :44 valid; `SemanticsDefaultTest` ExclusiveCase; audit-calls row 26; SPEC 7.2.3's label and erased valid; captures; a raw store; +V16 "F9 prepared inputs" (N prepared inputs); +warm with a default; +cen; +led. Done when a5-old-default-access and -borrow have 0 blocking rows | +70/−5, t+120 | 3 | G:A5-F9 | no |
| 2e-3 | Inline copies of an invalid default | As 2e-D decides; recommended R-a: at the omitting call, before its checks, the inspection Loans its defaults form end (PendingSlot roots and reserved explicit arguments), with no builder change; deletes the rest of R4-M70's default taint (DEC) | 2e-2 | Comp: p15 `fc(n@uniq)` gives only the template's `DefaultArgumentBorrow_Kd`; a valid default in a two-view generic caller gives none; an inline default inspecting a reserved explicit argument is valid; a j1-shaped holder keeps its independent error; +cen; +led. Done when a grep finds no default taint and no default-slot `Unsupported_Kd` | +20/−20, t+40 | 2 | G:A5-F9 | no |
| 2e-4 | A5-F9 records and ledger close | M39's Binding part void (†, with OQ6's answer); ARCH's R4 row and PLAN §3; CODEMAP; PLAN; `classification.tsv`; 7.2.3's target sides; OldRule on `DefaultAccessTest` :28; `Landed += F9` | 2e-1–2e-3, OQ6 | Doc + Evid: +led. Done when e0ffa5a60934, 9fcd0c360b5d, b29228117a54, dc7ace954713 pair and the rows 2x-T2 assigned are gone (15 on the old base, H) | doc, tsv ±6 | 1 | G:A5-F9 | no |

### 4.8 C5 and A5-F6 preserves results (2f)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2f-S1 | S2 revision sketch | `S2/preserves-revision.md`: S2 rebased on dev: SPEC 15.6.3:570 and :582 with P12 and E8; §2.2's static refinement under P6 (vacuous, H); the divergences from A3 §5 (Move carry, invalidation by exclusive Borrow, effects of calls inside a conformance, the earlier result); how rule 1 is decided; the 12 + 3 conformances and §5's losses re-read. The proposal file is untouched until OQ3 instructs its revision | — | Doc. Done when line references match dev and no divergence from 2f-D's draft is open | docs +80 | 3 | R (proposal review) | no |
| 2f-D | A5-F6 design | Two designs and a design review: Φ_c's conflict domain (carrier reach, or a Place phantom with a loss, OQ8); invalidation and Move carry (`MirLoanSets` Remove+Add or a new operation superseding R4-M53's four; 2x-R10 first); the conformance's earlier-result holder (a fixed point of σ(_0) at returns) and delegation tags; desc(c) into stores via W (G113); A6 rows (`CallEffectConflict_Kd` at the later call; a Φ_c met at a receiver's formation located at its call); R4-M68's condition; E3's residue (re-add a shared cut only if Φ_c lies past a shared Deref); 2x-S1's stored-Φ_c reading; PD30 for S2's rejections; ER-preserves; the S1015 sections it implements | 2x-P6, G148 decided, 2x-S1 | Doc. Done when the review leaves no blocking finding, the owner approved the package (OQ2) and DEC records the approval naming G:A5-F6's units | doc ~100 | 4 | FP | no |
| 2f-1 | C5-Binding: preserves obligations | `EffectObligation` gains its bound kind; preserves rows for conformance witnesses (explicit specializations included), functions under a slot's preserves bound and Callable evidence (Function Item, concrete Closure); A4 reads Confined rows only; the old `Check` stays until H5 | 2f-D | Comp: `MirEffectsTest`, `EffectBoundImplementationTest`, `EffectBoundDeclarationTest`, `KimiLibraryShapeTest`; warm 0 B; +cold; +cen; bp. Done when a view test lists one preserves obligation per library (12) and milestone (3) conformance | +35/−3, t+40 | 2 | D+R (2f-D) | adv (`BindingModel.cs` after R5-F2b), BU |
| 2f-2 | Abstract, descent and carrier facts | New `B/TyCtxt.Preserves.cs`: per call `abstract`, `descends`, `preserves` (a multi-signature Callable input a located `Unsupported_Kd`, registered for X-2) and, if 2f-D picks carrier reach, the abstract carriers | 2f-1 | Comp: view tests (takeTwo, Merge, Iteration adapters, nextPair, Callables f and g, `P<i32,R>`/`P<i64,R>`), `KimiLibraryShapeTest`; warm 0 B; +cold; +cen. Done when the facts match hand-computed expectations | +70, t+60 | 3 | D+R (2f-D) | no |
| 2f-3 | Rule I: desc(c), stores, rule 1 | New `M/Analysis/MirBorrows.Preserves.cs`: an abstract descending call issues desc(c) into its result and through W into the storage its store rows write; Φ_c conflicts per 2f-D with the call as a fact (PD31); where only rule 3 could excuse, a located `Unsupported_Kd` (R1g's MIR twin); a conflict only a stored Φ_c causes is a located `Unsupported_Kd` unless 2x-S1 read otherwise; a Take-only second call is a Language error; DEC (A5-F6's representation, ER-preserves, rule 1 under row 10) | 2f-2, G148 merged, 2c-6 (chain) | Comp: `MirBorrowsTest.Preserves` (36a–36g, a G113 store case, a stored-Φ_c case, Take-only, tupled, f/g, two requirements, a Merge-shaped caller), `VerifiedSpecExampleTest` (15.6.3-abstract-descent; an 8.11.1 Take-only row); +V16 "F6 descents" (N abstract calls × M store rows); +warm; +cen; +led. Done when 5e1a449cc7bf's A5-F6 rows are matched and no new blocking row appears | +120/−5, t+180 | 4 | G:A5-F6 | no |
| 2f-4 | Rule II: the caller relation | An undisturbed Φ_c of the same requirement, Callable value or slot on the same receiver Place is excused at the call and its reservations (E2); invalidation by write, assign, destroy, part Move or exclusive Borrow of the receiver, a part or an ancestor; a Move carries the relation; a non-path index relates nothing; a static-rooted receiver is invalidated by a call whose A4 keys write it; replaces 2f-3's rule-3 `Unsupported_Kd`; forms S2 rejects that the current SPEC accepts (takeAcross, S2 §5's losses) report a located `Unsupported_Kd` until 2f-S2 (PD30) | OQ3, 2f-3, 2x-R9, G:A5-F5 merged | Comp: 33a–33e, takeAcross in both spellings, moved, S2's losses 1–5, `CallableEffectBoundTest` R1g rows, 8.11.1 and 22.2.3 nextPair; +V16 "F6 caller relation" (N calls × M receiver Places, invalidation per write); the relation's memory term in `Prepare`'s limit with a test at the limit; +warm; +cen; +led. Done when takeTwo and nextPair are valid and takeAcross reports the located `Unsupported_Kd` at S2's location | +110, t+180 | 4 | G:A5-F6 | no |
| 2f-5 | Rule III: conformance | Each preserves obligation's implementation body is checked with an earlier-result holder live from entry to every exit; delegated (d, w) tags excused unless w is moved or invalidated; problems map to `IncompatibleContractImplementation_Kd` at the Self-is-C clause or `UnsatisfiedEffectBound_Kd` at a slot's function or Callable argument; a driver step | 2f-1, 2f-4, 2a-1b, 2c-2 | Comp: Drain valid, Merge an error (a new 8.11.5 case); each S2 §4 conformance a test; +V16 "F6 conformance" (N conformance bodies × body size); +warm; +cen (12 + 3 conformances accepted; a rejection is OQ7); +led. Done when the 40 A4 rows 2x-T2 assigned are cleared or classified with clauses | +170/−10, t+220 | 4 | G:A5-F6 | drv |
| 2f-6 | A5-F6 records, migration and ledger close | OldRule target sides; S2 §5's "not a loss" tests; `classification.tsv` (rows 17, 18, 20, 21; 5e1a449cc7bf split); STATUS Callable-effects row; PLAN (G113, R1g closed; G106's part); CODEMAP; `Landed += F6`; ARCH's R4 row and PLAN §3 | 2f-5 | Doc + Comp: +led. Done when the 52 rows 2x-T2 assigned are gone, 6440a7d24123, 5f5b9e82ef48, 732662664f20, 0be4e108bc23 pair and a105a408a756, 936f8571825e are rejected on MIR (with PD30's code where S2 decides them) | docs, t±100 | 2 | G:A5-F6 | no |
| 2f-S2 | S2 SPEC integration | The proposal revised from the sketch, then SPEC 6.4.6, 8.11.3–8.11.6, 15.6.3:582, 22.2.3, App. E; takeAcross and moved in 8.11.1; INTEGRATED intake; a separate session, as for P12 and E8 | OQ3, 2f-D | Doc. Done when SPEC states S2 as adopted, with examples, before R4's exit | docs | 2 | R (SPEC) | no |
| 2f-7 | S2's Language codes | The forms 2f-4 and 2f-5 report as located `Unsupported_Kd` under PD30 switch to their Language codes; spec-case and test expectations follow the integrated SPEC | 2f-S2, G:A5-F6 merged | Comp: `MirBorrowsTest.Preserves`, `VerifiedSpecExampleTest`; +led. Done when no S2 interim `Unsupported_Kd` remains | +5/−10, t±30 | 1 | R | no |

### 4.9 A6 completion (2g)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2g-D | A6 design | Two designs and a design review of ER-derived: the cause contract after R4-M70 (A1, A3, A4 problems at Opaque sites; A5 has none); the cause site on the problem read at H3's publication (†) or A3's keys in `MirOpaque`; Opaque access kinds and the completion-undecided flag (G136, G105); the assumed completion of the written Place's Move Path only; one primary-location rule per code family; R5's acceptance of H3's change (AR-7) | 2x-P10, 1c design adopted, AR-6, AR-7 | Doc. Done when the review leaves no blocking finding, the owner approved the package (OQ2) and DEC records the approval naming G:A6's units | doc | 3 | FP | no |
| 2g-1 | E5: derived problems keep their cause | A problem whose subject is an Opaque site keeps one cause fact (R4-M10's "dropped" superseded, DEC); `MirDifferenceLedger.Sides` excludes MIR problems carrying a cause fact from the MIR side (they arise only in Binding-rejected programs, so R4-M12's count is unchanged); a contract test checks each cause site (a node Binding failed, or a MIR construction record's subject); `MirReport.Add` drops nothing silently | 2g-D, OQ6, 2f-5 (chain), AR-3 | Comp: `MirAnalysisTest` (cause contract over the corpus's rejected programs), `MirDifferenceTest`; +led. Done when every derived problem has exactly one valid cause | +40/−10, t+50 | 3 | D+R (2g-D), outside G:A6 (PD29) | adv |
| 2g-2a | A6's builder publication | In W1 slot S-b: the builder publishes each Opaque root's access kind (G136) and marks an Opaque site whose completion Binding left undecided (G105) in `MirOpaque`; MirDump prints both; DEC (the Opaque access kinds) | 2g-D, AR-1 slot S-b, BU U5, 2x-R8 | Comp: `MirBuildTest`, `MirDumpTest`, goldens; sel; +cen. Done when every Opaque site has its kind and flag | +40/−5, t+30 | 2 | D+R (2g-D), outside G:A6 (PD29) | W1 S-b, sh |
| 2g-2 | A1 derived rule (G105) | A1 treats an Opaque write as initializing the written Place's Move Path only (assumed completion) and an Opaque read as no Move; A1 problems at Opaque sites take 2g-1's cause | 2g-1, 2g-2a, 2x-R7 | Comp: `MirInitTest` (an assignment Binding rejects; a rejected constructor Field; `x.a = <rejected>; use(x.b)` with `x.b` uninitialized reports `x.b`; an independent uninitialized use on another path stays); +warm (`MirInitTest`'s allocation test with an Opaque site); sel; +cen; +led. Done when a1-incomplete has no new-side row and Milestone 41's A1 rows are derived | +30/−10, t+70 | 3 | G:A6 | no |
| 2g-3 | A3 derived rule (G105, b1/b3) | MirLints derives `RequireFallthrough_Kd` when every runtime path to the body's end passes a site 2g-2a flagged | 2g-2a, 2g-1 | Comp: `MirAnalysisTest`, `ControlFlowAnalysisTest.RequireFallthroughs`; +cen; +led. Done when rows b1 and b3 match nothing and are deleted | +20/−5, t+30 | 3 | G:A6 | no |
| 2g-4a | Primary locations: A1 | The rule per code family (SPEC 23.1, DIAGNOSTICS) for A1's Moved/Uninitialized; the MIR subject fixed where it contradicts the rule, else (d) with the clause | 2g-2, 2g-3, 2c-7, 2d-10, 2e-4, 2f-6 | Comp: `MirInitTest` location rows, one per a1-location signature 2x-T2 assigned; +led. Done when no open (b) A1 row of kind location remains (≈ 78 on the old base, H) | +20/−10, t+40 | 3 | G:A6 | no |
| 2g-4b | Primary locations: A5 codes and restated rows | The rule for the A5 Loan codes (a5-location) and the restated rows; each restated row classified as the same failure at another location, or a missed independent error (a (b) defect of its family with a regression test) | 2g-4a, 2g-1 (chain) | Comp: the family test files' location rows (named per signature); +led. Done when no open (b) row of kind location remains (≈ 312 on the old base, H) and every restated row has its class | +20/−10, t+60 | 3 | G:A6 | no |
| 2g-5 | Cross-strata merge completed | 2a-2's merge for every family reporting in both strata (A5-F4, A5-F7's escape, A5-F9); for a PreparedInput-rooted Borrow that A5-F9 reports as `DefaultArgumentBorrow_Kd`, the Origin-stratum problem is dropped (PD11) | 2g-4b (chain), 2a-2, 2c-6, 2e-2 | Comp: twins per family (A5-F4 `return local@ref`; A5-F7's escape; A5-F9's PreparedInput: one problem per failure; a Refuted Borrow plus an earlier write conflict on the same Loan reports both); +led. Done when no subject has a Loan and an Origin problem for the same failure in the ledger run's MIR side, the rejecting spec-case variants or the diagnostic corpus | +15, t+60 | 2 | G:A6 | no |
| 2g-6 | G126 A1/A3 cleanups (bp) | One encoding of "every view"; MirInit's contract names `OwnershipStorageLimit_Kd`; G126's missing tests (a Move across a loop iteration, a `let` assigned in a loop, a forward P2 join) | 2g-2 | Comp: `MirInitTest`, `MirDropTest`; bp. Done when G126's A1/A3 items close | +20/−20, t+40 | 1 | G:A6 | no |
| 2g-7 | A6 records and contract | An A6 contract test (each MIR problem code's recorder, requirement and fact rows match DIAGNOSTICS' A6 table); CODEMAP; `classification.tsv` (G105 rows); PLAN (G105); ARCH's R4 row and PLAN §3 (A6 done); R5's planning session notified for Q19's second reading | 2g-1–2g-6 | Comp + Doc: the contract test; +led. Done when the contract test passes and no row cites "A6 (unit 1c)" or G105 | docs, t+40 | 2 | G:A6 | no |

### 4.10 R4's exit (X)

| ID | Unit | Change | Prereq | Verification | Size | Diff | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| X-1a | (Conditional, OQ10 (a)) The old gate's identity in the raw row | The old Unsupported gate's identity at `OwnershipAnalysis.cs:2119` and ≈ 50 sites (D5 exception); the ledger row shape (R4-M17) | OQ10 (a), R5-Q17 merged, AR-3 | Tool + Comp: `MirDifferenceTest`; old-path records unchanged; +led. Done when every G106 row carries its gate | +15/−5, t+20 | 2 | R | D5, adv |
| X-1 | Ledger closure | Residual rows: E8 ×4 (d) R4-M52 (a4e693a304e9, 55f3c3c9a921, 943a89665f91, ebd2922c028d); a1aae04c6469 (a) 4.7.4; a5815e667296 (a) G147 (7); af43dcd8ffce per 2x-S1; the U Invocation Never row 76521fc87e18 (a); G106's remaining U rows re-triaged by test; matchless and wildcard (b) rows deleted; owners for the ownerless 4efae67ec7a7, f0c29ce98038, 92df8d7329c3, 21bbb68ddf53; each restated row's class checked (2g-4b); the codes the ledger does not compare (TransferRequired, A-13; control-flow lints; `OwnershipCaseLimit`; the effect records `IsEffectRecord` filters), each with the tests that stand in for it | every family, 2f-7, 2x-E1, 2x-E2, AB-3, AR-8, X-1a if OQ10 (a) | Evid: VERIFICATION's mir measurement at the head (Functional and Allocation under mir with `KIMI_MIR_DIFF`, equal failure sets). Done when summary.md shows 0 open blocking, 0 unclassified signatures, no library or milestone row, open (b) per OQ5, and every uncompared code class names its stand-in tests | tsv ±60, doc ~20 | 3 | R | no |
| X-2 | `Unsupported_Kd` census (PD28) | Each MIR-side producer (34 grep sites at the base, plus every registered one) with SPEC form, owner and removal stage; no family-wide gate (A3-closure, default taint and G124 gone); each A5-relevant S1015 section (§8.11.2, §14.10.3, §15.2.4, §15.3.5, §15.3.6, §15.6.1, §15.6.4, §16.2.1, §16.3.3) covered by a family's MIR tests or registered as a located `Unsupported_Kd` in STATUS; the remaining rules and stages as one list (OQ9, with OQ13's S5 answer) | X-1, OQ13 | Doc + Evid. Done when each producer has a STATUS or PLAN row with an owner-approved stage | doc | 2 | R + OQ9 | no |
| X-3 | R6-check handoff list | `S2/r6-check-handoff.md`, delivered to R5's planning session through AR-4 (R4 does not edit R5's plan): every old-Analysis member read outside `C/Analysis` (src, tests, `src/Benchmark`) mapped to its H unit; `Ownership.Bodies` sites tagged (R4-M13a); the 59 `Transitional` R6 declarations per H unit; §7's deletion-list additions; A-13 recorded; ARCH's stage-ledger rows only if a stage changes | each records unit | Doc; `ArchitectureRulesTest` if the Bodies ratchet falls. Done when every external reader has an H unit and the ratchet equals the count | doc, ≈ −9 test reads | 2 | R | no |
| X-4 | Exit measurements | PLAN10 P3 "MIR build + A1–A6" against the old analyses (pinned interleaved, compiler id checked); each V16 shape VERIFICATION lists; warm Bind/Analyze 0 B; area sizes, hotspots, duplication candidates in `artifacts/benchmarks/`; the default pipeline's `-DiagnosticSnapshot` against the step-2 base with each difference recorded | X-1, X-2 | Bench. Done when the artifacts exist and no default-pipeline difference is unrecorded | 0 | 2 | — | no |
| X-5 | Final end-to-end review (R4-M35) | An independent reviewer checks App. A rules 1–30 against rule tests; A3 E1–E14 and step 2's ER-* implemented; losses = SPEC clauses; prototype parity (every round case, including the A5-F6/A5-F8 cases `MirBorrowsTest.Prototype.cs` omits, has a MIR twin with the same verdict); a symptom-fix sweep; V16; sampled (a)/(d) clauses; restated rows sampled with the paired failure removed (MIR still rejects); X-1's uncompared-code list; fact rows per code (PD31); the DIAGNOSTICS A6 contract | X-1–X-4 | Session (default), the mir measurement, all DONE milestones; review record. Done when no blocking finding remains | 0 | 3 | final | no |
| X-6 | R4 exit records | ARCH: R4 Done with the evidence, and PLAN §3 (H1, H3, H5 unblocked) in the same commit; PLAN §7 (G103, G106 closed); STATUS (the MIR analyses' boundary and limits); CODEMAP; R5's planning session notified for H0 | X-5, OQ9 | Doc. Done when ARCH's R4 row reads Done | docs | 1 | R (ARCH); — for PLAN, STATUS, CODEMAP | no |

---

## 5. Ledger, metrics, stop rules and the exit gate

### 5.1 Ledger by stage

Open blocking rows on the 806110fc base (H on later bases; 2x-T2 re-measures and re-assigns), in PD1's order:

| Stage | Blocking rows it closes (signatures) | Non-blocking (b) rows it closes | Open blocking after |
| --- | --- | --- | --- |
| Base | — | — | 440 |
| 2x-T2 | G147 (4)(5)'s 2 rows if R5-F14/F15 paired them (else X-1) | — | re-measured |
| 2x-T3 restated (OQ5 (iii)) | ≤ 74: of DUP-A1 26 (Comparison/Field 8, /If 3, /NumberLiteral 1, CallReservation/NumberLiteral 4, parts of /Invocation 7, /IdentifierName 2, /MemberAccess 1), DUP-A5 39, E8 Conversion 2, Origin 2, U 5 (3 through `BodyOf`), only those meeting the narrowed predicate | — | ≥ 366 |
| 2x-T4 | 180: U missing IdentifierName 136, MemberAccess 30, SyntaxForm 2, U acceptance IdentifierName 5 (Milestone 42), Comparison/Conversion 4 (d), Comparison/MemberAccess 3 (a) | — | ≥ 186 |
| 2b (A5-F5) | 0 (PD7) | F5 (2 signatures, 18 rows); u-static re-checked | ≥ 186 |
| 2e (A5-F9) | 15: DefaultArgumentBorrow 10, DefaultArgumentAccess 5 | F9 (4 signatures, 30 rows); a5-defaults/u-defaults 3 | ≥ 171 |
| 2a0, 2a (A5-F4) | 85: Unproven Field 38, TypeSemantics 17, Invocation 9, Conversion 3, IdentifierName 2; Unsatisfied Conversion 9, Field 2, IdentifierName 2, Invocation 2, NumberLiteral 1; UnprovenConstraint 1; Loan Comparison/Binary 1 | a5-old-origins-binding (30 signatures, 422 rows) re-owned by 2a0-4; a5-new ≈ 13 | ≥ 86 |
| 2c (A5-F7) | 5: Comparison/IdentifierName 3, /CodeBlock 1; G147 (3) 1 | a5-writes (8i) 16 rows (with 0c) | ≥ 81 |
| 2d (A5-F8) | 19: G147 (1) 10 (Comparison/Binary 5, CallActivation/Invocation 2, Placement/DictionaryLiteral 2, Comparison/IdentifierName 1); G147 (2) 9 (after BU) | update duplicates go to 2g-4b | ≥ 62 |
| 2f (A5-F6) | 52: A4 IncompatibleContractImplementation/Is 35, UnsatisfiedEffectBound/MemberAccess 3, /IdentifierName 2, U Invocation 3, CallEffectConflict/Invocation 9 | F6 (10 signatures, 160 rows); a4-binding 39 + 39 | ≥ 10 |
| 2g (A6) | 0, plus restated rows found to be missed independent errors (they become their family's (b) defects) | G105 (42 signatures, 253 rows), a5-location (14 signatures, 312 rows) | ≥ 10 |
| 2x-E1, 2x-E2 | 0 | E9b 95 (b), E9a 10 (e) | ≥ 10 |
| X-1 | the rest: 7 residual, 1 U Never, 2 of G147 (4)(5) if still open, any restated row 2x-T3 left `missing` | the 4 ownerless rows; the rest per OQ5 | 0 |

**Exit metric.** summary.md at the exit head: 0 open blocking rows, 0 unclassified signatures, no library or milestone row,
`Landed` = the 14 families (A5-F5 with its recorded (e) variants), A6's contract test passing, and the uncompared code classes
covered by their stand-in tests. Open question: whether open (b) rows naming R4 work must also be 0 (OQ5 (i)).

### 5.2 Metrics

| ID | Metric | Moves with | Direction |
| --- | --- | --- | --- |
| N1 | Open blocking ledger rows (§5.1) | 2x-T3, 2x-T4, every family's records unit, X-1 | falls to 0 |
| N2 | Families in `Landed` (today 4 of 10 plus Binding, A1, A3, A4) | 2x-T7 (F10 with a variant), 2a-4, 2b-8, 2c-7, 2d-10, 2e-4, 2f-6; A6 by 2g-7's contract test | rises to all |
| N3 | SPEC-example variants checked on MIR with their code or a recorded (e) difference (43 blocks, 63 variants) | every records unit | rises to all |
| N4 | Census problems of every analysis (2x-T1) | every +cen unit | stays 0 |
| N5 | Prototype cases with a MIR twin of the same verdict | every rule unit | rises to all |
| N6 | Listed losses, each an owner-approved clause integrated in SPEC | OQ8, PD30 | decided and integrated |
| N7 | MIR `Unsupported_Kd` producers without a stage | 2b-3, 2c-3, 2e-3, 2f-7, every registering unit, X-2 | 0 |
| N8 | OldRule methods (19), spec-case Old sides (22), pair-case Old sides (1) with a passing target side | 2x-T5, records units | rise to all |
| N9 | `Ownership.Bodies` reads (182) and their stage tags | migration units, X-3 | fall; all tagged |
| N10 | New problem codes with tested fact rows (PD31) | 2a-1a, 2a-1b, 2b-3, 2e-2, 2f-3 | all |

### 5.3 Stop rules

- **A5P §8.** The owner is consulted after two consecutive working sessions on step 2 that land no unit; a planning- or
  record-only session lands nothing.
- **Unit timebox.** A unit not landed within two working sessions is re-split or goes to the owner.
- **Review.** Two blocking review rounds on one family of findings go to the owner (representation, whitelist or hold; for
  A5 rules OD1 excludes the whitelist, A5P §9.3).
- **Corpus.** A census failure on a library or milestone program stops the unit's merge (OQ7).
- **Gap.** A probe or test that finds a rule gap (2d-0, 2d-4–2d-6, 2x-T7) stops for the full process; it never becomes a
  narrow acceptance or a conservative Language error.

### 5.4 R4 exit gate

1. Every family A5-F1–A5-F10 in `VerifiedSpecExampleTest.Landed` (A5-F5's static-Borrow variants with their recorded (e)
   MIR differences until P42, approved in OQ9) and A6's contract test passing; every SPEC example of A5P App. A checked on MIR
   with its code or such a recorded difference; App. A rule 22 covered by MIR rule tests.
2. The ledger at the exit head per §5.1's exit metric and OQ5; every code class the ledger does not compare has its
   stand-in tests (X-1).
3. 2x-T1 green: no problem of any MIR analysis in the library, the 44 milestones, the 31 goldens or the accepting SPEC
   variants (R4-M12).
4. No `Unsupported_Kd` covers a whole A5 family; every remaining MIR producer has an owner-approved stage (OQ9, OQ13).
5. Prototype parity; every listed loss an owner-approved SPEC clause integrated in SPEC (PD30); every A3 erratum and ER-*
   implemented.
6. Every OldRule method, spec-case and pair-case Old side has a passing MIR target side; tests asserting A5 rules read MIR
   facts; every `Ownership.Bodies` read carries its stage (R4-M13a).
7. Each V16 shape within 2×/4×; VERIFICATION lists every shape (each added by the unit that added its test).
8. PLAN10 P3's "MIR build + A1–A6" recorded against the old analyses; warm Bind and Analyze at 0 B.
9. X-3's handoff list delivered; asks A-4, A-5, A-6, A-7 and A-14 met; S2 integrated (2f-S2) and 2f-7 landed, or OQ3 (b)'s
   exception recorded.
10. Every new MIR problem code publishes its fact rows, each checked by a test (PD31).
11. Records (ARCH, STATUS, PLAN, CODEMAP, the DIAGNOSTICS A6 table; area sizes, hotspots and duplication candidates) and the
    final end-to-end review passed.

### 5.5 Evidence cadence

| Evidence | Cost (H) | When |
| --- | --- | --- |
| Census test (2x-T1) | minutes | every unit marked +cen: rule, view, builder and Binding-acceptance units |
| Ledger run (mir Functional with `KIMI_MIR_DIFF`) | about one Session | every unit marked +led |
| bp triple | seconds after a Session | every restructuring and bp Binding unit |
| sel pending check, and from R5's G3 the affected native families and O2 kernels (A-10, R5-M8) | about a Unit run, plus the selected families | every unit touching `Mir/Build`, `MirTypes`, `MirInit` or `MirDrops` (2b-5, 2d-1–2d-3, 2g-2a, 2g-2, 2x-E1, 2x-E2, 2x-E4b, 2x-R8) |
| Cold Bind measurement (+cold) | ≈ 10–20 min on a quiet machine | 2a0-2, 2b-4, 2c-2, 2f-1, 2f-2 |
| Pinned interleaved timing | ≈ 10–20 min on a quiet machine | 2b-2, 2x-E5, X-4 |
| Native fixtures and O2 | per selection | 2d-3 (`Dictionary*.ll`) |

---

## 6. Volume and difficulty

### 6.1 Totals (H)

| Stage | Units | Compiler lines (+/−) | Tests | Drafts and documents |
| --- | ---: | --- | --- | --- |
| Cross-cutting (2x) | 37 (1 conditional) | +220/−54, moves ≈ +1,440/−1,360 | +455, ±160 migrated | prototypes ≈ 2,400 lines; S5 proposal |
| 2a0 | 7 | +398/−64 (boundary per OQ11 (b)), move +280/−270 | +310 | census, design |
| 2a (A5-F4) | 6 (1 conditional) | +415/−5 | +570, ±200 migrated | — |
| 2b (A5-F5) | 9 | +227/−60 | +440 | design |
| 2c (C2, A5-F7) | 9 | +315/−70 (old Analysis +15), move +170/−165 | +410, ±20 | design |
| 2d (A5-F8) | 12 (1 conditional) | +66/−15, library +4, move +210/−200 | +510, ±400 migrated | — |
| 2e (A5-F9) | 5 | +100/−70 | +220 | design |
| 2f (C5, A5-F6) | 10 | +510/−28 | +680, ±130 migrated | design, S2 sketch, SPEC |
| 2g (A6) | 10 | +205/−70 | +420 | design |
| X | 7 (1 conditional) | +15/−5 | +20, −9 reads | records, handoff list |
| **Total** | **112 (4 conditional)** | **≈ +2,490/−440, net ≈ +2,050; moves ≈ +2,100/−2,000** | **≈ +4,000, ≈ ±900 migrated** | six design rounds, the S2 sketch, S5's proposal |

The volume is small next to R5/R6 (≈ 9,300 added lines in R5 alone). The cost of step 2 is design and evidence, not code:
six full-process rounds, eight prototype rounds and a ledger run per rule unit. The unit count grew in the revision through
splits that make each unit commit-sized (2a0-3, 2a-1, 2g-2, 2g-4, 2x-E4), the hotspot schedule (2x-R6–2x-R11), the adoption
record (2x-A0), S5's proposal, 2x-P10, the BU repairs 2x-E6/2x-E7 and the interim-rule switch 2f-7.

### 6.2 Difficulty distribution

| Stage | 1 | 2 | 3 | 4 | 5 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2x | 15 | 11 | 10 | 1 | 0 |
| 2a0, 2a | 2 | 3 | 4 | 4 | 0 |
| 2b | 3 | 3 | 3 | 0 | 0 |
| 2c | 2 | 2 | 2 | 3 | 0 |
| 2d | 2 | 8 | 2 | 0 | 0 |
| 2e | 1 | 2 | 2 | 0 | 0 |
| 2f | 1 | 3 | 2 | 4 | 0 |
| 2g | 1 | 3 | 6 | 0 | 0 |
| X | 1 | 4 | 2 | 0 | 0 |
| **Total** | **28** | **39** | **33** | **12** | **0** |

The 12 units rated 4 are the three FP designs of new representations (2a0-D, 2c-D, 2f-D), the A5-F6 prototype round, and
eight implementation units: 2a0-3a and 2c-5 move a judgment across phases (2a0-3a's twin under OQ11 (b) is a temporary
structure with a removal stage); 2a-1a, 2a-1b and 2c-2 create a canonical component or a shared representation; 2f-3, 2f-4
and 2f-5 implement S2. Each is preceded by a census or prototype round and an FP design, and §6.4 gives its response.

### 6.3 Full-process designs

Six family design rounds (2a0-D, 2b-D, 2c-D, 2e-D, 2f-D, 2g-D), each approved once by the owner as a package (OQ2) and
recorded in DEC by its design unit, plus the S2 sketch's and S5's proposal reviews, a conditional FP for 2x-E1 if the
Aggregate contract changes, and the final end-to-end review (X-5). Read literally, the audits implied about 12 separate
double designs (each view, each erratum, G93, Transferable); PD5 folds them into their family's round.

### 6.4 Parts that are too large or too hard, ranked, with responses

| Rank | Part | Why | Response | Owner action |
| ---: | --- | --- | --- | --- |
| 1 | A5-F6 and S2 | S2 is undecided and stale; the conformance half sits near R4's exit (after H5 the 12 library Iterator conformances need it); A3's Place phantom accepts Merge and rule-1 forms SPEC rejects (H); the prototype's excuses are hand-given; its SPEC text lags the rule (PD30) | 2f-S1's sketch after the pause; 2x-P6 and 2f-D during the gate; rule I lands without S2; 2x-P6 derives the rules; PD30's interim `Unsupported_Kd` until 2f-S2; OQ3's fallback (b) keeps conformance checkable | OQ3 |
| 2 | Lane C's length | One serialized chain of about 23 units carries every family's rule code | Small rule units; restructurings placed by count (§3.7); preludes beside it; PD1's swap rule for a late prelude | — |
| 3 | A5-F4's Origin stratum | A new canonical component; the boundary timing is the owner's; atom identity between MIR Types and rows could break chains silently; per-mask-class cost; 2a-1 alone was +330 code, t+450 | Census first (2a0-0), its own prototype file, one FP design, bp split before the boundary, OQ11 (b) with no default-pipeline acceptance change, record-by-record snapshot classification, an atom-identity test per Ref form, 2a-1 split into 2a-1a (Refuted) and 2a-1b (Unknown, Work counter, memory term) | OQ2, OQ6, OQ7, OQ11, OQ12 |
| 4 | 2f-5 (+170/−10, t+220) | One driver step plus conformance checking of every preserves body | Its prerequisites carry the facts (2f-1, 2f-2) and the relation (2f-4); a stall past the timebox splits it into the earlier-result holder and the delegation tags | OQ3 |
| 5 | C2 and A5-F7 | A shared representation (a receiver slot in Retention); a D5 gate; BU's 0c must merge with 2c-3 or A3-closure becomes unsound; per-case receivers; `Binding.FunctionTypes.cs` over the limit | FP design with an R5 reviewer; OQ4; joint merge (AB-2); 2c-2 outside the group; 2c-5 net ≤ 0 in FunctionTypes or 2x-R11 first | OQ4 |
| 6 | Ledger proof of "no blocking row" | Mixed signatures (G106), wildcard rows and restated rows that can never pair; codes the ledger does not compare | 2x-T3/2x-T4 first with the narrowed predicate; each records unit closes named rows with reproducers; X-1's stand-in tests | OQ5, OQ10 |
| 7 | A6 last | ≈ 390 location rows; any family landing after 2g-4a/2g-4b reopens them; the builder hunks need a W1 slot | 2g-4a after every family's records unit; option P narrows E5; one short W1 unit (2g-2a, S-b) | OQ6 |
| 8 | Coordination | The pause; W1 now covers most builder files until Q16; BU's merges; `MirDifferenceLedger.cs` and the driver are shared | Asks AR-1–AR-8 and AB-1–AB-6 at adoption; slot order in AR-1; held-file units outside groups (PD29); new partial files; one hold line per unit | — |
| 9 | Hotspots and over-limit files | Twelve files due or near due; five Binding files over 5,000 tokens (`Binding.cs` 13,208, `Binding.Diagnostics.cs` 12,601, `Binding.FunctionTypes.cs` 7,522, `Binding.OriginDeclarations.cs` 5,975 read only, `Binding.Closures.cs` 5,911) | §3.7's schedule: 2x-R1–2x-R11, 2a0-1, 2c-0, 2d-1, R5's builder restructures; net ≤ 0 per Binding unit | — |
| 10 | A5-F5's testability | Binding rejects explicit static Borrows, so SPEC's 15.6.4 and 16.3.3 examples never reach A5 | Implicit static Loans; G100's record becomes `Unsupported_Kd`; the examples land as recorded (e) differences until P42 | OQ9 |
| 11 | A5-F9's inline copies | Keeping a drop over set-level taints can accept invalid programs (j1) | R-a over every inspection Loan, judged in 2e-D on a j1-shaped case | — |
| 12 | Prototype debt | Eight rounds before the rule units, serialized on one file | Drafts in lane D, rounds drafted on branches and completed after 2x-P0, one reviewer per round | OQ8 |

### 6.5 Calendar risks

1. The owner's pause of 2026-10-18; nothing starts before it is lifted.
2. G148's third design (GXS) under the full process, its owner question and SPEC session, and 1c's seven serialized units:
   lane C waits for them.
3. The owner's S2 decision (OQ3) must arrive before 2f-4; OQ11 and OQ12 before A5-F4's chain units.
4. W1's availability for the R4 slots (R5's own W1 order runs to Q16), R5's builder restructures, and BU's merges (`bu2`,
   U5, `bu4-index-uniq`).
5. Reviewer capacity: six FP design reviews and seven group reviews in step 2, while R4 reviewers also owe R5 sign-offs.
6. Machine time: a ledger run per rule unit and quiet-machine timing for 2b-2, 2x-E5, the +cold units and X-4.

### 6.6 Not verified

No code was built or run for this plan: the ledger counts come from the 806110fc base before 1c, G148 and R5's F12–F15; the
row causes come from the audits' reading of sources and event rows; every size, difficulty, library and milestone effect is
(H), as are the audits' own unverified items (atom identity, the multi-chain count, the F8 share of a5-old-loans, the G93
cost, the copy-equals-template lemma, the Φ_c conflict domain and S2's library verdicts), the restated predicate's row count,
and the hotspot counts after 1c and G148. Checked for this revision: `bu1` is in the base; token counts of the files in
§3.7; that no `tests/mir` golden prints a static's ` raw`; that no spec case has family F10; that `Variant.Mir` records
exist for landed families' differences.

---

## 7. Findings to record on adoption

**PLAN rows (2x-A0; net ≤ 0 lines: PLAN is at its 225-line limit).** Only findings no unit of this plan owns become rows;
the rest stay in this plan with their units.

| Finding | Where | Proposed owner |
| --- | --- | --- |
| An exclusive virtual receiver after a two-phase reservation: F14's `Dispatched` leaves the reservation in the Ref temp `_4` while the Call takes the Upcast `_5`, so A5 skips the activation check and false-conflicts a later receiver read; latent while Binding rejects objuniq/uniq Self virtuals. Choose before Binding admits them: an A5 rule (a Proven Upcast's Move-Cast of a reservation holder keeps the reservation) or a MIR form keeping the holder. Reproducer `virtual func bump(self: objuniq/Self, n: i32)` with `owner.bump(owner.tail)` (valid, SPEC 15.6.7) | pending-records (W1 sign-off) | R4 (A5 E13) and R5 (builder), before Binding admits the form |

Folded into existing rows: G100 (2b-6, 2x-E4a/b, 2x-E6, 2x-E7 named); G106 (the restated kind under OQ5, X-1a under OQ10);
G124 (2b-4, 2b-5); G147 ((2)'s validator half to R5's W1, AR-8; the owner column; G147 (6) done); G136 (the static items to
2b; V16 clearing to 2x-E5; Opaque access kinds to 2g-2a; `lent` validation to R5's W1, AR-8; the Value-callee arity to 2c-3);
G126 (A1/A3 items to 2g-6; the A2 group merge and A4's `readers[]` stay "with the next change to each file", whose next
changes are 2x-E4b and 2x-R6/2b-7; `IntrinsicId.DestroyValues` without a producer goes to X-3's list). G110 and G119 close
(2x-T6), G125 closes with 1c-9 (step 1), so the adoption commit adds no net line.

**Findings kept in this plan (each with its unit):** 5e1a449cc7bf's filing (2b-8, 2f-6); S2's staleness and A3 §5's
divergence (2f-S1, 2f-D); `RecordMismatch`'s inner chains (2a0-3a, OQ11); the "no principal candidate" record (2a0-3b, OQ12);
the containment skip and raw anchors (2a0-0, 2a-1a); the untracked census and V16 shapes (2x-T1, 2x-T6, each +V16 unit); no
OldRule format (2x-T5); `BodyOf` and R4-M47's predicate (2x-T3); a5-location without an owner (2g-4b); 15.5's label (2a-1a);
the two closure `Unsupported` families (2c-7 registers them for X-2); A4's StaticInit copy (2b-7); the four ownerless rows
(X-1); A5-F10's landing (2x-T7); the hotspot counts (§3.7).

**Corrections of existing records:** PLAN G110 and G119 are stale (landed with 1b); G147 (6)'s row text is stale; G147's
owner column shows (2) as BU's and (4)(5) as landed; G56's "a Binding-level relation stops ownership analysis" changes with
2a0-3a; `MirReport.cs:6` claims derived-drop rules that do not exist until 1c-6/2g-1; ask A-3 is met.

**DECISIONS-level records, each written by the unit that first implements it:** the plan's adoption, packet 1's answers and
U8p's supersession (2x-A0); each family package as an owner-approved design naming its group (its design unit, OQ2);
ER-rows and the C1-F4 view (2a0-2); the Origin boundary per OQ11, superseding R4-M9b's view list where it applies (2a0-3a);
`MirOrigins` as a canonical component and the stratum order (2a-1a); the E12/E7 merge contract (2a-2); the restated kind,
superseding R4-M47's clause (2x-T3, after OQ5); `OldRule = "ledger:<source hash>:<line>"`, refining R4-M50 and R4-M60
(2x-T5); A4's Keys classes, superseding R4-M38's `bool` (2b-1); ER-effects (2b-3); the Transferable view (2b-4); the C2
contract (2c-2); A5-F7's rule and the temporary rule's deletion (2c-3); the D5 gate (2c-5, OQ4); the setter writeback,
amending R4-M37's Open (2d-2); ER-defaults, superseding R4-M57's and R4-M63's unknown-callee clause (2e-1) and R4-M70's
default taint (2e-1, 2e-3); A5-F6's representation, ER-preserves and rule 1 under row 10 (2f-3); S2's adoption (2f-S2);
ER-derived, superseding R4-M10's "dropped" (2g-1); the Opaque access kinds in `MirOpaque` (2g-2a); the primary-location policy
(2g-4a); the hotspot rulings (the unit each precedes).

**R6-check deletion-list additions (ask AR-4):** the old Origin reporter (`OwnershipAnalysis.Borrows.cs:14-121`) and its
Unrepresentable gate (`:74-79`); Binding's `LocalRegionSources` consumers; under OQ11 (b), Binding's inner-chain twin records
and the gate `Binding.Diagnostics.cs:985` (H11); `CallLocalOrigin` (`Binding.Closures.cs:670-675`); 2c-5's old-Analysis
gate; the old default analysis (≈ 747 lines: `OwnershipAnalysis.DefaultDeclarations.cs`, `OwnershipAnalysis.Defaults.cs`,
`OwnershipBody.Defaults.cs`); A5-F8's old files (`OwnershipAnalysis.Match`, `.Iteration`, `.Updates`, `.Properties`,
`.Arithmetic`, `OwnershipBody.Updates` and A5-F8's parts of Elements, Sequences and Dictionaries); `IntrinsicId.DestroyValues`
without a producer (for R5 or R7 to remove or produce).
