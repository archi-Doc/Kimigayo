# Diagnostics conformance review — 2026-09-30

This review compares [DIAGNOSTICS.md](DIAGNOSTICS.md) with the formal specification and the compiler's recording, aggregation, publication and input paths. The formal specification remains authoritative. The frozen proposal is unchanged; its integration register records the prerequisite clarification.

The public diagnostic contract is integrated into SPEC §23.3.3, §23.3.6 and §23.4.7 and linked from SPEC.md. The audit found implementation violations in that shared path and in declaration/input reporting, repaired below. This is not a claim that every reporting site fully conforms: the existing parser, flow-refinement and ownership-evidence gaps remain open.

## Specification and implementation map

| Developer document | Formal requirement | Implementation and review evidence |
| --- | --- | --- |
| §§1–3: definition, categories, facts and contract faults | §23.3.6.1–2, §23.3.6.7 | DiagnosticEntry/DiagnosticEntries and DiagnosticCollection validate catalogs, arity, typed scalar values and ranges. Corrected the developer document's claim of separate statically typed generators: current phase conversions use runtime-validated schemas. Catalog, owner and contract tests cover the boundary. |
| §2 rules 8–10; §4.2: outcomes, error state and phase boundaries | §23.3.3 | DiagnosticOwner records Error state before suppression. Project and CheckService use phase error state and reject unexplained failures. Input failures and cancellation interactions are covered by CheckServiceTest. |
| §4.1: source snapshots and identities | §23.3.6.3–4 | Keys distinguish syntax subjects and document snapshots. Source table entries now retain consumption order even when related sources are first encountered through supplements. Same-path snapshot tests verify distinct entries and excerpts. LSP input revisions remain in the unit input records managed by InputStore; JSON is not a public schema. |
| §4.1: multiple-subject problems | §23.3.6.4 | Binding.DuplicateDiagnostics normalizes paired declaration collisions. Each later declaration refers to the first consumed declaration; the first's invalid analysis state depends on a later direct Error. Scope, overload, alias, direct-conformance and specialization cases are covered. |
| §4.3: capture, aggregation, prerequisites | §23.3.6.4 | Prerequisite and related arrays are captured, repeated related facts are unioned, and every Error at a prerequisite key must be explained. Mixed direct/unresolved keys, cycles, warnings, invalidation and arrival permutations are covered. |
| §5: explanation, limits and order | §23.3.6.2, §23.3.6.5–6 | Full related values are ordered before bounding/limiting; omissions count distinct omitted evidence. Excerpts copy and expand only a bounded window. Typed mismatch tests preserve differences; function duplicate ranges exclude bodies. |
| §6: faults | §23.3.3 and §23.3.6.7 | DiagnosticFaults keeps catalog-independent fixed fault text, orders a retained exception record with the result and uses the shared Note limit. Contract faults replace partial results; analysis exceptions keep valid records. |
| §7: output adapters | §23.3.6.8, §23.4.7 | Commands and WorkspaceCheck convert the same records. Reviewed CLI underlines and LSP payloads with relatedInformation both enabled and disabled. JSON round trips, structural equality and input attribution remain tested. |
| §§8–10: migration, evaluation and workflow | Repository development policy; observable behavior remains in §23 | Historical D0–D4 checkpoints are not an exhaustive conformance certificate. Snapshot difference kinds were corrected to match the harness; timing is measured separately. |

## Repairs and verification

| Unit | Defect and correction | Formal evidence under artifacts/verify/ |
| --- | --- | --- |
| `c84a676a` | A direct Error could incorrectly release a key containing an unresolved/cyclic Error. Additional related evidence was discarded, scratch arrays remained mutable, and related arrival order changed source tables. Added scalar/schema/severity/range validation and corrected fault ordering/Note limits. | `20260930-114809-063-unit-diagnostics-contract-audit`: warning-free Release build, 247 tests, zero snapshot differences. |
| `ca678a53` | Returning after the first consumed source failure lost other explanations. Test-source I/O and UTF-8 failures became Faulted; pending/cancelled reads could be swallowed by an earlier encoding failure. Unified product/dependency/test source handling and preserved pending/cancellation. | `20260930-115609-661-unit-diagnostics-input-audit`: warning-free Release build, 119 tests, zero snapshot differences. |
| `a9e1cdf9` | Paired duplicate declarations reported the first declaration as an independent duplicate and omitted related evidence. Normalized groups, added the Name fact, retained parsed function signature spans, and preserved invalidation/rebinding. | `20260930-120724-830-unit-diagnostics-duplicate-audit`: warning-free Release build, 345 tests, 12 native specialization O0/O2 executions, zero snapshot differences; warm duplicate grouping allocates nothing. |
| Excerpt and empty-prerequisite follow-up | A bounded excerpt still copied/expanded the entire source line. Restricted text work before tab expansion and clipping; preserved UTF-16 primary ranges, tab anchors and EOF insertion carets. An empty prerequisite array incorrectly replaced a direct diagnostic's typed Reason with prerequisite facts; empty arrays now mean no prerequisites. | `20260930-121544-084-unit-diagnostics-excerpt-audit`: warning-free Release build, 95 tests, zero snapshot differences. The final boundary reproducer fails before the fix and passes in `20260930-122401-548-unit-diagnostics-final-boundaries`: 96 tests and zero snapshot differences. |

Reproducers, including failed pre-fix runs and intermediate build/test failures, are retained in `artifacts/verify/diagnostics-audit-feedback/`. `adapter-review.log` contains the inspected CLI/LSP examples. The first three Unit directories retain their verified change patches; the final boundary Unit and Session retain the remaining patch. The audit adds 40 regression cases; passing existing snapshots alone did not expose these failures.

Final Session `20260930-122850-706-session-diagnostics-audit-final` passes a warning-free whole-solution Release build, all 13,795 functional/allocation tests and the diagnostic snapshot (zero differences). Of 32 milestone harnesses (1–23, 27–32, 37, 39, 41), 31 pass in that run; milestone 12 fails with `Access to the path is denied` during its parallel native O2 build. Its unchanged isolated rerun passes both O0/O2 checks (`milestone12/Release/befba9e0a997401cb9b685009ed2b6a8/verification.json`, compiler SHA-256 `8EDE6CB0F8A4287B109A709C6A53FD838E38FA388269C0FD15F338BC71A80F01`). Thus all 32 have passing coverage at the final source state, but the Session itself is retained as failed, with the supplemental result rather than a rewritten summary. The earlier Session `20260930-121855-044-session-diagnostics-audit-complete` passed 13,794 tests and had the same native access failure in milestone 2; its isolated retry also passes. Both failures and retry logs are retained. The access failure's cause is not established.

## Measurements

`artifacts/benchmarks/diagnostics-audit/` retains fixed Release `Benchmark --diagnostics` runs, using seven samples after the same warm-up. The core contract correction keeps warm rebind/report at 120 bytes/operation; the 300-error workload increases from 4,099,150 to 4,153,422 bytes (+1.3%) for correct capture/aggregation. Single before/after timings do not establish a general speedup.

The excerpt reproducer allocated 50,496 bytes for eight finalizations with a 2,000-character prefix and 16,025,016 bytes with a 1,000,000-character prefix. The repaired allocation regression requires that unquoted prefix length add no allocation after source line indexing. The fixed workloads in `before-excerpt.json` and `after-excerpt.json` measure those same prefix sizes after line indexing:

| Unquoted prefix | Allocated bytes/finalization, before → after | Median microseconds/finalization, before → after |
| --- | --- | --- |
| 2,000 characters | 6,312 → 2,104 | 2.7 → 0.4 |
| 1,000,000 characters | 2,002,312 → 2,104 | 715.7 → 0.3 |

The final 300-error workload allocates 4,156,054 bytes and warm rebind/report remains 120 bytes. Whole-check timings vary between runs; the bounded excerpt allocation and isolated warm-reuse regressions, not a general throughput improvement, are the verified performance claims.

## Remaining work and confidence boundary

The previously documented §11 gaps are still real and are not weakened out of the formal specification:

- Parser reporting sites still pass syntax descriptions or repair prose as an UnexpectedToken token argument. Convert those sites to factual token/construct explanations and conditional Advice, with per-requirement recovery and output tests.
- Programs 33 and 38 require flow refinement. A refined use still reaches Language diagnostics instead of a precise Unsupported classification. Distinguishing that case from a genuinely missing member requires refinement-aware analysis (P33), not relabeling all failed lookups.
- Ownership diagnostics still lack earlier-Move, conflicting-Loan and missing-initialization provenance. Source evidence must be carried through the ownership dataflow and branch joins; guessing from nearby source positions would violate the prerequisite/evidence contract.

The shared owner cannot recover facts a recorder never supplied. In particular, the existing first-failure Binding state and ownership's `(Source, Failure)` aggregation still need per-recorder checks for independent conditions/Places and instantiation-specific facts. The duplicate group repair captures its collision facts separately, but does not certify all other recorders. Keep these limitations open; neither the historical migration stages nor a passing Session establishes complete diagnostic conformance.

NativeAOT is not part of this review. CSP serving, public JSON/catalog schemas, durable diagnostic identities, hover and automatic repairs remain outside the implemented diagnostic-only service.

## Second review — 2026-09-30

A second comparison of this document with SPEC §23 and the code reproduced and repaired these defects:

- **Recovery.** Recovery nodes without their syntax Error published unexplained `PrerequisiteUnavailable_Kd` records. A token rejected by the lexer was reported again as an `UnmatchedToken_Kd` with an empty token, because the lexer and the parser used different targets. A body that a syntax Error left missing produced ownership Errors. A missing member name was located on the next line.
- **Mismatch display.** `BoundPair` showed a Type that lies inside the other as a bare "…". Bounded text could split a surrogate pair.
- **Command and check entry.** An unsupported target published no Error and faulted the command as an unexplained rejection; it now reports `UnsupportedTarget_Kd`. The command path rendered partial records without a fault after an analysis exception. An invalid `TestProjectId` produced a Completed result.
- **Record details.** The project file's source-table entry followed later sources. `CheckFaulted_Kd` declared a Text Reason. Two failed writes of one interpolation had no defined order and faulted the check.

SPEC §23.3.6.8 and §23.4.7 now list omissions after related locations, as both adapters always rendered them. The existing parser prose, flow-refinement and ownership-evidence gaps remain open. So does a Note shortened by its limit without an omission count.
