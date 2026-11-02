# R6-check H0: the design package (index)

Status: H0's one design (plan §4.10 row H0, FP), revision 0: the index of three parts, [H0a](H0a-publication.md) (publication, H3),
[H0b](H0b-flip.md) (H1, H4, H5, H3b) and [H0c](H0c-deletions.md) (H6–H15, census, check-only pass, exit gate), with the planning session's
rulings R1–R4 applied in the parts (marked "(ruling Rn)") and the integration's consistency edits (marked "(integration)"). Not reviewed.
Working record, not authority: it becomes authority only as the DEC entry of §10 after the owner's approval under OQ11 (DECISIONS R5-M1).

Working time (WORKFLOW "Working-time limit", DECISIONS Q12): integration 2026-11-02 18:23–18:36, one session, within the one-hour limit.

Base: dev `1d23d559d` (the parts read `1ec539c79` and `e8ba140b3`; only PLAN differs). "Plan" is the
[R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` = `src/Kimi/Compiler/`,
`T/` = `tests/xUnitTest/Tests/`. No build, test or probe was run; the counts in §6 marked "verified" were re-grepped on the base.

## 1. Purpose and the owner's words

H0 designs R6-check: MIR problems published through `MirReport` (H3), the acceptance flip with the old effect checks deleted (H5, PD29)
with TransferRequired in Binding (H1) and A4's row checks (H4), the rehearsal and its classification (H3b), the deletion order of the old
Analysis and its readers (H6–H15), the census of what ends at R6-check, the check-only SPEC/IMPL pass and the exit gate (plan §5.5).
The owner approved the division of work on 2026-11-02 and told R5: 「その役割分担でOKです。H0の設計から始めて下さい。終わったら、
R4の完了を待ってください。」 H0 is designed during R4; its units run after R4's exit.

## 2. Division of work (the owner, 2026-11-02)

| Session | Owns in R6-check |
| --- | --- |
| R5 (lead) | H0, the H3b rehearsal and ledger classification, H5's Core side (and the Backend gate, I2), H8–H10, H12, H13, H15, the windows; Core's publication table (H3-2); H6 and H14 (not in the division; H0c gives them to R5) |
| R4 | The MIR side: H3's publication in `MirReport` (H3-1's Mir files, `MirReport.Records`), H4, asks A-4/A-5, MIR false rejections (H3b class 2 MIR rows), `MirDropTable.Incomplete` |
| BU | The Binding side: H1a, Q8-6, Q14, H1, Q8-3 (H, R4-12), H5's Binding hunk (BU-3), H7, H11, the end of the old-analysis D5 exceptions, Binding-cause rows |

## 3. Decisions (one line each; § of the part)

| # | Decision |
| --- | --- |
| H0a-1 | One publication table in Core decides each pipeline's publication by published columns; default unchanged to H5; mir from H3: every MIR problem but A4's, plus the old effect tail; from H5 every MIR problem everywhere (§2) |
| H0a-2 | Acceptance under mir is computed from the published rows; no `Binding.LintErrors` term (ruling R1) (§2) |
| H0a-3 | H3-1 (bp) folds construction and collector records into `MirReport` as `NoBody` `ControlFlow` problems; `MirReport` resets in `MirProgram.Clear`; validator findings stay `Findings` (§3.1) |
| H0a-4 | The merge across bodies by (code, subject, Reason) is `MirReport.Records` (R4); Core only maps records to the adapter (§3.2) |
| H0a-5 | A record's requirement is its problems' own (R4-M67, R5-M23); no code → requirement table; partition = requirement's (§3.3) |
| H0a-6 | Keys: a record with facts at its subject node, condition = its Reason's ordinal; a location-only code by `ReportLocation` (§3.4) |
| H0a-7 | One fact table, one row per `MirFactKind`; case evidence and Note through Q8-6's renderer; a kind without a row is `InternalInvariant_Kd` (§4.1) |
| H0a-8 | A record with `Cause` facts publishes as `PrerequisiteUnavailable_Kd`, `DerivedFrom` its causes' keys or `Binding.FailureCauses` for Value −1 (§4.2) |
| H0a-9 | No new PLAN10 P10 fallback producer: every MIR incompleteness already has a record (§4.3) |
| H0a-10 | H3→H5 under mir: A4's problems held; the old effect tail the one producer (Transitional, removal H5); its early-stop gap a mir-only PLAN open issue (§5.1) |
| H0a-11 | G128 (PD30): (A) no rule; the collector's `Unsupported_Kd` and A5's `OwnershipDepthLimit_Kd` at one call both stay, counted, until R7 (§5.2) |
| H0a-12 | Independence: only records with MIR or Binding `Cause` facts are dependent (§6) |
| H0a-13 | Owners: R4 the Mir files and `MirReport.Records`, R5 the Backend and Core files and the table, BU Q8-6's renderer and `FailureCauses` (§7) |
| H0b-1 | G:R6-check-flip = H1 → Q8-3 → H5: one branch, one group review, one merge; H1a, Q8-6 and H4 land on dev first (§2, §3, §8) |
| H0b-2 | H3b is the group branch's first full run, not a throwaway; its fixes land on dev; rebase and re-run until no class 2 or 5 row (§5) |
| H0b-3 | H5's Core: `Analyze` invalidates both partitions, builds the MIR, runs the old analysis as an unpublished shadow; `Analysis`, `Problems`, `ReportAnalysis` = the MIR's published rows (§2.1) |
| H0b-4 | No `LintErrors` term from H5 (a lint Error rejects through `Accepts`); `AnalysisResult.BodyCount` deleted (§2.1) |
| H0b-5 | `Project.cs:550-554` needs no edit (§2.1) |
| H0b-6 | Generation's gate is `!Accepts`; gate step 2 (`FunctionCodegen.cs:90-101`, PD12, R5-M96) deleted; the `Group == -1` invariant stays (§2.1) |
| H0b-7 | H5 deletes both old `ReportDiagnostics` and `FallthroughKey` (ruling R4); one test reader re-pointed (index I3) (§2.2) |
| H0b-8 | H4 is test-level (A): A4's summary of a row operation replaces `SummarizePublishedOperation`; four Array rows `Row: None`; `Binding.PublishedOperations.cs` deleted (§4) |
| H0b-9 | Five classes under Q13, pre-classified by fixed maps; Session, snapshot and milestones decide, the ledger proposes (§5.3, §5.4) |
| H0b-10 | Stop rule: class 5 stops H5; class 2 blocks until fixed on dev or accepted as one owner list; a repeat in a fixed family goes one level higher (§5.5) |
| H0b-11 | H6 lands right after H5, before H10a (ruling R3) (§7) |
| H0b-12 | Rollback: revert the group's merge alone until H10a (and H6, index I4); H10a waits for the post-merge sweep (§2.5) |
| D-c1 | From H5 to H10b the old analysis runs inside `Analyze` as an unpublished shadow, so its test readers keep their inputs |
| D-c2 | H10 splits: H10a (ledger harness, `Analyzed`, `KIMI_MIR_DIFF`, `IsTwinRecord`) after H5 and H6; H10b (the old Analysis) after H8a–c, H9, H6, H10a |
| D-c3 | Order: H6 → H10a → H8a → H8b → H8c → H9 → H10b → H11 → H12 → H14; H7 after H5 once G157 (1) is settled; H13 any time; H15 last (ruling R3) |
| D-c4 | Zero-reference rows in two steps: tests, `src/Benchmark`, `src/Playground` at H10b; `src/Kimi` at H11 |
| D-c5 | Stage 4 keeps `ControlFlowAndOwnership` and times `MirAnalysis.Run` plus collection; H9 splits `BuildMir` at the stage 3/4 boundary (bp first) |
| D-c6 | H14 also rewrites Mir's and the Backend's forwarding-property reads (R5-M9 postponed only Binding's) |
| D-c7 | Record corrections: H7's site, H11's A3 gates, H5's ledger filter, `ReportAnalysis`'s `:84` (stops at H5, ruling R2), G186's old walk at H5 |
| D-c8 | `ComparisonPlan(CallPlan)` and `DictionaryComparison` go at H11 (their compiler readers die with `EffectSummary` at H5) |
| R1 | H0a-2's `Binding.LintErrors` term is dropped (H0b-4: lint errors already reject through `Accepts`) |
| R2 | The old control-flow report (`Compilation.Pipeline.cs:84`) stops at H5, consistent with D-c1; its code goes when its last caller goes |
| R3 | H6 (hover's line from MIR problems) lands right after H5 (H0b-11), ahead of H0c's order |
| R4 | `FallthroughKey` and both old `ReportDiagnostics` go at H5 (H0b-7), having lost their only caller |

## 4. Units

Groups: G:R6-check-flip (H1, Q8-3, H5), G:check-tests (H10a, H8a–c), G:check-del (H7, H9, H10b, H11, H12, H14). Sizes (H) from the parts.

| Unit | Change | Owner | Prerequisites | Review | Group | Size |
| --- | --- | --- | --- | --- | --- | --- |
| H1a | Eager Final publication, `Check` split (bp; Q8 D8) | BU | G2 (done) | D+R (Q8) | — | ±70 |
| Q8-6 | One Semantics-case renderer in Binding: H1's first commit landing early (bp); the old caller until H5 | BU | before H3-2 | D+R (Q8) | — | ±60 |
| H3-1 | Fold construction and collector records into `MirReport` (bp) | R4 (Mir), R5 (Backend, Core) | R4's exit, OQ11 | R | — | +30/−45, t ±60 |
| H3-2 | Publication under mir: `MirReport.Records`, Core's table, keys, fact table, cause resolver; the old effect tail | R4 (`MirReport.Records`), R5 (Core, old tail) | H3-1, R4's exit with A6 (2g-D–2g-7), G11, Q17, Q8-6, OQ11 | D+R under H0, one review | — | ≈ +250/−15, t +300 |
| H4 | A4's summary replaces `SummarizePublishedOperation` (test-level); four Array `Row: None` | R4 | R4's exit, OQ11 | R (D+R under H0) | — | +60/−50, t ±60 |
| Q14 | Q8's probe, evidence in `artifacts/verify/r6-q14/` | BU | H1a; Q8-3's rule as a probe | — | — | 0 |
| H3b | Rehearsal on `claude/r6-flip` (H1, Q8-3, H5 as implemented) and the classification table (Doc) | R5 (R4, BU confirm rows) | H3, H4, H1a, Q8-6; drafts of H1, Q8-3, H5 | R | — | 0 (Evid + Doc) |
| H1 | TransferRequired judged in Binding (Q8 §3.7); the 10 old sites and `Ownership.TransferRequired` deleted | BU | H1a, Q8-6, Q14, OQ11 | group (one reviewer, BU dispatches) | G:R6-check-flip | +110/−75, t +150 |
| Q8-3 | `MirValidator`'s Copy rule (Q8 §3.9) | BU (H, R4-12) with R4's review | H1 | group | G:R6-check-flip | +30, t +40 |
| H5 | The flip; Binding's and the old effect checks, both old `ReportDiagnostics`, `FallthroughKey`, PD12's refusal, OldRule traits and Old sides deleted | R5 (Core, Backend gate, tests), BU (Binding hunk), R4 (`Incomplete`) | H1, Q8-3, H3b (no open class 2/5 row), H4, Q9–Q12, Q20/A-4, A-7, Q18 or H2 | FP (H0) + group + H15 | G:R6-check-flip | ≈ −2,350/+70, t −250/+150 |
| H2 | (Conditional) Problem Loan facts, only if Q18 has not landed through A6; not designed by H0 (plan row) | R4 | Q18 not landed | D+R (A3 §6) | — | +100 |
| H6 | Hover's "Ownership checks" line from MIR problems | R5 | H5 (right after it, ruling R3), H3 | R | — | ±20 |
| H10a | Ledger harness, `Analyzed`, `KIMI_MIR_DIFF`, `IsTwinRecord`; MIR helpers into `CompilationTestHelper` | R5 | H5's post-merge sweep, H6 | group | G:check-tests | ≈ −1,000/+80 t |
| H8a | `Bodies` and `Issues` residue → MIR dumps, problems or deletion | R5 (R4 reviews) | H5, H10a, Q18 or H2 | group | G:check-tests | ≈ −900 t |
| H8b | 55 control-flow entry calls and 14 lint reads → lint observations; ratchet 0 | R5 (BU reviews) | H5, Q9–Q12 | group | G:check-tests | ≈ −700 t |
| H8c | Analysis-declared enums and old-type names in tests | R5 (R4 reviews) | H5 | group | G:check-tests | ≈ −1,100 t |
| H9 | Benchmark successors; `BuildMir` split at stage 3/4 | R5 | H5 | group | G:check-del | ≈ −350/+30 |
| H10b | Delete `C/Analysis/` (50 files), its readers and the shadow call | R5 | H8a–c, H9, H6, H10a | group | G:check-del | ≈ −16,300 |
| H11 | Old-Analysis-only Binding members; the `src/Kimi` zero-reference row | BU | H10b, Q-c2 (R4-13) | group | G:check-del | ≈ −700 |
| H12 | Remove the pipeline switch, `[mir]`, `-Pipeline` | R5 | H10a, H10b | group | G:check-del | ≈ −600 |
| H14 | Forwarding-property reads (Parsing, root helpers, Mir, Backend); 37 retags to R7 (R5-M9) | R5 (BU reviews) | H10b, H11 | group | G:check-del | ≈ −150 |
| H7 | Binding's wide-division gate (`Binding.Operators.cs:331-334`) | BU | H5, K9a, Q-c1 (BU-4) | group | G:check-del | −4 |
| H13 | Confirmation table of Mir/Build syntax-class sites (doc) | R5 | none | R | — | 0 |
| H15 | Exit: records, PLAN10-P3 record, owner's review of exceptions | R5 | H6–H14, OQ15 | final review | — | docs |

## 5. Critical path

R4's exit (A6 2g-D–2g-7, A-4) → H3-1 → H3-2 (Q8-6 landed) → H3b rounds (H4, H1a landed; H1, Q8-3, H5 on `claude/r6-flip`; Q14 before H1's
code) → no open class 2 or 5 row → the window (peers' freeze, H0b §7) → G:R6-check-flip's merge → the post-merge sweep → H6 → H10a →
H8a → H8b → H8c → H9 → H10b → H11 → H12 → H14 → H15. Off the path: H1a, Q8-6, H4 (before H3b), H7 (after H5 and G157 (1)), H13 (any
time); H12 needs only H10a and H10b. Not on dev today: H1a, Q8-6, Q8-3, Q14's evidence; `VerifiedSpecExampleTest.Landed` lacks A2,
A5's F6–F8 and F10 (R4's exit adds them).

## 6. Facts that differ from the plan or a record

| # | Plan or record | Base | Proposed record |
| --- | --- | --- | --- |
| F1 | OldRule traits 19 (plan H5, M12) | 25 in 13 test files (verified) | H5 deletes all; H0's DEC entry names the count; M12 re-counted at H5 |
| F2 | spec-cases Old sides 22 (plan H5, M12) | 34 Old, 14 Mir in `tests/diagnostics/spec-cases.json` (verified); pair-cases 1 | as F1 |
| F3 | `EffectSummary` ≈ 1,985; H5 ≈ −2,700 | `Binding.EffectBounds.cs` 1,421 lines (verified; R4-M106); H5 ≈ −2,350/+70 | H5's commit (Q5 net size) |
| F4 | H8c: 114 methods, 52 files | the Analysis-declared enums occur in 10 test files (not reproduced) | H8c recounts at its start and splits by file family |
| F5 | H7's gate `Binding.Expressions.cs:1382-1386` | `Binding.Operators.cs:331-334` (verified) | D-c7; H7's commit |
| F6 | PD30: R4-M51's `Unsupported_Kd` is A4/A5's code for G128 | R4-M153 gave A5's `CallRetention.Limit` `OwnershipDepthLimit_Kd` (`MirBorrows.Reports.cs:108`) | H0a-11 (A) supersedes PD30's single-owner clause in H0's DEC entry |
| F7 | PLAN G128 row: A5 reports a located `Unsupported_Kd` (R4-M51) | `OwnershipDepthLimit_Kd` (R4-M153) | H3-2 corrects the row (Auto, PLAN); PLAN G313's "H5's merge decides" follows H0a-11 for its G128 part |
| F8 | Plan H0/H3: requirement derived from code and facts with Q17's table; a P10 fallback | void since R5-M23 (R4-M67); no fallback producer (H0a-9) | H0's DEC entry; H3-2's entry |
| F9 | Plan H3 ±200; H4 +60/−40 with records publishing from H5 | H3 ≈ +280/−60 (fact table, keys); H4 test-level, nothing to publish (H0b-8) | H3-2's and H4's commits; H0's DEC entry |
| F10 | Plan H10 one unit; `DeferredExpansionLimit_Kd` and G186's old walk at H11; `ComparisonPlan(CallPlan)` "until R7" | H10a/H10b (D-c2); H10b; H5; H11 (D-c8) | H0's DEC entry |
| F11 | Q8: the renderer's old caller until H11 | until H5 (ruling R4) | H0's DEC entry (supersedes Q8's line) |
| F12 | Line drift: `Project.cs:510`, `MirDifferenceLedger.cs:44, 279`, A3 gates `Binding.Diagnostics.cs:985`/`TypeOrigins.cs:182-192`, `TestCatalog.cs:108` | `:550-554` (verified), `.ComparedSet.cs:21, 160`, `Binding.OriginFits.cs:96-100`/`TypeOrigins.cs:187-198`, `:122` (verified) | each unit's commit (D-c7) |
| F13 | Counts: H8a 28 calls; H8b 64 calls/16 files; M12 `Ownership.Issues` 15; H9 12 files; H12 6 declarations, ≈ −300; H14 36 properties; CODEMAP ≈ 65 | 29; 55/15 (ratchet 64); 16 in 10 files; 10 + `Program.cs`; 5 Transitional + `CompilerIdentity`, ≈ −600; 37; 50 | each unit recounts at its start |
| F14 | PLAN G164, G203 (and H0c's G213, G218, G268, G291): "acceptance at H3" | H3 publishes under mir only; the default verdict moves at H5 (H0a-1) | H3-2 rewords those rows (Auto, PLAN) |
| F15 | ARCH's D5 exception list | lacks R4-M159's G273 stop (PLAN `:36` and the code tags record it) | owner question O-1 |
| F16 | Transitional census | 51 `("R6")` and 13 `("R6-check")` in `src` (verified), none in tests | H15's grep (exit gate item 3) |

## 7. Consistency across the parts

Resolved by the rulings: R1 (H0a-2 against H0b-4), R2 (H0c's `:84` at H10b against H0a/H0b at H5), R3 (H6's position), R4
(`FallthroughKey` at H10b against H5; Q8-6's old caller until H5 follows). Resolved by integration (marked in the parts):
- I1 Q8-6: H0a's "H1's first commit" and H0b's "on dev before the group" agree: Q8 lets H1's first commit land early; it lands before H3-2.
- I2 H5's Backend gate (`FunctionCodegen.cs:90-101`): H0b named R4, H0a-13 gives the Backend files to R5; R5 takes it, R4 keeps `Incomplete`.
- I3 `ControlFlowAnalysis.ReportDiagnostics` has one test reader, `T/OptionalTryDiscardTest.cs:246`; H5's test hunk re-points it (ahead of H8b).
- I4 H6 between H5 and H10a: it follows the post-merge sweep, and a rollback after H6 also reverts H6 (H0b §2.5).
- I5 H0c §5.3's "H3" rows: H3 publishes under mir only; default verdicts move at H5, matching H0b §5.4's known rows (G218, G268, G291).
- I6 H0a's "deleted (H5, H10)" for old issues: unpublished shadow from H5, code deleted at H10b (D-c1, D-c2).
Unresolved: none between the parts. Same unit names and order in all three. Questions that decide an owner or an option, not a
contradiction, stay in §8 (R4-1, R4-10, R4-12, BU-3).

## 8. Open questions by addressee (deduplicated; source in brackets)

| # | To | Question |
| --- | --- | --- |
| R4-1 | R4 | Do you accept `MirReport.Records` owning the merge across bodies, superseding R4-M71's "at publication" placement? [H0a R4-1] |
| R4-2 | R4 | May H3-1 move `MirReport`'s reset from `MirAnalysis.Run` (`:69`) to `MirProgram.Clear`? [H0a R4-2] |
| R4-3 | R4 | Is "Ownership requirement, `NoBody`, not `InternalInvariant_Kd`" A4's set by contract, stated in `MirProblem`'s invariants? [H0a R4-3] |
| R4-4 | R4 | Will 2g-1's Binding cause (Cause, Value −1, site = the Binding-failed node) be the only Binding-cause form H3 resolves? [H0a R4-4] |
| R4-5 | R4 | Will A5's problems at Binding's twin rows (R4-M111) and G353's refused stored Type carry a Binding cause, or stay two records to H11? [H0a R4-5] |
| R4-6 | R4 | Does one concrete call past G128's bound give both `StorageNesting` and `OwnershipDepthLimit_Kd` (a reproducer for H3)? [H0a R4-6] |
| R4-7 | R4 | Does the C1-F4 row a `Relation` fact names give `source` and `destination` without reading syntax? [H0a R4-7] |
| R4-8 | R4 | After H5 deletes gate step 2, does `MirDropTable.Incomplete` stay as A2's contract or go in H5's R4 hunk? [H0b R4-1] |
| R4-9 | R4 | Does every obligation the old `ReportUnprovenOriginObligations` rejects have a Binding or A5 judge? [H0b R4-2] |
| R4-10 | R4 | Do you accept H4 as test-level (A) with four `Row: None` Array rows, after your exit and before the rehearsal? [H0b R4-3] |
| R4-11 | R4 | Will A-4 bring the OldRule ratchet (7) to 0 and `VerifiedSpecExampleTest.Landed` to every family by your exit? [H0b R4-4] |
| R4-12 | R4 | Who implements Q8-3 inside the group: BU with your review, or you? [H0b R4-5] |
| R4-13 | R4 | Does A-12 land before H11, so `RequirementImplementation` goes then (else the owner approves its retag to R7)? [Q-c2] |
| R4-14 | R4 | G121's lost `do` fallthrough record (2x-P10): fixed before H5, or a classified H3b row? [Q-c4] |
| BU-1 | BU | Will H1a and Q8-6 (taking a body's binders and view bits) land before H3-2, and Q14 run before H1's code? [H0a BU-1, H0b BU-1] |
| BU-2 | BU | Are `Binding.FailureCauses(node)` keys stable at `ReportAnalysis` time for an Opaque site and a twin node? [H0a BU-2] |
| BU-3 | BU | Do you author H5's Binding hunk (≈ −1,830) with R5's review? [H0b BU-2] |
| BU-4 | BU | Fix G157 (1) before H7, or keep the wide-division gate until it is fixed? [Q-c1] |
| O-1 | owner | Add R4-M159's G273 stop to ARCH's D5 exception list in the next ARCH edit (recommended: yes)? [Q-c3] |

Counts: R4 14, BU 4, owner 1. The owner's one look under OQ11 (approving H0 with §10's supersessions) is the request, not a question.

## 9. Not done

- No build, test, probe or run: Q14, Q19's second reading, R4-6's reproducer, the mir census and H3b's class counts are H3b's inputs.
- No design review of H0 yet (FP: a reviewer who wrote none of the parts, before the owner's look).
- H8a–c's per-file split and F4's recount; H13's site-by-site table; H14's exact counts; `Binding.CallableEffects.cs`' exact ranges.
- PLAN not edited (F7, F14 and the gap issue of H0a-10 are proposed records); STATUS rows and per-row clauses follow from H3b's run.
- PLAN10 §5.6.3 against SPEC §14 clause by clause (H0c §6 row 3). No SPEC or IMPL change is proposed.

## 10. DEC entry (recorded after the owner's approval, OQ11)

"R5-M… R6-check's design H0 (OQ11; the owner, <date>). `draft/Sketches/R6-check/H0-design.md` with parts H0a, H0b and H0c is the
owner-approved design for H1–H5 and the order of H6–H15. Groups: G:R6-check-flip (H1 → Q8-3 → H5, one branch, one group review, one
merge, after H1a, Q8-6, H3 and H4), G:check-tests (H10a, H8a–c), G:check-del (H7, H9, H10b, H11, H12, H14). H3 publishes MIR problems
under mir through Core's publication table and `MirReport.Records` (A4 held, the old effect tail until H5); H5 makes the MIR decide on
every pipeline with the old analysis as an unpublished shadow until H10b; H3b is the group branch's first run, classified in five Q13
classes, class 5 stopping and class 2 blocking H5; H6 right after H5; H10 split into H10a and H10b. Rulings R1–R4. Supersedes: R5-M156
under mir from H3 (restored at H5); R4-M71's merge placement (if R4 accepts, R4-1); PD30's single owner (H0a-11 (A)); the plan rows'
derived requirement and P10 fallback; plan H4's published records; plan H10 as one unit; Q8's 'until H11' for the renderer's old caller;
PD12's refusal and R5-M96's interim at H5. H3-2 and H5 write their own entries (H0a §10, H0b §11). Rejected: the parts' rejected options."
