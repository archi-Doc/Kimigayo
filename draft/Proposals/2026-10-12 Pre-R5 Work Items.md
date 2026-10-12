# Pre-R5 Work Items

Date: 2026-10-12. Status: adopted by the owner on 2026-10-12 (decisions in §1 and §2; the rest as recommended). Work list for the interval between the R4 exit and the R5 start of the [compiler reduction plan](<2026-10-10 Compiler Size Reduction and Single MIR Plan.md>) ("the plan"); no item is started. No SPEC change. Items that can wait until the plan completes are in [Post-Reduction Work Items](<2026-10-12 Post-Reduction Work Items.md>) ("the companion"), whose Appendix A records the disposition of every R1 inventory item.

Base: `fcb190f4` (`dev`, R4 U1). `file:line` refers to that commit.

## 1. Scope

- **In scope:** carry-overs from R0–R3 that no later stage owns and that R5 needs as input, that cost less before R5 than during the two-generator period (R5–R6), or whose records would otherwise be lost. Most come from R1, which closed early on 2026-10-10 at 129,482 lines (ceiling 127,000).
- **Out of scope:** work the plan or the stage ledger assigns to R5–R9 (for example the measurement hooks at R6/R7, the library `#Unsupported` mechanism at R8, the old-path gates and `DeferredExpansionLimit_Kd` at R6, and the recovery of the D2 regressions); R4's own exit work (U12); PLAN §7 issues and P34_REMAINING, except where an item names them.
- **R4 exit:** carry-overs that R4's exit records (duplication candidates, ledger rows) and that must precede R5 are added here then.
- **Gate (owner, 2026-10-12):** §2 is an R5 start condition beside the plan's (R4 gates; §16-11, decided 2026-10-10). §3–§5 are adopted as recommended.

## 2. R5 comparison baselines (required)

R5 exits only if O2 run time stays within the variation band of the old generator and the O0 change is recorded (plan §12.2 R5, §13.3, §18 P1 and P4). R0 deferred these baselines to before R5 (COMPILER_ARCHITECTURE stage ledger, row R0), and none exists: the latest native timing runs in `artifacts/benchmarks/` predate the plan (2026-10-01 to 10-08). Take them on the R4 exit commit, before R5 changes shared code: R5 moves the IR helpers behind `LegacyRuntimeHelpers` and calls them from the new generator.

| ID | Work |
| --- | --- |
| B1 | **Native O2/O0 timing (legacy).** `src/backend/windows-x64/benchmark-arithmetic.ps1` (10 inputs), `benchmark-object-finalization.ps1 -Name r5-base-<n>`, `benchmark-p42.ps1 -Runs 5 -Name r5-base-<n>`, and the ExclusiveViews and SliceCopies runs of [Views.md](../../src/Benchmark/Views.md) (`-Runs 7`). Run the set three times as separate invocations; the range of their medians is the P4 band. |
| B2 | **Hot-path coverage.** Nothing times Array, Dictionary, string or Writer/interpolation hot paths at O2, though R5 and R8 change exactly these. Add Kimigayo sources under `src/Benchmark/Kimi/` (Array append, index and iteration; Dictionary insert, lookup, iteration and removal; string comparison and slicing; owning interpolation and Writer output) that print a checksum, and run them like B1. Restore the 12 cases of [RawPointers.md](../../src/Benchmark/RawPointers.md): its `bench.ps1` exists neither in the repository nor on disk. |
| B3 | **Pipeline stages (legacy).** `--compiler-pipeline-check` and `--compiler-pipeline` ([CompilerPipeline.md](../../src/Benchmark/CompilerPipeline.md)); EmissionLowering + LlvmIrWriting are the legacy counterpart of R5's monomorphization and codegen. Add opt/llc wall time at O0 and O2 with the `NativeToolchain` arguments, and the real workloads proposed in R0 (Milestone24, 25 and 37, PairCaseFamilies, InspectionLoans, GenericDefaults, ElementPlaces, SharedClone, AdaptationWorkloads). Record only (P3). |
| B4 | **Native allocation counts.** Name the R4 exit Session as the baseline of the `NativeAllocationAudit` classes, `DynamicArrayCostTest` and `Utf8ConsoleTest` (P2); no new measurement. |
| B5 | **Reproducible commands.** VERIFICATION's measurement table lacks `--callable-plans`, `--local-regions`, `--fixed-origin-loans`, `--property-plans`, `--diagnostics` and `--named-arguments`; add them and B1–B3's commands. |

Conditions: one compiler commit for every run, fixed conditions as in VERIFICATION, no other process running. Store the results with `commit.txt`, `status.txt` and `dotnet-info.txt` in `artifacts/benchmarks/reduction-R5-base/`, keeping failed runs. Then remove "(timing baselines carried to before R5)" from the ledger row R0.

**Owner decision (2026-10-12):** B2's new benchmarks join the P1 gate from R5: their O2 run time may not regress beyond the P4 band, as for B1. Reason: R5 changes how these helpers are called (calling convention, inlining). Rejected: recording them only until R8's measurement gate (plan §5.8).

## 3. Keep known wrong code out of the R5 reference (adopted)

R5 compares the new generator's fixture and milestone output with the old one's, and R4's ledger classifies analysis differences. [P34_REMAINING](../../docs/dev/P34_REMAINING.md) records accepted programs that the old generator miscompiles (severity W). No fixture covers them, so no comparison would notice if the new path repeated them:

- R1: inherited (BaseBorrow) receivers reached through reference layers read the wrong address.
- R2: a string comparison whose operand's owner is moved inside the comparison reads freed memory. SPEC 13.4 makes the operand an inspection Loan, so the program is invalid.

Work: write each reproducer with its SPEC result. For R1 that is the expected output; for R2 it is rejection, which A5 should report in R4 (confirm in the ledger). Record each as an (a) row with its reproducer. The unit that makes the new path correct (R4 A5, R5 codegen) turns it into a passing test. Do not fix the old path: the plan allows miscompile fixes there (§16-14), but R6 deletes it.

## 4. G87 before the two-generator period (adopted)

PLAN §7 G87: `Kimi.Result<T, E>` lacks the conditional Copy clause of SPEC §22.1, so a second use of `Result<i32, i32>` reports `TransferRequired_Kd`. The fix changes ownership plans and IR:

- Before R5, it touches one generator plus the MIR analyses.
- During R5–R6, it touches both generators and R5's comparison baseline.
- After R6, it touches one generator again.

Do it before R5 as its own library unit: the clause, a `KimiLibraryShapeTest` row for the Copy set, and a regression that fails before the fix. Otherwise leave it until after R6. The same timing applies to any other library or Binding fix that changes generated IR.

## 5. Records and documents

| ID | Work |
| --- | --- |
| D1 | **PLAN §3 carry-over line.** Only C3h waits for R8; C3a–C3g, C4, `self.helper()` and `S.v` are in the companion. "One case limit" is done (R4 U0a, DECISIONS R4-M18). Replace the R1 list with links to these two documents, and replace the stale "R4 remains unstarted" and pause wording. Do this in R4 U12's PLAN update. |
| D2 | **STATUS stale rows.** STATUS.md:195 (entry of 2026-10-01) still says Program 40 "shows the remaining limit", although P40 is DONE (2026-10-04; STATUS.md:167). STATUS.md:364 cites "PLAN G40", which PLAN no longer lists (the R1 inventory records the fix in `62c828f1`); confirm before rewording. |
| D3 | **Back up Git-ignored records that R5–R9 rely on** (AGENTS, Artifacts). `artifacts/verify/reduction-R0/` holds `disposition.md` (the per-file stage of every compiler file), `narrow-*.md` (the N-ids cited by STATUS, PLAN and R4 decision O4), `benchmark-classification.md` and `advice-note-audit.md`. Also back up `artifacts/handoff/r3/` and `r4/` (unit records and decisions not yet in DECISIONS) and the raw ledgers under `artifacts/verify/mir-diff/`. The R1 working inventory exists only in a previous session's temporary folder; the companion's Appendix A keeps its dispositions. |

## 6. Order and completion

1. D1 and D2 with R4 U12's documentation commit (documentation only).
2. B2 and B5 (Benchmark change: Session including the whole-solution build). Then B1 and B3 on the R4 exit commit, and B4; then D3.
3. §3 and §4 as separate units with their unit verification.

Done when the B1–B4 results and the band are stored, the ledger row R0 is updated, PLAN links these documents, and §3 and §4 are done. Moving §3 or §4 out needs a new owner decision.
