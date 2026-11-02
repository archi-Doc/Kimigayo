# R6-check H0: the design package (index)

Status: H0's one design (plan §4.10 row H0, FP), revision 1 after the design review (REVISE; every sampled fact correct): the index of [H0a](H0a-publication.md) (the publisher, H3), [H0b](H0b-flip.md) (H1, H1b, H4, H5 with H6, H3b), [H0c](H0c-deletions.md) (H7–H15, exit). Applied in the parts and marked: the planning session's rulings
R1–R4 ("(ruling Rn)"), the integration's edits ("(integration)") and the planning session's rulings on the review's findings ("(review Bn/Nn)").
Working record, not authority: it becomes authority only as the DEC entry of §10 after the owner's approval under OQ11 (DECISIONS R5-M1).

Working time (WORKFLOW "Working-time limit", DECISIONS Q12): integration 2026-11-02 18:23–18:36; revision 1 18:47–19:01; re-review fixes ≈ 10 min.

Base: dev `1d23d559d` (the parts read `1ec539c79` and `e8ba140b3`; only PLAN differs). "Plan" is the
[R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` = `src/Kimi/Compiler/`,
`T/` = `tests/xUnitTest/Tests/`. No build, test or probe was run; the counts in §6 marked "verified" were re-grepped on the base.

## 1. Purpose and the owner's words

H0 designs R6-check: one publisher of MIR records (H3, no published change), the acceptance flip on every pipeline with the old effect
checks deleted (H5, PD29) together with TransferRequired and the unproven obligations in Binding (H1, H1b) and A4's row checks (H4), the
rehearsal and its classification (H3b), the deletion order of the old Analysis and its readers (H7–H15), the census of what ends at
R6-check, the check-only SPEC/IMPL pass and the exit gate (plan §5.5). The owner approved the division of work on 2026-11-02 and told R5:
「その役割分担でOKです。H0の設計から始めて下さい。終わったら、R4の完了を待ってください。」 H0 is designed during R4; its units run after R4's exit.

## 2. Division of work (the owner, 2026-11-02)

| Session | Owns in R6-check |
| --- | --- |
| R5 (lead) | H0, Q19's second reading with R4, the H3b rehearsal and classification, H5's Core side (the Backend gate, I2; the `C/Analysis` hunk, N3; hover, H6), H8–H10, H12, H13, H15, the windows; Core's publisher (H3-2); H14 (H0c) |
| R4 | The MIR side: H3-1's Mir files and `MirReport.Records`, H4, asks A-4/A-5, MIR false rejections (H3b class 2 MIR rows), the `Incomplete` contract (N2) |
| BU | The Binding side: H1a, Q8-6, Q14, H1, H1b (B2), Q8-3 (H, R4-11), H5's Binding hunk (BU-3), the review of H4's Binding edits (N3), H7, H11, the end of the old-analysis D5 exceptions, Binding-cause rows |

## 3. Decisions (one line each; § of the part)

| # | Decision |
| --- | --- |
| H0a-1 | One publisher in Core maps each `MirReport` record to the adapter; H3-2 lands it with lowest-layer tests; until H5 it publishes today's rows only, unchanged on every pipeline; from H5 every record on every pipeline; no pipeline column (review B1) (§2) |
| H0a-2 | Removed (review B1): no mir acceptance before H5; acceptance from the published rows is H5's (H0b-3, ruling R1) |
| H0a-3 | H3-1 (bp) folds construction and collector records into `MirReport` as `NoBody` `ControlFlow` problems; `MirReport` resets in `MirProgram.Clear`; validator findings stay `Findings` (§3.1) |
| H0a-4 | The merge across bodies by (code, subject, Reason) is `MirReport.Records` (R4); Core only maps records to the adapter (§3.2) |
| H0a-5 | A record's requirement is its problems' own (R4-M67, R5-M23); no code → requirement table; partition = requirement's (§3.3) |
| H0a-6 | Keys: a record with facts at its subject node, condition = its Reason's ordinal; a location-only code by `ReportLocation` (§3.4) |
| H0a-7 | One fact table, one row per `MirFactKind`; case evidence and Note through Q8-6's renderer; a kind without a row is `InternalInvariant_Kd` (§4.1) |
| H0a-8 | A record with `Cause` facts publishes as `PrerequisiteUnavailable_Kd`, `DerivedFrom` its causes' keys or `Binding.FailureCauses` for Value −1 (§4.2) |
| H0a-9 | No new PLAN10 P10 fallback producer: every MIR incompleteness already has a record (§4.3) |
| H0a-10 | Effects (PD29): no MIR analysis problem publishes before H5, so no A4 hold and no old-analysis edit; A4's problems publish at H5, which deletes the old effect checks (review B1) (§5.1) |
| H0a-11 | G128 (PD30): (A) no rule; the collector's `Unsupported_Kd` and A5's `OwnershipDepthLimit_Kd` at one call both stay, counted, until R7 (§5.2) |
| H0a-12 | Independence: only records with MIR or Binding `Cause` facts are dependent (§6) |
| H0a-13 | Owners: R4 the Mir files and `MirReport.Records`, R5 the Backend and Core files and the publisher, BU Q8-6's renderer and `FailureCauses` (§7) |
| H0b-1 | G:R6-check-flip = H1 → H1b → Q8-3 → H5 (with H6): one branch, one group review, one merge; H1a, Q8-6 and H4 land on dev first (§2, §3, §8) |
| H0b-2 | H3b is the group branch's first full run, not a throwaway; its fixes land on dev; rebase and re-run until no class 2 or 5 row (§5) |
| H0b-3 | H5's Core: `Analyze` invalidates both partitions and builds the MIR, no old analysis; it runs only through `Compilation.AnalyzeLegacyOwnership()` (`[Transitional("R6-check")]`, removal H10b) for its readers (review B3) (§2.1) |
| H0b-4 | No `LintErrors` term from H5 (a lint Error rejects through `Accepts`); `AnalysisResult.BodyCount` deleted (§2.1) |
| H0b-5 | `Project.cs:550-554` needs no edit (§2.1) |
| H0b-6 | Generation's gate is `!Accepts`; step 2's refusal becomes a contract: an `Incomplete` body implies a published Error (R4-M69), so one reaching generation is `InternalInvariant_Kd`; `Group == -1` stays (review N2) (§2.1) |
| H0b-7 | H5 deletes both old `ReportDiagnostics` and `FallthroughKey` (ruling R4); one test reader re-pointed (I3) (§2.2) |
| H0b-8 | H4 is test-level (A): A4's summary of a row operation replaces `SummarizePublishedOperation`; four Array rows `Row: None`; `Binding.PublishedOperations.cs` deleted (§4) |
| H0b-9 | Five classes under Q13, pre-classified by fixed maps; Session, snapshot and milestones decide, the ledger proposes (§5.3, §5.4) |
| H0b-10 | Stop rule: class 5 stops H5; class 2 blocks until fixed on dev or accepted as one owner list; a repeat in a fixed family goes one level higher (§5.5) |
| H0b-11 | H6 (hover from MIR) is part of H5 (review B3, superseding ruling R3) (§2.1) |
| H0b-12 | Rollback: revert the group's merge alone until H10a; H10a, P42's remainder, H7, H8b and H8c wait for the post-merge sweep (review N1) (§2.5) |
| H0b-13 | H1b (BU, in the group): Binding's verdict at Final, or an `InternalInvariant_Kd` contract, for each obligation the old reporter gives `UnprovenConstraint_Kd`; a successor for each old CFA pending site (review B2) (§3) |
| H0b-14 | H3b's table gives each row its class and its exit-gate category (a)–(e) by a fixed map (review N4) (§5.3) |
| D-c1 | From H5 to H10b the old analysis runs only when a test or benchmark reader calls the transitional entry; never in check or the editor (review B3) |
| D-c2 | H10 splits: H10a (ledger harness, `Analyzed`, `KIMI_MIR_DIFF`, `IsTwinRecord`) after H5's sweep; H10b (the old Analysis and the entry) after H8a–c, H9, H10a |
| D-c3 | Order: H5 (with H6) → sweep → H10a → H8a → H8b → H8c → H9 → H10b → H11 → H12 → H14; P42, H7, H8b, H8c held until the sweep (N1); H13 any time; H15 last |
| D-c4 | Zero-reference rows in two steps: tests, `src/Benchmark`, `src/Playground` at H10b; `src/Kimi` at H11 |
| D-c5 | Stage 4 keeps `ControlFlowAndOwnership` and times `MirAnalysis.Run` plus collection (no old analysis from H5, B3); H9 splits `BuildMir` at stage 3/4 (bp first) |
| D-c6 | H14 also rewrites Mir's and the Backend's forwarding-property reads (R5-M9 postponed only Binding's) |
| D-c7 | Record corrections: H7's site, H11's A3 gates, H5's ledger filter, `ReportAnalysis`'s `:84` (stops at H5, ruling R2), G186's old walk at H5 |
| D-c8 | `ComparisonPlan(CallPlan)` and `DictionaryComparison` go at H11 (their compiler readers die with `EffectSummary` at H5) |
| R1 | H0a-2's `Binding.LintErrors` term is dropped (H0b-4: lint errors already reject through `Accepts`) |
| R2 | The old control-flow report (`Compilation.Pipeline.cs:84`) stops at H5; its code goes when its last caller goes |
| R3 | H6 right after H5 (superseded by B3: H6 is part of H5) |
| R4 | `FallthroughKey` and both old `ReportDiagnostics` go at H5, having lost their only caller |
| B1 | The mir column between H3 and H5 is dropped: H3-2 lands the publisher, default unchanged; every pipeline flips at H5 |
| B2 | H1b, a BU unit and H5 prerequisite, gives the unproven non-relation obligations a Binding verdict; each old report names its successor (H0b §2.3) |
| B3 | The old analysis leaves `Compilation.Analyze` at H5; one transitional entry serves its readers until H10b (`Compilation.Ownership` private, re-review 3); hover's line moves into H5 |
| N1 | P42's remainder, H7, H8b, H8c and H10a wait for H5's post-merge sweep; H0c's prerequisites follow D-c3's order |
| N2 | Gate step 2's deletion states its contract: an `Incomplete` body implies a published Error, checked as `InternalInvariant_Kd` |
| N3 | H5's `C/Analysis` hunk (≈ −380) is R5's; BU reviews H4's Binding edits |
| N4 | H3b's classes map to the exit gate's categories (a)–(e) (H0b §5.3; §10) |
| N5 | Q19's second reading (a plan prerequisite of H0, not met) is an R4/R5 item at R4's exit, before H3b |

## 4. Units

Groups: G:R6-check-flip (H1, H1b, Q8-3, H5 with H6), G:check-tests (H10a, H8a–c), G:check-del (H7, H9, H10b, H11, H12, H14). Sizes (H).

| Unit | Change | Owner | Prerequisites | Review | Group | Size |
| --- | --- | --- | --- | --- | --- | --- |
| H1a | Eager Final publication, `Check` split (bp; Q8 D8) | BU | G2 (done) | D+R (Q8) | — | ±70 |
| Q8-6 | One Semantics-case renderer in Binding: H1's first commit landing early (bp); the old caller until H5 | BU | before H3-2 | D+R (Q8) | — | ±60 |
| H3-1 | Fold construction and collector records into `MirReport` (bp) | R4 (Mir), R5 (Backend, Core) | R4's exit, OQ11 | R | — | +30/−45, t ±60 |
| H3-2 | Core's publisher (keys, fact table, cause resolver) and `MirReport.Records`, lowest-layer tests; no published change (B1) | R4 (`MirReport.Records`), R5 (Core) | H3-1, R4's exit with A6 (2g-D–2g-7), G11, Q17, Q8-6, OQ11 | D+R under H0, one review | — | ≈ +220/−10, t +250 |
| H4 | A4's summary replaces `SummarizePublishedOperation` (test-level); four Array `Row: None` | R4 (BU reviews Binding edits, N3) | R4's exit, OQ11 | R (D+R under H0) | — | +60/−50, t ±60 |
| Q19 | Second ledger reading mapped to tests and snapshot rows (N5) | R4 with R5 | R4's exit (A5-F6, A6) | — | — | 0 (Evid) |
| Q14 | Q8's probe, evidence in `artifacts/verify/r6-q14/` | BU | H1a; Q8-3's rule as a probe | — | — | 0 |
| H3b | Rehearsal on `claude/r6-flip` (H1, H1b, Q8-3, H5 as implemented) and the classification table (Doc) | R5 (R4, BU confirm rows) | H3, H4, Q19, H1a, Q8-6; drafts of H1, H1b, Q8-3, H5 | R | — | 0 (Evid + Doc) |
| H1 | TransferRequired judged in Binding (Q8 §3.7); the 10 old sites and `Ownership.TransferRequired` deleted | BU | H1a, Q8-6, Q14, OQ11 | group (one reviewer, BU dispatches) | G:R6-check-flip | +110/−75, t +150 |
| H1b | Binding's verdict at Final (or an `InternalInvariant_Kd` contract) for the unproven non-relation obligations; the 9 pending sites' successors (B2, re-review 2) | BU | H1a, OQ11 | group | G:R6-check-flip | +60, t +60 |
| Q8-3 | `MirValidator`'s Copy rule (Q8 §3.9) | BU (H, R4-11) with R4's review | H1 | group | G:R6-check-flip | +30, t +40 |
| H5 | The flip on every pipeline; the transitional entry (B3), `Compilation.Ownership` private (re-review 3); hover (H6); the `Incomplete` contract (N2); old effect checks, both old `ReportDiagnostics`, `FallthroughKey`, PD12's refusal, OldRule traits, Old sides deleted | R5 (Core, Backend gate, `C/Analysis`, hover, tests), BU (Binding hunk), R4 (`Incomplete` contract) | H1, H1b, Q8-3, H3b (no open class 2/5 row), H4, Q9–Q12, Q20/A-4, A-7, Q18 or H2 | FP (H0) + group + H15 | G:R6-check-flip | ≈ −2,350/+90, t −250/+210 |
| H2 | (Conditional) Problem Loan facts, only if Q18 has not landed through A6; not designed by H0 (plan row) | R4 | Q18 not landed | D+R (A3 §6) | — | +100 |
| H6 | Hover's "Ownership checks" line from MIR problems: part of H5 (B3) | R5 | (H5's) | (H5's) | G:R6-check-flip | ±20 |
| H10a | Ledger harness, `Analyzed`, `KIMI_MIR_DIFF`, `IsTwinRecord`; MIR helpers into `CompilationTestHelper` | R5 | H5's post-merge sweep (N1) | group | G:check-tests | ≈ −1,000/+80 t |
| H8a | `Bodies` and `Issues` residue (read through the entry from H5) → MIR dumps, problems or deletion | R5 (R4 reviews) | H5, H10a, Q18 or H2 | group | G:check-tests | ≈ −900 t |
| H8b | 55 control-flow entry calls and 14 lint reads → lint observations; ratchet 0 | R5 (BU reviews) | H8a, the sweep (N1), Q9–Q12 | group | G:check-tests | ≈ −700 t |
| H8c | Analysis-declared enums and old-type names in tests | R5 (R4 reviews) | H8b, the sweep (N1) | group | G:check-tests | ≈ −1,100 t |
| H9 | Benchmark successors (the old-analysis benchmarks call the entry until here); `BuildMir` split at stage 3/4 | R5 | the sweep; after H8c (D-c3) | group | G:check-del | ≈ −350/+30 |
| H10b | Delete `C/Analysis/` (50 files), its readers and the transitional entry | R5 | H8a–c, H9, H10a | group | G:check-del | ≈ −16,300 |
| H11 | Old-Analysis-only Binding members; the `src/Kimi` zero-reference row | BU | H10b, Q-c2 (R4-12) | group | G:check-del | ≈ −700 |
| H12 | Remove the pipeline switch, `[mir]`, `-Pipeline` | R5 | H10a, H10b | group | G:check-del | ≈ −600 |
| H14 | Forwarding-property reads (Parsing, root helpers, Mir, Backend); 37 retags to R7 (R5-M9) | R5 (BU reviews) | H10b, H11 | group | G:check-del | ≈ −150 |
| H7 | Binding's wide-division gate (`Binding.Operators.cs:331-334`) | BU | the sweep (N1), K9a, Q-c1 (BU-4) | group | G:check-del | −4 |
| H13 | Confirmation table of Mir/Build syntax-class sites (doc) | R5 | none | R | — | 0 |
| H15 | Exit: records, PLAN10-P3 record, owner's review of exceptions | R5 | H7–H14, OQ15 | final review | — | docs |

## 5. Critical path

R4's exit (A6 2g-D–2g-7, A-4) and Q19's second reading → H3-1 → H3-2 (Q8-6 landed) → H3b rounds (H4, H1a landed; H1, H1b, Q8-3, H5 on
`claude/r6-flip`; Q14 before H1's code) → no open class 2 or 5 row → the window (peers' freeze, H0b §7) → G:R6-check-flip's merge (H5 with
H6) → the post-merge sweep (held until then: P42's remainder, H7, H8b, H8c, H10a) → H10a → H8a → H8b → H8c → H9 → H10b → H11 → H12 → H14
→ H15. Off the path: H1a, Q8-6, H4 (before H3b), H7 (after the sweep and G157 (1)), H13 (any time); H12 needs only H10a and H10b. Not on
dev today: H1a, Q8-6, Q8-3, Q14's evidence, Q19's second reading; `VerifiedSpecExampleTest.Landed` lacks A2, A5's F6–F8 and F10.

## 6. Facts that differ from the plan or a record

| # | Plan or record | Base | Proposed record |
| --- | --- | --- | --- |
| F1 | OldRule traits 19; spec-cases Old sides 22 (plan H5, M12) | 25 traits in 13 test files; 34 Old, 14 Mir in `tests/diagnostics/spec-cases.json`; pair-cases 1 (verified) | H5 deletes all; H0's DEC entry names the counts; M12 re-counted at H5 |
| F3 | `EffectSummary` ≈ 1,985; H5 ≈ −2,700 | `Binding.EffectBounds.cs` 1,421 lines (verified; R4-M106); H5 ≈ −2,350/+90 | H5's commit (Q5 net size) |
| F4 | H8c: 114 methods, 52 files | the Analysis-declared enums occur in 10 test files (not reproduced) | H8c recounts at its start and splits by file family |
| F5 | H7's gate `Binding.Expressions.cs:1382-1386` | `Binding.Operators.cs:331-334` (verified) | D-c7; H7's commit |
| F6 | PD30: R4-M51's `Unsupported_Kd` is A4/A5's code for G128 | R4-M153: A5's `CallRetention.Limit` is `OwnershipDepthLimit_Kd` (`MirBorrows.Reports.cs:108`) | H0a-11 (A) supersedes PD30's single-owner clause |
| F7 | PLAN G128 row: A5 reports a located `Unsupported_Kd` (R4-M51) | `OwnershipDepthLimit_Kd` (R4-M153) | H3-2 corrects the row (Auto, PLAN); G313's G128 part follows H0a-11 |
| F8 | Plan H0/H3: requirement derived with Q17's table; a P10 fallback; H3 publishes under mir | void since R5-M23 (R4-M67); no fallback producer (H0a-9); no published change at H3 (B1) | H0's DEC entry; H3-2's entry |
| F9 | Plan H3 ±200; H4 +60/−40 with records publishing from H5 | H3 ≈ +250/−55 (fact table, keys); H4 test-level, nothing to publish (H0b-8) | H3-2's and H4's commits; H0's DEC entry |
| F10 | Plan H10 one unit; `DeferredExpansionLimit_Kd`, G186's old walk at H11; `ComparisonPlan(CallPlan)` "until R7"; H6 its own unit; Q8: the renderer's old caller until H11 | H10a/H10b (D-c2); H10b; H5; H11 (D-c8); H6 in H5 (B3); until H5 (ruling R4) | H0's DEC entry |
| F12 | Line drift: `Project.cs:510`, `MirDifferenceLedger.cs:44, 279`, A3 gates `Binding.Diagnostics.cs:985`/`TypeOrigins.cs:182-192`, `TestCatalog.cs:108` | `:550-554`, `.ComparedSet.cs:21, 160`, `Binding.OriginFits.cs:96-100`/`TypeOrigins.cs:187-198`, `:122` (verified) | each unit's commit (D-c7) |
| F13 | Counts: H8a 28; H8b 64/16 files; M12 `Issues` 15; H9 12 files; H12 6 declarations, ≈ −300; H14 36; CODEMAP ≈ 65 | 29; 55/15 (ratchet 64); 16 in 10 files; 10 + `Program.cs`; 5 + `CompilerIdentity`, ≈ −600; 37; 50 | each unit recounts at its start |
| F14 | PLAN G164, G203 (and G213, G218, G268, G291): "acceptance at H3" | every pipeline's verdict moves at H5 (B1) | H3-2 or H5 rewords those rows (Auto, PLAN) |
| F15 | ARCH's D5 exception list | lacks R4-M159's G273 stop (PLAN `:36` and the code tags record it) | owner question O-1 |
| F16 | Transitional census | 51 `("R6")` and 13 `("R6-check")` in `src` (verified), none in tests; H5 adds the entry | H15's grep (exit gate item 3) |
| F17 | Plan H0's prerequisite "Q19 second reading" | not taken (no evidence after A5-F6 and A6) | Q19 unit at R4's exit, before H3b (N5) |
| F18 | R4-M112's departure keeps `Same(Ownership(UnprovenOrigin), UnprovenConstraint_Kd, "A5")` "until R6-check", no successor | only the old reporter gives it (`OwnershipModel.cs:205-207`, verified); A5 never reports it; the ledger credits A5 (`ComparedSet.cs:51`, verified) | H1b is the successor; H5's DEC entry names it (B2) |
| F19 | Hover's line (plan H6 after H5) | reads `Ownership.TemplateBody` (`HoverBuilder.Virtual.cs:75`, verified), which runs the old analysis in the editor | moves into H5 (B3) |

## 7. Consistency across the parts

Resolved by the rulings R1–R4 and by integration (marked): I1 Q8-6 as H1's early first commit, before H3-2; I2 H5's Backend gate is R5's,
R4 keeps `Incomplete`; I3 `ControlFlowAnalysis.ReportDiagnostics`' one test reader (`T/OptionalTryDiscardTest.cs:246`) is re-pointed in
H5; I6 no old issue published from H5. I4 (H6 after H5) and I5 (H3's rows under mir) are superseded by B3 and B1. Correction: revision 0's
"Unresolved: none" was wrong. The review found that H0a's mir column had lost its consumer, since H0b-2 runs H3b with H5 applied (B1);
that H0b's "no successor" for `PendingBinding`, the `Unresolved` fallback and the non-relation obligations was unchecked (B2); and that
D-c1's shadow ran frozen code in every check (B3). All three are ruled and applied; nothing open between the parts beyond §8.

## 8. Open questions by addressee (deduplicated; source in brackets)

| # | To | Question |
| --- | --- | --- |
| R4-1 | R4 | Do you accept `MirReport.Records` owning the merge across bodies, superseding R4-M71's "at publication" placement? [H0a R4-1] |
| R4-2 | R4 | May H3-1 move `MirReport`'s reset from `MirAnalysis.Run` (`:69`) to `MirProgram.Clear`? [H0a R4-2] |
| R4-3 | R4 | Will 2g-1's Binding cause (Cause, Value −1, site = the Binding-failed node) be the only Binding-cause form H3 resolves? [H0a R4-4] |
| R4-4 | R4 | Will A5's problems at Binding's twin rows (R4-M111) and G353's refused stored Type carry a Binding cause, or stay two records to H11? [H0a R4-5] |
| R4-5 | R4 | Does one concrete call past G128's bound give both `StorageNesting` and `OwnershipDepthLimit_Kd` (a reproducer for H3)? [H0a R4-6] |
| R4-6 | R4 | Does the C1-F4 row a `Relation` fact names give `source` and `destination` without reading syntax? [H0a R4-7] |
| R4-7 | R4 | Do you accept "an `Incomplete` body implies a published Error" (R4-M69) as A2's contract, `Incomplete` staying as the input of generation's check? [H0b R4-1, N2] |
| R4-8 | R4 | Do you accept H4 as test-level (A) with four `Row: None` Array rows, after your exit and before the rehearsal? [H0b R4-3] |
| R4-9 | R4 | Will A-4 bring the OldRule ratchet (7) to 0 and `VerifiedSpecExampleTest.Landed` to every family by your exit? [H0b R4-4] |
| R4-10 | R4 | Will you take Q19's second reading with R5 at your exit, before H3b? [H0b R4-6, N5] |
| R4-11 | R4 | Who implements Q8-3 inside the group: BU with your review, or you? [H0b R4-5] |
| R4-12 | R4 | Does A-12 land before H11, so `RequirementImplementation` goes then (else the owner approves its retag to R7)? [Q-c2] |
| R4-13 | R4 | G121's lost `do` fallthrough record (2x-P10): fixed before H5, or a classified H3b row? [Q-c4] |
| BU-1 | BU | Will H1a and Q8-6 (taking a body's binders and view bits) land before H3-2, and Q14 run before H1's code? [H0a BU-1, H0b BU-1] |
| BU-2 | BU | Are `Binding.FailureCauses(node)` keys stable at `ReportAnalysis` time for an Opaque site and a twin node? [H0a BU-2] |
| BU-3 | BU | Do you author H5's Binding hunk (≈ −1,830) with R5's review, and review H4's Binding edits (N3)? [H0b BU-2] |
| BU-4 | BU | Fix G157 (1) before H7, or keep the wide-division gate until it is fixed? [Q-c1] |
| BU-5 | BU | Will H1b give every obligation kind the old reporter leaves unchecked a Binding verdict at Final, or a shown-unreachable `InternalInvariant_Kd` contract? [H0b BU-3, was R4-2; B2] |
| O-1 | owner | Add R4-M159's G273 stop to ARCH's D5 exception list in the next ARCH edit (recommended: yes)? [Q-c3] |
| O-2 | owner | H3b's class 1: 1a (newly accepted) reach you as one list; 1r (newly rejected, each closing a named old unsound acceptance with its clause, like G177) are the planning session's, listed to you for information. The 1a list includes programs past the old deferred-cleanup limit (8,192 operations, no MIR counterpart; re-review 1). Confirm? (Recommended: yes.) [H0b O-1] |

Counts: R4 13 (H0a's R4-3 withdrawn by B1; H0b's R4-2 moved to BU), BU 5, owner 2. OQ11's approval of H0 is the request, not a question.

## 9. Not done

- No build, test, probe or run: Q14, Q19, R4-5's reproducer, H1b's census and H3b's class counts are inputs of their units.
- Revision 1 was re-reviewed (ACCEPT); its three non-blocking findings are applied (re-review 1–3).
- H8a–c's per-file split and F4's recount; H13's site-by-site table; H14's exact counts; `Binding.CallableEffects.cs`' exact ranges.
- PLAN not edited (F7, F14 are proposed records); STATUS rows and per-row clauses follow from H3b's run. No SPEC or IMPL change proposed.

## 10. DEC entry (recorded after the owner's approval, OQ11)

"R5-M… R6-check's design H0 (OQ11; the owner, <date>). `draft/Sketches/R6-check/H0-design.md` with parts H0a, H0b and H0c is the
owner-approved design for H1–H5 and the order of H6–H15. Groups: G:R6-check-flip (H1 → H1b → Q8-3 → H5 with H6, one branch, one group
review, one merge, after H1a, Q8-6, H3 and H4), G:check-tests (H10a, H8a–c), G:check-del (H7, H9, H10b, H11, H12, H14). H3 lands one
publisher of MIR records with no published change; H5 makes the MIR decide and publish on every pipeline; from H5 the old analysis runs
only through `Compilation.AnalyzeLegacyOwnership()` for its test and benchmark readers until H10b; H1b gives Binding's verdict for the
unproven non-relation obligations; an `Incomplete` body implies a published Error. H3b is the group branch's first run: each row a Q13
class and an exit-gate category ((a) for 1r, 1a, 1x; (b) or, accepted by the owner, (e) for 2; (d) for 3 and 4; (b) for 5 until fixed;
(c) for a SPEC ambiguity, the owner's); class 5 stops and class 2 blocks H5; 1a reach the owner as one list, 1r are reported (O-2).
P42's remainder, H7, H8b, H8c and H10a wait for H5's post-merge sweep; H10 splits into H10a and H10b. Rulings R1–R4; review B1–B3,
N1–N5. Supersedes: R4-M71's merge placement (if R4 accepts); PD30's single owner (H0a-11 (A)); the plan rows' derived requirement, P10
fallback and mir publication at H3; plan H4's published records; plan H6 and H10 as units; Q8's 'until H11' for the renderer's old
caller; R4-M112's kept ledger row (successor H1b); PD12's refusal and R5-M96's interim at H5. H3-2 and H5 write their own entries."
