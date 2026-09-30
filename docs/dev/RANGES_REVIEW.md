# Ranges review checkpoint — 2026-09-30

The user requested an audit of the integration and implementation of the frozen proposal `draft/Changes/2026-09-29 Redesign Ranges B.md`, including nearby bugs, then requested an early stopping point, a written handoff and a commit. **The full audit is not complete.** Do not infer completion from the existing P41 milestone status. The starting revision was `3604fb43` on `dev`, with a clean worktree.

## Changes at this checkpoint

- **Value reads in branch results:** `Binding.ControlFlow` now chooses the terminal read Type for differing safe-reference layers over positions, ranges and constrained Type parameters. Literal result fitting can obtain explicit read-conversion evidence from the complete expression (`a@ref` infers its referent from `a`), restricted to an already bound operand or a known identifier. Ordinary borrow results wait for block-local declarations and all their Origins. `ControlFlowAnalysis` respects the resulting expected adaptation. Previously valid `if`/`match` results could fail Binding or leave pending flow obligations. Tests check copied snapshots, released borrows, nested references, generic Position/PositionRange and preservation of equal reference layers and their Loans.
- **Type-parameter identities:** `Binding.TypeIdentity` computes the finite equivalence class of available parameter identities instead of following one directed fact with a 16-step cutoff. `A is B; A is C` now makes B and C interchangeable. Constraint and capability proofs share facts across that class. Proof normalization deliberately keeps symbolic subjects symbolic; substituting concrete assumptions there changed the existing four-valued proof rules and was rejected by regression tests. A recursive structural expansion guard remains. Tests include premise order, cycles, 24 parameters, compound Types, zero-allocation warm rebinding and native generic integer-range iteration.
- **Literal position warnings:** `Binding.PositionWarnings` evaluates the bound integer width/sign, including 128-bit arithmetic, instead of always using i32. It suppresses resolution warnings for arithmetic that itself Aborts, and performs resolution inequalities in Int128 with values saturated outside the target isize domain. Reproducers include missing warnings for `values.tryGet<i64>(1 << 63)` and `values.tryGet<i8>(127 << 1)`, and the false warning for unsigned underflow `values.tryGet<u8>(0 - 1)`. Tests cover limits, overflow, division, shifts and zero-allocation warm rebinding.
- **Documentation:** SPEC §4.6.2 now excludes integer positions from the no-arithmetic/no-Comparable rule. Appendix A.13 names `tryGet`/`trySlice` correctly. `draft/INTEGRATED.md` records the subsequent API decision already made by `278a3091`: do not restore the proposal's two `tryGet` overloads, which conflict with §9.1. The frozen proposal itself was not edited.

## Evidence and verification

Incremental feedback (not Unit completion evidence): `artifacts/verify/ranges-review-feedback/proofs.log` passes 613 tests, including all selected constraint classes and the new range regressions. Failed intermediate runs remain in that directory. No NativeAOT tests were run. No compiler speedup is claimed; new allocation regressions retain strict zero-allocation assertions.

| Formal run under `artifacts/verify/` | Result |
| --- | --- |
| `20260930-095053-858-unit-ranges-review-checkpoint` | PASS: warning-free non-incremental Release build, 862 tests, 124 native O0/O2 executions, Milestone 41 original-source O0/O2 (2 checks). |
| `20260930-095326-380-session-ranges-review-checkpoint` | Whole-solution Release build PASS, 13,720 tests with 2 failures. Unconditional early conversion binding broke `BorrowedTupleProjectionTest.ExecutesStoredElementBorrows` (StoredReferenceOutlivesWrapper) and `OwnershipJoinTest.JoinedBorrowCannotOutliveEitherLocalSource`. The snapshot step was not reached. Both regressions were subsequently repaired; retain this failed run. |
| `20260930-100204-878-unit-ranges-review-scope-repair` | PASS on the final compiler sources: warning-free non-incremental Release build, 609 tests (including both regressions, control-flow/results/continuations and the three changed feature classes), 38 native O0/O2 executions. |

**A complete Session rerun on the final sources remains pending**, deliberately deferred at the user's stopping point. The last repair limits early conversion evidence to literal fitting with an available read operand; it does not eagerly bind unrelated borrow conversions. CLI/LSP rendering of the new width-specific warning cases has not been individually inspected. Shared output adapters ran in the Session suite, but the outstanding diagnostic work below still needs its own output review. Evidence is ignored by Git and must be retained separately.

## Resume here

1. Read this file, the checkpoint commit, AGENTS.md, CODEMAP.md and DIAGNOSTICS.md §10. Inspect `git status` before continuing; retain any later user changes. **First rerun the full Session and diagnostic snapshot** on the final sources. LSP discovery tests need permission to enumerate the OS temp directory and its ancestors; the previous Session used an elevated sandbox permission, not a source workaround.

   ```powershell
   ./scripts/verify.ps1 -Mode Session -DiagnosticSnapshot -Name ranges-review-resume
   ```

2. Finish **proposal §3.4 diagnostics** before declaring the integration complete. Appendix A.13 already requires repairs for non-iterable ranges, but the rejection currently publishes generic `UnsatisfiedConstraint_Kd` without a range-specific Reason, label or Advice. Reproduce:

   ```kimi
   for i in 1..^1 => ()
   for i in ..3 => ()
   for i in 1@i32..3@i64 => ()
   ```

   The first two should explain the unresolved boundaries and suggest `r.resolve(values.length)`; the third should suggest explicit conversion of the boundaries to the same integer Type. Valid counterparts are `for i in (1..^1).resolve(5) => ()`, `for i in (..3).resolve(5) => ()` and `for i in 1@i64..3@i64 => ()`. Entry naming must follow the actual Subject mode (`Iterable`, `UniqIterable` or `IntoIterable`), not a hard-coded entry. Relevant sources: `Binding.Sequences.FailIterationSubject`, `Binding.Diagnostics`, `Binding.ReportDiagnostics`, `DiagnosticCode.tinyhand`.
3. Fix the **shadowed boundary diagnostic**:

   ```kimi
   struct Start
   let r = ..3
   let p: Start = r.start
   ```

   This rejects, but the primary span is the whole declaration and the generic message fails to distinguish the user Type from `Kimi.Start`. `FailMismatch` currently uses `BoundType.Name`, losing qualification and generic arguments; equal display strings discard mismatch evidence. Preserve the actual/expected facts and use the RHS location. Proposal §3.4 also requires qualified display for shadowed Kimi names. Hover is not currently implemented; record that product boundary separately rather than claiming it works.
4. Check proposal §3.4's **range-shape mismatch advice** (PositionRange for resolution, appropriate iteration Contract/Item for enumeration, concrete shape for boundary access). Reconcile any missing formal requirements in the owning chapters and append their disposition to INTEGRATED.md. Do not weaken requirements to match implementation or reopen the frozen proposal. Investigate contextual advice at `Binding.Calls`' rejected-candidate recording; avoid speculative automatic edits or inferring arbitrary function intent.
5. Preserve the diagnostic workflow: independent expected public records, valid counterparts, unrelated errors, prerequisite/recovery cases, Reason/primary location/Note/Advice, CLI and LSP output, limits and rebinding. Initial diagnostic-only tests were run and all four failed; their draft C# and logs are retained at `artifacts/verify/ranges-review-feedback/RangeDiagnosticTest.cs` and `diagnostic-repro.log`. They are deliberately outside the compiled test project at this checkpoint. Their intended expectations need refinement (notably the iteration entry), not blind acceptance. The source examples above are the durable reproducers if ignored artifacts are unavailable.
6. Continue the remaining proposal-to-SPEC-to-implementation matrix, especially requirement versus implementation-policy distinctions in §3.4. Array/fixed-array read entries currently duplicate Slice resolution logic instead of delegating literally; determine whether a refactor has practical benefit without losing one-time receiver evaluation or Partial Move behavior. Fixed-array `length`/`indices` staying compiler metadata was explicitly allowed by the proposal. Existing `PositionModelTest` covers all 12 integers against an overflow-free model; retain native O0/O2 checks, receiver-once, closed maximum/exhaustion and rebind coverage.

Use incremental test-project builds and selected xUnit methods for feedback, formal Unit verification once per completed unit, and Session verification at the end. The existing proposal/model tests are useful evidence, but passing them does not settle the unfinished diagnostic audit. A separate timing comparison of the changed hot paths remains unperformed; use Benchmark and identical input/configuration if making a performance claim.
