# Ranges review — 2026-09-30

The audit resumed from the checkpoint in `03644ee3`, with a clean worktree. The checkpoint's full Session rerun and final Session verification of the repaired sources both pass. The remaining proposal-to-specification audit and diagnostic repairs are complete in `ae55e615`. The frozen proposal `draft/Changes/2026-09-29 Redesign Ranges B.md` was not edited.

## Repaired behavior

- **Non-iterable ranges.** `for` rejections retain the range and the actual selected entry (`Iterable`, `UniqIterable` or `IntoIterable`). The public record explains both boundary Types and the same-integer requirement. Omitted/from-end boundaries get length-resolution Advice; different integer Types get explicit-conversion Advice. Prerequisite failures and unrelated errors retain their own identities.
- **Type mismatch evidence.** Binding stores the actual/expected semantic Types instead of comparing their short names. Publication displays generic arguments and qualifies the designated Kimi positions/ranges. A shadowed `Start` versus `Kimi.Start` mismatch points to `r.start`. Structural display includes reference layers, function Types and concrete/symbolic array lengths. Bounded mismatch formatting keeps whole names such as `i32` and `i64` rather than just their differing digits.
- **Range-shape arguments.** Rejected overloads keep the argument/parameter comparison that failed, including literal, saved, generic and named-argument cases. Candidate locations and a primary Note retain the comparison even beyond the related-location limit. Advice conditionally describes `PositionRange`, the appropriate iteration entry plus Item constraints, or a concrete boundary-bearing Type. It neither infers a function's purpose nor supplies an automatic edit. Successful alternative overloads, shadowing and unmatched labels do not get this Advice. Reused candidate scratch is cleared even for inaccessible candidates.
- **Warnings.** CLI and LSP output for the checkpoint's `i64`/`i8` literal-width reproducers was inspected. The generic Advice now distinguishes `None` from a try operation and Abort from an aborting operation. Resolution, warning selection and acceptance are unchanged.
- **Formal intake.** SPEC §4.6.1 and §4.6.3.4 now state the previously omitted diagnostic requirements. `draft/INTEGRATED.md` records this intake without reopening the frozen proposal. No public Kimi declaration changed.

## Audit matrix

The formal specification is the authority. Proposal section numbers below identify reviewed scope, not a second authority.

| Proposal scope | Owning specification / implementation | Verification or disposition |
| --- | --- | --- |
| §2.2–§2.3, §3.2: closed capabilities, unchecked position construction, value reads and Type identity | SPEC §3.5.3, §4.6.2, §8.3/§8.4.7; `Core.kimi`, `Binding.ReadTypes`, `Binding.TypeIdentity` | `PositionContractTest`, `ValueReadTest`, `TypeIdentityTest`; the checkpoint repairs and warm allocation regressions pass again. |
| §2.4: syntax, distinct shapes, independent boundary Types and literal fitting | SPEC §4.6.3.1–2, §12.3.1; `Binding.Ranges` | `IntegerRangeTest`, `RangeValueTest`, `RangeDiagnosticTest`; no obsolete `IndexRange`, `Index.init` or `Range<T>` spelling remains in the formal chapters or public library index. |
| §2.5: position/range resolution and target-independent snapshots | SPEC §4.6.3.3, §4.6.4; `Core.kimi`, direct/saved selection lowering | `PositionModelTest`: all 12 integer Types, signed/unsigned extremes, negative/zero/maximum lengths, all shapes, re-resolution, direct/saved agreement and invalid Abort paths against the overflow-free model. |
| §2.6: common APIs, receivers, ownership and evaluation order | SPEC §4.6.1/§4.6.6/§4.6.9/§4.7; Array/Slice/Storage sources and keyed selection | `KeyedIndexingTest`: API equivalence with Slice, receivers evaluated once, saved keys, nested selections, Partial Move boundaries and Array-result borrow retention. The earlier `trySlice` naming decision remains (§9.1 excludes constraint-only overloads). |
| §2.7: conditional iteration, maximum closed ends, exhaustion and value entries | SPEC §4.6.3.4–5; `Core.kimi`, `Binding.Sequences` | `IntegerRangeTest`, `RangeValueTest`, `TypeIdentityTest`: all integer families, closed maximum/exhaustion, reversed Abort, no retained range borrow and ResolvedRange cursor/iterator equivalence. New diagnostics cover all three Subject modes. |
| §2.8: formatting | UTF-8 formatting §4.2; `Core.kimi` | `RangeValueTest` checks the source-like form of every range shape, from-end/omitted boundaries and resolved intervals. |
| §3.2/§3.4: warnings, folding and merged checks | SPEC §17.4.4, §4.6.8; `Binding.PositionWarnings`, range emission | `PositionWarningTest`, `PositionModelTest`: width/sign-sensitive arithmetic, overflow exclusion, fixed-array folding and O0/O2 model agreement. |
| §3.4: diagnostic repair and qualification | SPEC §4.6.1/§4.6.3.4, §23.3.6; Binding diagnostic/candidate records and shared adapters | 35 new `RangeDiagnosticTest` cases: positive counterparts, unrelated errors, recovery, rebinding, strict zero-allocation warm recording, bounds, candidate omissions and CLI/LSP rendering. |
| §3.4: literal delegation of Array/fixed-array read APIs to Slice | Implementation policy; observable equivalence is SPEC §4.6.6 | Retain the existing small Kimigayo wrappers over shared Position/PositionRange resolution. Literal delegation would create a whole-sequence view before even a failed try lookup; no measured benefit justifies changing these acquisition paths. API equivalence and Partial Move checks are native-tested. This is a call-graph choice, not a language limitation. |
| §3.4: fixed-array metadata and iterator representation | SPEC §4.6.1/§4.6.8; library README | Fixed-array length/indices remain compiler metadata, explicitly allowed by the proposal. The previously selected closed iterator and ResolvedRange cursor optimization are retained. |
| §2.9, §6.3: exclusions and performance | Appendix D, SPEC §4.6.8; earlier benchmark records | No descending/stepped/infinite range, user position conformance or string indexing is added. This diagnostic follow-up changes no native runtime algorithm and makes no timing claim. Historical P41 comparisons, including the approximately 65% O0 `insert(^1, x)` regression, remain documented in PLAN_HISTORY; they were not repeated here. |

## Evidence

All paths below are under `artifacts/verify/`, ignored by Git and retained separately from commits.

| Run | Result |
| --- | --- |
| `20260930-105940-212-session-ranges-review-resume` | PASS on checkpoint `03644ee3`: warning-free whole-solution Release build, 13,720 managed tests, diagnostic snapshot. Both checkpoint Session regressions remain fixed. |
| `ranges-review-resume-feedback` | Reproducers and incremental checks, including failed intermediate attempts. `limits.log` retains reviewed CLI/LSP output; the final focused range suite contains 35 passing cases. These feedback builds are not formal completion evidence. |
| `20260930-111705-405-unit-ranges-review-diagnostics` | Failed build: a missing namespace import in the new display helper; repaired. |
| `20260930-111848-972-unit-ranges-review-diagnostics-final` | Warning-free build, 392 tests and unchanged diagnostic snapshot passed; native step stopped because this checkout lacked `toolchain/windows_x64/kernel32.lib`. Retained as failed evidence. |
| `ranges-review-resume-feedback/toolchain-setup.log` | The existing local setup script validated pinned LLVM identities, generated the shared import library and passed backend O0/O2 tests. No download or PATH change. |
| `20260930-112258-944-unit-ranges-review-unit` | PASS on final compiler sources: warning-free non-incremental Release build, 393 tests, 122 native O0/O2 executions, Milestone 41 original-source O0/O2. Diagnostic snapshot: zero differences from the resumed checkpoint. The targeted new diagnostic changes are covered by their own public-record/output regressions. |
| `20260930-112705-566-session-ranges-review-complete` | PASS on clean `ae55e615`: warning-free non-incremental whole-solution Release build; all 13,755 functional/allocation tests; diagnostic snapshot with zero differences; all 32 existing milestone harnesses (1–23, 27–32, 37, 39, 41), each building and directly running the original source at O0 and O2. |

The Unit's `verified-change.patch` preserves the staged implementation and documentation delta; the final Session identifies the clean compiler/test commit. No NativeAOT tests were run.

## Product boundaries

Hover and automatic repair/code-action support remain unimplemented; the language server publishes diagnostics. The range audit does not complete the separate parser-explanation, ownership-evidence or object-flow-refinement work tracked in DIAGNOSTICS.md §11. No new timing or whole-compiler performance claim is made.
