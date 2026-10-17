# Evidence, test migration and moving metrics for R5 and R6-gen (q4)

Base: checkout `dev` = `5c42b577` (clean) for code and tests; documents read from `origin/dev` = `2aac946b`
(`git show origin/dev:<path>`, copies under `scratchpad/deep/od/`). Tests, scripts and the native runner are the same on
both except `scripts/verify.ps1`, `scripts/verification-selection.ps1` and VERIFICATION (G117, executed-selector check;
`git diff --stat 5c42b577 origin/dev -- scripts docs/dev/VERIFICATION.md`), so script line numbers below are origin/dev's.
Read-only: no build, test or native run. Counts are `git grep`/Python scans of `tests/xUnitTest/**/*.cs` (scripts under
`scratchpad/deep/py/`). (H) marks an inference I did not verify.

Short names: A5P = `draft/Proposals/2026-10-16 Complete Polonius A5 Plan.md`; PLAN10 = `draft/Proposals/2026-10-10 Compiler
Size Reduction and Single MIR Plan.md`; PRE5 = `draft/Proposals/2026-10-12 Pre-R5 Work Items.md`; DEC/ARCH/VER/PLAN = origin/dev
`docs/dev/DECISIONS.md`, `COMPILER_ARCHITECTURE.md`, `VERIFICATION.md`, `PLAN.md`.

## 0. Recommendation in short

R4 stalled because its metric (P9) could not move and its exit demanded parity with an old path that follows other rules
(A5P:54-77). R5's evidence should therefore compare **native behavior**, never IR shape, and its metric should be a
**checked-in pending ledger** of the tests and fixtures that fail under the MIR code generator, which verify checks for exact
equality and which only shrinks. Before the first codegen unit, three cheap, codegen-independent units must land: the IR
assertion classification with one owner decision (EmitFixture's built-ins; incidental and covered-proxy deletions), fail-closed
native instrumentation (import interposition instead of IR-text rewrites), and a fixture comparison script (D5 part 1 now,
parts 2-3 later). Divergent native outcomes get one canonical per-path form modeled on DEC R4-M60: the target in the
expectation, the legacy value recorded beside it with its decision item and class, written as a `.deviation` sidecar the
comparison script checks, removed at R6-gen. MIR codegen is pooled from its first unit: a warm allocation test leaves the
pending ledger only at 0 bytes. The O2 harness needs a per-variant environment; the lost A=B record is retaken.

## 1. Generation-coupled assertions and instrumentation

### 1.1 Verified counts

| Item | Count (this run) | Evidence | Survey figure |
| --- | ---: | --- | --- |
| `EmitFixture(` call sites | 1,181 lines / 366 files (1 definition) | `git grep -c "EmitFixture("` | 1,180 |
| `WriteFixture(` | 319 = 241 `NativeAllocationAudit.` + 76 `ScalarEmissionTest.` + 2 definitions | `git grep -c` per prefix | 321 / 77 |
| Generated fixtures per Session | 5,932 `.ll` (each with `.stdout/.exit/.stderr/.timeout`) | `artifacts/verify/20261017-052809-117-session-g111-g118-final/fixtures-Release` | 5,932 |
| Single-line IR-text assertions (candidates) | 306 lines / 85 files before removing false positives (diagnostic `text`, console `output`) | union of `Assert.(Contains…)(…, ir|output|text|…)` and asserts with LLVM tokens; list in `scratchpad/deep/ir-asserts.txt` | ≈247 / ≈65 after manual removal |
| Methods inspecting a prepared module (`TryPrepare(out <m>`) | 113 methods / 50 files hold the 119 reads; 44 of them also read `Ownership.Bodies`, 37 also emit a native fixture | `py/methods.py` | 131 Emission-structure methods / 54 files (wider pattern) |
| `BodyLowering.Validate*Flags` | 22 lines / 8 files | `git grep` | same |
| Negative `__kimi` name assertions | **33 lines / 16 files** (DictionaryLibraryTest 10, DynamicArrayCostTest 4, StorageBoundaryTest 3, …) | `git grep -E 'Assert\.DoesNotContain\([^;]*__kimi'` | 30 |
| All negative IR assertions (`DoesNotContain|DoesNotMatch` with IR tokens) | 53 lines | `git grep -E` | — |
| Ad-hoc IR rewrites outside the audit | **23 lines / 12 files**: 14 on import calls/declarations (`HeapAlloc`, `HeapFree`, `GetLastError`, `QueryPerformance*`) in 9 files, 9 on runtime-template or entry text (DynamicArrayCost copy anchor, StringComparison 2, Utf8Float 1, SharedArcRuntime 4, SharedObjectRuntime 1) | `git grep -n '\.Replace('` per file | 19 / 11 |
| Allocation measurements that include generation | 186 sites / 170 files (180 `WriteIr`, 6 `Validate`), 183 assert 0 bytes | `py/zero.py` over 634 `AllocationMeasurement.Measure(` sites | 184 / 181 |

### 1.2 EmitFixture's three built-in checks

`tests/xUnitTest/Tests/ScalarEmissionTest.cs:130-142` runs for every one of the 1,180 calls, before the fixture is written:

| Check | Line | Class | Basis |
| --- | --- | --- | --- |
| Some internal function named `@__kimi_entry_body` or `@__kimi_f<N>` with a scalar result | 136-137 | Incidental | IMPL §21.3.1: "symbol names and optimization attributes are implementation-defined" (`docs/impl/21-layout-runtime-and-code-generation.md:101`) |
| No `select i1` after that function, except two old-Emission shapes spelled in `%zero\d+`/`%minusone\d+` and `WindowsLowering` reason constants | 138 | Behavior proxy, incidental spelling | Stands for "source control flow still branches" (comment :134-135); the fixture written right after observes evaluation order natively |
| No `store i1\b` anywhere | 139 | Required (internal contract) (H) | `bool` is 1 byte (IMPL §21.1 table, `21-…md:54`); a stored `i1` leaves the byte's other bits unspecified for byte-wise reads (H: LLVM LangRef wording, not checked here). Spelled in LLVM, not old SSA names, so it is already pipeline-neutral |

Consequence: unless the MIR code generator reproduces old function names and never emits `select i1`, **every** EmitFixture test
fails under the codegen selector before it writes its fixture. This is one owner decision that must precede the first codegen
unit (A5P:855-860 "classified ... before codegen").

### 1.3 Fail-open instrumentation

`tests/xUnitTest/NativeAllocationAudit.cs`:

- `:16-19` text-replaces `call ptr @HeapAlloc(`, `call i32 @HeapFree(` and `call void @__kimi_exit(` without checking that
  anything was replaced. The audit result is only checked on paths that call the replaced `__kimi_exit` (`:36-61`). If a
  generator reaches `ExitProcess` by another call text, **all 241 audits pass vacuously**; if it calls `HeapAlloc` with any
  other call text (`call noalias ptr @HeapAlloc(`, a different helper), allocations are undercounted, and the **60 audits that
  expect `(0, 0, 0)`** (`git grep -P 'NativeAllocationAudit\.WriteFixture\([^;]*?,\s*0,\s*0,\s*0\b'`) pass vacuously.
- `:23-25` requires the Array copy anchor exactly once when `maxTransferredBytes >= 0`; `:27-31` instruments the Dictionary
  transfer anchor only if `@__kimi_transfer_bytes` is defined. Only 3 calls pass transfer bounds, all Dictionary
  (`DictionaryCapacityTest.cs:101`, `DictionaryCostTest.cs:35,60`); `DictionaryCostQueries` (max 0, no min) is the vacuous case.
- The three `HeapAlloc` call texts live in runtime templates: `WindowsRuntime.ll.in:191` (`__kimi_alloc`),
  `LlvmModuleWriter.Arrays.cs:117` (shrink), `LlvmModuleWriter.Dictionaries.cs:36` (`__kimi_try_allocate`). Every one goes
  through the import declared once at `WindowsRuntime.ll.in:4` (`declare dllimport ptr @HeapAlloc(ptr, i32, i64)`), and process
  exit through `ExitProcess` (`:9` declare, `:21` the only call, inside `__kimi_exit`). The import list is closed
  (`WindowsProfile.cs:34` `RuntimeImports`), and the native runner rejects undeclared object dependencies
  (`test-scalar-fixture.ps1:37-43`).

The 12 ad-hoc rewrite files:

| Behavior if the anchor is missing | Sites |
| --- | --- |
| Fails closed natively (the expected Abort only happens with the injection) | Failing allocators: DictionaryCapacityTest:53, DictionaryLibraryTest:209,219-220, DynamicArrayCostTest:35, FunctionDefaultTest:150, HeapClosureErasureTest:96, SliceToArrayTest:34; fake clocks TimeLibraryTest:24-25,108-109; rc/arc control injection (asserted: SharedObjectRuntimeTest:78, SharedArcRuntimeTest:74) |
| Fails open (undercount against an upper bound) | WorkloadCostTest:61 (bound only; anchor asserted present, other call texts uncounted); DynamicArrayCostTest:247 with `exactAllocations: false` |
| Fails open (hook never runs, fixture expects empty stdout and exit 0) | Utf8FloatTest:150 (`call void @__kimi_entry_body()` not asserted) |
| Weakened but runs | StringComparisonEmissionTest:155 (`define internal i32 @__kimi_string_` → external; unasserted) |

Fail-closed design (one helper, the same rule everywhere): **interpose imports by symbol**, not by call text. Rewrite every
`@HeapAlloc(`/`@HeapFree(`/`@ExitProcess(` reference except its `declare` line to the audit function, then assert that the
module declares the import exactly once (or not at all, with no reference) and that no other reference to the symbol remains.
This counts every call whatever its decorations, under any generator that keeps the Windows profile's imports, and is the
"count in the runtime adapter" of A5P:852 at the one boundary both generators share. The transfer anchor becomes required
whenever a transfer bound is given; text anchors that remain (copy anchor, `__kimi_entry_body` hook, define-internal) are
asserted with their counts. Failure-injection sites use the same interposer with a failing body.

### 1.4 Negative name assertions

The 33 `DoesNotContain("…__kimi…")` assertions (16 files) pass vacuously if a generator renames or inlines helpers. Most
stand for "no runtime call / no allocation / no per-element helper" (for example `DynamicArrayCostTest`, `StorageBoundaryTest`)
(H: from a read of the top files). They are invisible to any failure-based metric, so the classification must convert each into
a positive native observation (audit counts, a fixture) or delete it as incidental; they cannot simply ride along.

### 1.5 Classification procedure

Unit of classification: one assertion (or one instrumentation site), recorded with file:line, method, class, basis, disposition
and the native fixture that already covers it.

| Class | Test | Disposition | When |
| --- | --- | --- | --- |
| **R** required | SPEC/IMPL clause or a recorded runtime contract (IMPL §21.1-21.3 ABI, imports, FP attributes, no `mustprogress`/`willreturn`, SHARED_OBJECT_RUNTIME ordering, runtime glue layout, stack bound, `store i1`) | Keep; re-spell as a pipeline-neutral matcher (no `%v\d+`, `%p{}`, helper ids) in one shared helper; both generators must satisfy it | Rewritten in R5 by the codegen family that first emits the construct |
| **P** behavior proxy | Natively observable (Abort code/location, evaluation order, checks present, allocation anchors) | If a fixture in the same method or class observes it: delete (batch "P-covered"); else add the fixture or MIR dump golden, then delete | Same family unit |
| **I** incidental | Old SSA names, helper names and caching, phi/load avoidance, folding, constant pooling, unrequired attributes | Delete by owner decision (A5P:858) | In the family unit that makes the test pass under the selector |
| **S** structure | Reads `EmissionModule`, `BodyLowering`, `OwnershipBody` (TryPrepare, `EmissionOpcode`, `Instructions`, `Validate*Flags`, defect injection) | Keeps passing under the selector (it calls the old Emission directly), so it guards only legacy; any R/P property inside it gets a pipeline-neutral test in R5; the method is deleted with the old Emission | Property in R5; deletion at R6-gen (PLAN10 §13.2:716 "削除した仕組みのテストは、消すか観測できる振る舞いの検査に書き換える", approved PLAN10 §18) |

Procedure: (1) a script lists candidates (patterns of §1.1, kept with the table); (2) a classifier assigns class, basis and
covering fixture per row, reading the method (single-line greps miss multi-line asserts, so the candidate list is a floor);
(3) a second reader checks every R and I row and a sample of P rows; (4) the owner approves **class rules plus the I and
P-covered deletion lists as one decision** (the OD13 pattern, A5P:938-941), not row by row; (5) units apply the dispositions.
Estimated split of the ≈247 single-line IR assertions (tests-harness §1.6, (H)): R 60-80, P 100-120, I 65-75.

### 1.6 Which classes block which R5 units

| Class / item | Blocks | Why |
| --- | --- | --- |
| EmitFixture naming and `select` checks | The first codegen unit (every EmitFixture test) | Fail before the fixture is written |
| Fail-open audits, Utf8Float hook, upper-bound counters | Evidence part 4 and every allocating codegen family (Array, Dictionary, strings, closures, objects); prudently the first codegen unit | A vacuous pass is invisible to the pending metric |
| Negative name assertions (33) | Credibility of the family's evidence, not its pending count | Pass vacuously |
| I and P on public `WriteIr` text | The pending reduction of the family that covers the test | Fail under the selector forever otherwise |
| R spelled in old names | The family that emits the construct (e.g. ForeignEmission ABI widths, rc/arc atomics) | Needs the neutral matcher in the same unit |
| S (113 TryPrepare methods, 22 Validate flags, injection) | R6-gen, not R5 | Guard only legacy; their R/P properties must be covered pipeline-neutrally before deletion |
| OD4 instance-failure tests (20 methods / 10 files read `Failed|Failure|AnalyzeInstance`, `FailureIsResourceLimit`, `ReportFailure`) | The collector unit | Expectation moves from build time to acceptance (A5P:839-844) |

## 2. Per-pipeline expectations

### 2.1 Existing forms

| Form | Decision | Shape | Removal |
| --- | --- | --- | --- |
| `[Trait("OldRule", "<item>")]` on a method (19 methods / 7 files) | DEC R4-M50 (DEC:1109-1114) | Marker only: the test asserts the old rule; no target expectation (changing expectations now was rejected) | R6-check |
| `spec-cases.json` variants with `Old`/`Mir` (43 cases, 63 variants, 22 `Old`, 5 `Mir` on origin/dev) | DEC R4-M45 (DEC:523-538) | `Expected` = SPEC records; a path's difference holds its records, class (a-e), SPEC clause and note; old side checked always, MIR side once its family landed (`VerifiedSpecExampleTest.cs:63-81` on origin/dev) | R6-check |
| `pair-cases.json` row `Old` (1 row, `B12-parent-move`) | DEC R4-M60 (DEC:1173-1186) | `Expected` = SPEC verdicts; `Old` names the revision item and holds the old verdicts and the old native run; the test checks `Old` differs and is well formed (`GenericOwnershipCaseTest.cs:62-77,101` on origin/dev) | R6-check |

The common rule of R4-M45 and R4-M60: **the expectation states the target; the path that still decides records its deviation
beside it, with the decision that justifies it; the test checks that path's value, checks the deviation actually differs, and
the deviation is deleted with that path.** R4-M50 is the one marker-only exception, chosen because the old path decides
acceptance until R6-check.

No C# test today branches an expectation on the pipeline: the only pipeline reads are `MinimalEmissionTest.Describe`
(message text, `MinimalEmissionTest.cs:340`) and the ledger's gate (`MirDifferenceTest.cs:176`).

### 2.2 R5-R6-gen cases that need it

| Case | Legacy (default until R6-gen) | Target | Observed through |
| --- | --- | --- | --- |
| OD4 instance refusal | Accepted, `WriteIr` fails, `Emission.FailureInstance`, Emission-partition Unsupported at `value / 2` (`GenericGenerationDiagnosticTest.cs:30-50`) | Located acceptance-time record from the collector (A5P:839-844) | Diagnostics/facts (not native) |
| Static initialization cycle | `KIMI_E_ABORT: Static initialization cycle` at `Hello.kimi:2:12` (`StaticInitializationTest.cs:127`) | `KIMI_E_STATIC_CYCLE` at the accessing Place (SPEC §22.4.3/§22.5.2, layout-runtime §1.4) if R5 implements the SPEC | Fixture `.stderr` |
| Closure environment ≤ 8 bytes that needs destruction | Heap environment: `ContextualClosureTest` "Object" row expects 2 allocations (`ContextualClosureTest.cs:16`) | Inline (IMPL §21.1.3), 1 allocation (H) | Audit counts |
| Abort caller-location choices | Spread through `BodyLowering` syntax rules (layout-runtime §5) | SPEC §22.5 location | Fixture `.stderr`; potentially many fixtures (H) |

### 2.3 Proposed canonical form (codegen deviations)

One reader and one record, in the test helpers, modeled on R4-M60:

- `Codegen.Legacy` reads the process's codegen selection once (the selector's own reader; no second environment parse).
- `Codegen.Expect(target, legacy, item, class)` returns the legacy value while the legacy generator generates and the target
  under the MIR generator, and asserts `!Equals(target, legacy)` and a nonempty item (as `GenericOwnershipCaseTest.cs:70`
  does). `item` names the deciding record (an OD, IMPL/SPEC clause or DEC id); `class` is the ledger class (a, d, e).
- Native expectations take the same record: `ScalarEmissionTest.WriteFixture`/`EmitFixture` and
  `NativeAllocationAudit.WriteFixture` accept an optional `LegacyNative` (stdout, exit, stderr or the three counts, item, class)
  as part of the expectation value, choose the sidecars through `Codegen.Expect`, and write `<name>.deviation` (item, class,
  both values) in **both** runs. The native runner already ignores non-`.ll` files (VER:200-201).
- The comparison script (§4.7) accepts a sidecar difference between a legacy run and a MIR run only when both runs carry the
  same `.deviation` and each run's sidecars equal its side of it. Deviations are counted in the metric (§3.3).
- Removal: at R6-gen, delete `Codegen`, every `legacy:` argument and the `.deviation` writer; the targets remain. A DEC entry
  (temporary structure, removal stage R6-gen, ARCH "Temporary structures" row) records it in the first commit that uses it.

What it removes or avoids: per-test `if` branches on an environment variable; ad-hoc pending markers; the unanswerable question
"which sidecar is right" during the sweep. Rejected: a pipeline column in the pending ledger (mixes "not implemented" with
"intended difference"); per-pipeline fixture names (doubles the fixture set and breaks the cross-pipeline pairing); xUnit
traits per row (R4-M60 already found row traits invisible in xUnit v3); extending R4-M50's marker (no target value, so
nothing checks the MIR side).

Owner decision attached (OD-j in records-process): PLAN10's R5 exit admits only "match or (e)" (PLAN10:673). A legacy-wrong
(a) or intended (d) native difference either needs a D5 miscompile fix on the legacy side or an amended gate.

## 3. Moving metric and stop rule

### 3.1 Requirements

A5P §8 replaced P9 by "families completed, rules expressed, rule tests passing, open blocking rows" and a stop rule (owner
consulted after two working sessions that land no unit; ARCH:181-182 covers only the A5 plan). R5 has no metric or stop rule
(records-process OD-i). The metric must (1) be computable from the run's own evidence, (2) be bound to the commit (tracked
input, so `verify-commit.ps1` associates it), (3) move with each codegen family, (4) not be gameable by deleting tests.

### 3.2 The pending ledger

File: `tests/xUnitTest/TestData/codegen-pending.tsv` (tracked, next to `TestData/mir-diff/classification.tsv`; inside Verify's
input manifest, VER:102). Rows: `kind  key  count  reason`:

- `test  <Class>.<Method>  <n>` — n failing executed cases of that method under the codegen selector. Method granularity with
  counts, because xUnit truncates theory display names with `···` and they collide (result XML sample,
  `tests-Release-full.xml`; memory note `xunit-truncated-theory-names`).
- `fixture  <stem>  1` — a fixture the tests wrote whose native run fails at O0 or O2 under the selector.
- `reason` — the step-0 audit row or form family that will close it (for grouping; optional text).

Computation (one script, called by verify when the selector is on):

1. Tests: from `tests-<Config>-<tag>.xml`, group `//test[@result="Fail"]` by `type` and `method` (the attributes
   `Get-KimiExecutedTests` already reads, `verification-selection.ps1:40-44`).
2. Fixtures: from every `native*-fixtures/results.json` of the run (`test-scalars.ps1:46-47`: `fixture`, `ok`, `levels`),
   failing = `-not ok` or levels ≠ `O0,O2`.
3. Compare with the ledger, restricted to executed methods (Unit runs select classes): an unlisted failure or a count above the
   ledger fails the step (regression, or a new test that must be added with its reason); a listed entry that now fails fewer
   cases fails the step too unless `-UpdatePending` rewrote the file (the unit commits the shrunk file). The step passes iff
   the failing set equals the ledger exactly.
4. Write `codegen-pending.json` into the evidence: pending cases, methods, classes, fixtures; executed cases and fixtures;
   entries removed because they pass, entries removed because the method no longer executes (deleted tests), entries added
   (with "method executed in the base run" when `-PendingBase <evidence>` is given); `.deviation` count.

verify.ps1 interplay: today a failing test step sets `$failed` (`verify.ps1:85-88`) and the native step runs only after the
tests passed (`:284-285` "run them only when those tests passed"); `test-scalars.ps1:54` throws on any failed fixture. Under
the selector the test step must use the equality rule above and the native step must compare its results with the fixture rows
instead of requiring all-pass, so that fixtures of passing tests still run in the same evidence. Rejected alternatives: an
xfail mechanism inside xUnit (v3 has none; a test-identity lookup in every helper would be a wrapper without semantics); comparing
failure sets between consecutive runs without a tracked file (VER:96 does this for the ledger observer; it binds nothing to the
commit and cannot tell progress from regression).

The first ledger is generated by the selector unit's own Session (nearly every generating test fails with a located
Unsupported then; on the order of the 914 methods / 357 files that call `WriteIr`/`Validate`, tests-harness §1.3 (H)).

### 3.3 Metrics (reported per unit in the commit and per Session in evidence)

1. **Pending cases + pending fixtures**, falling only by tests that now pass (removals by deletion reported separately).
2. **Classes passing** under the selector (a class with no `test` row and none of its fixtures pending; class = feature owner,
   VER:150).
3. **Deviations** (`.deviation` count), each with a decided item; expected to stay small.
4. **Audit rows closed** (step-0 audit rows with no pending row naming them and their owning unit landed).
5. Ratchets (`ArchitectureRulesTest.cs:26-33` on origin/dev): they only forbid growth; R6-gen lowers them to 0.

Exit condition of R5 on this metric: ledger empty, every deviation decided under OD-j, native sweep of all fixtures and the 40
milestone harnesses green under the selector (PLAN10:673).

### 3.4 Stop rule

"The owner is consulted after two consecutive working sessions on R5 that land no unit; a unit lands when it adds a planned
deliverable with its first consumer (selector, keys, collector, layout, tooling) or reduces metric 1 by passing tests or
fixtures; a planning- or record-only session lands nothing; a session that lands units while metric 1 does not fall reports
why." Recorded in ARCH beside the A5 rule (ARCH:181-182) and a DEC entry (owner decision).

## 4. Cross-pipeline native evidence

### 4.1 Facts

- A Session regenerates 5,932 fixtures (latest Session above; R0 had 6,252) in about 3 minutes: 20,356 tests in 151 s, Session
  total 181 s (`summary.json` of `20261017-052809-117…`).
- R0's sweep `artifacts/verify/reduction-R0/native-baseline/results.json`: 6,252 fixtures, 12,504 executions, 4,148.5
  fixture-seconds (median 0.61 s, p90 0.73 s, max 4.3 s per fixture, both levels). Its logs were written between 07:08:18 and
  07:16:59, **≈8.7 minutes wall**, i.e. about 8 fixtures in flight (H: the `-Parallel` value is not recorded). At the
  default `-NativeParallel 4` (VER:196) the same sweep would take about 17-18 minutes (H).
- CI runs every fixture on pushes to `main`: `./scripts/verify.ps1 -Mode Session -Fixtures '*.ll' -VerifyToolchain -Name ci`
  (`.github/workflows/test.yml:64`, job timeout 90 min).

### 4.2 What `verify.ps1 -Fixtures` supports

A list of file-filter patterns ending in `.ll`; each runs as its own native step over the run's `fixtures-<Config>` directory
(`verify.ps1:283-302`); the runner verifies the input IR, builds and runs O0 and O2, and compares stdout, stderr and (without a
timeout) the exit code byte for byte with the sidecars (`test-scalar-fixture.ps1:22-27,58-68`); per fixture it records `ok`,
`levels`, `seconds` and a log in `results.json`; it is parallel and **continuing**: it collects every result and fails at the
end (`test-scalars.ps1:38-54`). `fixture-hashes.csv` covers the selected patterns only (`verify.ps1:297-301`). No script compares
two runs (`git ls-tree origin/dev scripts tests/scripts`). Selection includes only `.ll` files; sidecars never become inputs
(VER:200-201).

### 4.3 Does "both pass against the same sidecars" suffice for evidence part 3?

Yes, under four conditions, because each run is compared exactly with sidecars the same test code wrote:

1. **Same fixture set.** A test that fails under the selector writes no fixture (EmitFixture asserts first,
   `ScalarEmissionTest.cs:132-140`); missing fixtures must be explained by pending `test` rows.
2. **Byte-equal sidecars** in both runs, except fixtures carrying the same `.deviation` (§2.3).
3. **Fail-closed instrumentation** (§1.3); otherwise an audit or hook fixture "passes" without observing anything.
4. **Both native runs green at O0 and O2.** Timeout fixtures compare stdout and stderr but not the exit code
   (`test-scalar-fixture.ps1:58-66`); both runs must diverge, which is the intended equality.

Fixtures whose `.ll` is byte-identical in both runs (hand-written or helper-only IR among the 76 direct `WriteFixture` calls) are
not evidence about the MIR generator; the comparison script reports them separately.

### 4.4 `.ll` comparison for D5 part 1 and MIR part 2

- Part 1 (legacy `.ll` unchanged by an R5 or R4 unit): compare `fixtures-Release` of the unit's Session with the previous
  unit's Session. Cheap: generation is part of every Session; hashing 254 MB takes seconds (H). Added or removed fixtures are
  listed (test changes), changed bytes fail unless the unit is a declared behavior change (a D5 miscompile fix) or a test-only
  instrumentation change (§1.3 changes every audit fixture's text once).
- Part 2 (MIR `.ll` stable across restructuring commits): the same comparison on selector runs. First check determinism: two
  selector runs of the same commit must be byte-equal (a precondition the legacy path already meets, VER:121).
- Path normalization: **no fixture currently contains a path**: 0 of 5,932 `.ll` and 0 sidecars match
  `users|repos|worktree` (grep in the latest Session); test sources are named `Hello.kimi` and library sources
  `compiler://Kimi/...`. VER:121's "compare runs from the same worktree path, since the IR records source paths" applies to
  CLI-built IR (milestones, `kimi build`). The script still normalizes the repository and worktree roots in three spellings
  (raw `\`, `/`, and LLVM's `\5C` escape in `c"..."` strings) so that comparisons across worktrees stay valid.

### 4.5 Part 5: per-function O2 metrics

The native runner already writes each fixture's optimized IR to `<work>/native*/<name>.O2.ll` (`test-scalar-fixture.ps1:31-34`)
under `temp/verify/<run>/`. A post-processing step counts instructions per `define` there and stores `o2-metrics.json` in the
evidence; comparing two runs ranks fixtures by MIR/legacy growth, which picks the fixtures to time beside the O2 corpus.
No extra `opt` runs are needed.

### 4.6 Wall time and cadence

| Evidence | Cost (H) | When |
| --- | --- | --- |
| Part 1 legacy `.ll` diff | 0 extra (Session already generates) + seconds | Every R5 unit that touches shared Binding/MIR code, and every R4 unit (D5) |
| Selector Unit run + family fixtures under both generators | Unit cost × 2 | Every codegen unit |
| Selector Session (pending check) | ≈3 min | End of every R5 working session, beside the default Session |
| Full cross-pipeline sweep (`'*.ll'` × 2 + compare + 40 milestones × 2) | 2 × (3 + 9…18) min ≈ 25-40 min, milestones a few minutes more | Before each R5 group review merges to dev, at R5 exit, at the R6-gen switch |

### 4.7 Minimal tooling

1. `scripts/compare-fixtures.ps1 -Base <run> -Head <run>`: `.ll` set and bytes (normalized), sidecar set and bytes with
   `.deviation` acceptance, native `results.json` per fixture, identical-IR report; writes `fixture-comparison.json` and a
   short markdown into Head's evidence; regression script `tests/scripts/FixtureComparisonTest.ps1`; VER "Behavior-preserving
   commits" names the script instead of a hand comparison.
2. The pending ledger step (§3.2) in verify.ps1 with its regression script.
3. The O2 metrics step (§4.5), as an option of the comparison script.

## 5. Benchmarks

### 5.1 Harness facts

- `benchmark-o2-corpus.ps1` requires every pipeline report to share schema, **stage names** and input hashes with A's first
  report (`:119-125`, `:186`) and needs `EmissionLowering` and `LlvmIrWriting` for collect-plus-codegen (`:191-196`; the comment
  says "until R5 replaces them").
- A and B run in one process environment; only `KIMI_*`/`DOTNET_*` are recorded (`:131`). A is fixed at `49cb2b5d` (PLAN:35),
  whose compiler throws on any unknown `KIMI_PIPELINE` value (`Compilation.Pipeline.cs:129-134`). A selector spelled as a
  third `KIMI_PIPELINE` value would break A; a separate variable would be ignored by A only by accident.
- `CompilerPipelineBenchmark.Compile` calls `BuildMir`, `Ownership.Analyze`, `Emission.TryPrepare` and `module.WriteIr`
  directly (`CompilerPipelineBenchmark.cs:63-78`), not `Compilation.WriteIr`, so a selector in `WriteIr` would not reach it;
  the measurement asserts the split IR equals `compilation.Emission.WriteIr` (`CompilerPipelineMeasurements.cs:106-109`).

### 5.2 Missing pre-R5 baseline evidence

`artifacts/benchmarks/o2-corpus/` does not exist in the main checkout (`ls`), although commit `50548f51` says the evidence was
"copied ... into the main checkout's artifacts" and PLAN:35 points there; no worktree is at `49cb2b5d` (`git worktree list`).
What is lost is the A=B matched-run record (variance, P4) and the location of the baseline checkout; the numbers survive in
the commit message (kernel B/A 0.995-1.012; collect+codegen 68.3 / 132.4 / 262.7 ms). Because the protocol re-runs A in every
comparison (O2Corpus.md "Protocol"), retaking A=B on a fresh `49cb2b5d` checkout restores the record at about 10 minutes of
quiet machine time.

### 5.3 PRE5 §2 against the O2 corpus

| PRE5 item | Status | Proposal |
| --- | --- | --- |
| B1 legacy O2/O0 timing (arithmetic 10 inputs, object finalization, p42, Views) | Not run | Supersede by the corpus; add an `Arithmetic` kernel (checked and wrapping operations) so basic codegen is timed in the same harness |
| B2 hot paths (Array, Dictionary, string comparison/slicing, interpolation; RawPointers' 12 cases) | Partly: no string kernel, no RawPointers | Add a `Strings` kernel (comparison, slicing); owner keeps or drops RawPointers |
| B3 pipeline stages, opt/llc wall time, real workloads | Stages yes; opt/llc only inside each kernel's `buildSeconds`; no workloads | Record-only (P3): `buildSeconds` suffices; workloads optional |
| B4 name the baseline Session of the native allocation audits | Not done | Moot once audits fail closed: the test-coded counts are the P2 baseline, and the first fail-closed Session is the evidence |
| B5 VER measurement rows | Not done | Add the rows (documentation) or drop them |

The owner adopted PRE5 §2 as an R5 start condition (PRE5:12) and B2's kernels into the P1 gate (PRE5:28); A5P started R5 in
parallel without superseding PRE5 (records-process §1.3). A superseding DEC entry is needed either way.

### 5.4 What R5 needs

1. A per-variant environment in the harness (`-CandidateEnvironment NAME=VALUE`), applied to B's kernel builds and B's
   `--compiler-pipeline`, recorded per variant, with A's environment asserted free of selector variables.
2. The stage contract until R6-gen: keep the six names; under the selector B's stage 5 times instance collection and layout and
   stage 6 the MIR function codegen and text, documented in `CompilerPipeline.md`; or teach the harness a stage map (planning
   choice). Where MIR construction and A1/A2 fall for legacy acceptance + MIR codegen is decided in that unit.
3. The retaken A=B record and a recorded baseline checkout location, backed up (PRE5 D3).

## 6. Warm zero allocation

- 186 measurement sites in 170 files include generation (180 `WriteIr`, 6 `Validate`), 183 assert 0 bytes; the standard window
  and its rules are fixed (VER:167-169: no caller warm-up, no added warm-up to pass). VER:96 runs Allocation tests under the
  mir pipeline, so MIR construction and analysis already reuse their storage across rebuilds (H: latest full mir results
  found are `artifacts/verify/mir-diff/u6b-tests.xml.gz`, 2026-10-12).
- The old generator achieves this with a retained module cleared per preparation (`LlvmEmitter.cs:10,85,297-310`) and
  allocation-free text writing (stack buffers for digits, `LlvmModuleWriter.cs:711,724`; `LlvmConstantPool`).
- Consequences for MIR codegen: a retained per-`Compilation` codegen owner whose instance tables, worklist and layout cache
  clear and reuse capacity; text written through `TextWriter.Write(ReadOnlySpan<char>)` and `TryFormat` into stack buffers;
  no LINQ (ARCH's R5 rule for the Backend) or interpolation on the success path; under legacy acceptance, `WriteIr` reuses the
  compilation's MIR rather than rebuilding it fresh. The **first** codegen unit adds its warm test, and every later site leaves
  the pending ledger only at 0 bytes; pooling is part of each family's definition of done, not a final unit. A compiler
  allocation that measurably speeds generation may change an assertion by owner-approved policy (VER:167), but none should be
  planned.

## 7. Test migration cost and order

### 7.1 R6-gen

| Item | Size | Order / prerequisite |
| --- | --- | --- |
| Codegen deviations (`legacy:` values, `.deviation`, `Codegen`) | Few sites (OD4 ≈20 methods, statics, closure rows) | Deleted in the switch commit |
| Pending ledger and selector tooling | ≈300 lines (H) | Deleted in the switch commit (ledger must be empty first) |
| 113 TryPrepare methods (119 reads, 50 files; ≈2,100 lines counting trailing helpers) | 37 keep their native part; 44 also read Bodies | R/P properties covered in R5 by the classification; deletion mechanical at R6-gen |
| Defect injection into old plans (87 reads, DEC R4-M13a; heuristic scan: 71 reads / 68 methods / 55 files) | ≈1,500 lines (H) | Delete with the old Emission; MirValidator has its own tests (AGENTS: one validator per IR) |
| Emission pairing (40 reads) | inside the TryPrepare methods mostly | With the TryPrepare deletion |
| `BodyLowering.Validate*Flags` (22 lines / 8 methods) | ≈200 lines | Delete |
| Library reach (23 reads) | — | **R5**, with the collector's "generated" column (R4-M13a reason) |
| Benchmark: 4 `.Emission.` readers, stages 5-6, split-IR check | ≈50 lines | With the switch; stage names stay until A is retired |
| ArchitectureRulesTest | Emission rows to 0; zero-reference rows for `BodyLowering`, `EmissionModule`, `LlvmModuleWriter` in src, tests and `src/Benchmark` (PLAN10:674 gate, R0 classification finding 2) | Last commit of R6-gen |

DEC R4-M13a (DEC:698-711) assigns pairing and injection to "R5"; they guard the legacy generator, which stays the default until
R6-gen (D5, R4-M35), so they migrate at R6-gen. A superseding DEC note is needed (critic C12). Total R6-gen test deletion is
about -4,000 to -5,500 lines (H, method-chunk estimate); the judgment cost sits in R5's classification, so R6-gen itself is
mechanical.

### 7.2 R6-check (counts on origin/dev)

| Item | Size | Early? |
| --- | --- | --- |
| `OldRule` (19 methods / 7 files), `spec-cases` `Old` (22 variants), pair-cases `Old` (1 row) | ≈330 lines | Target sides can be added per A5 family as it lands (A5P:789 "each family migrates ... as it lands"); old sides and traits deleted at R6-check |
| Pair-cases' 21 rejecting rows reading `Ownership.Issues`/`LoanSource` (`GenericOwnershipCaseTest.cs:86-87`), `Ownership.Issues` 15 lines / 9 files, `OwnershipFailure.` 26 lines / 14 files | ≈270 lines | Needs MIR facts first: a Loan location on `Problem` (R3-7 keeps `Problem(Code, SourceRef)` minimal until R6-check unit a, R4-M3a) |
| Old control-flow entries (64 calls / 16 files, ≈1,050 lines) | — | Needs the HIR lint design (13 codes; owner named in ARCH:15, no design, critic C13); can be built and tested unpublished early |
| Bodies R6 residue (24) + 3 `Body(c)` helpers (28 calls) + old kind enums (203 lines / 52 files, 114 methods) | ≈2,700 lines | With the Analysis deletion; dump goldens where a property survives |
| `LegacyRecords`, ledger, `KIMI_MIR_DIFF`, `KIMI_PIPELINE`, `-Pipeline`, PipelineSwitchTest, `Describe` | — | Mechanical at the switch |

## 8. Corrections to the survey reports

1. tests-harness §1.4: "19 further rewrites in 11 files" → 23 rewrite lines in 12 files (14 import anchors, 9 template/entry
   anchors including SharedArcRuntimeTest:75,113,124,129 and SharedObjectRuntimeTest:81).
2. layout-runtime §1.7 and §5: "30 `DoesNotContain` on `__kimi_*`" → 33 lines in 16 files.
3. records-process §4 ("never bare `*.ll`, ~3,600 fixtures take over an hour") and the memory notes `verify-fixtures-glob`,
   `native-fixture-sweep` are stale: the runner is parallel and continuing (`test-scalars.ps1:38-54`), R0 ran 6,252 fixtures
   in ≈8.7 minutes wall, and CI runs `'*.ll'` (`test.yml:64`).
4. tests-harness §1.1: "R0 wall time with 4 workers ≈ 17-20 min (H)" → ≈8.7 min by the logs' write times.
5. tests-harness §1.1: `WriteFixture(` 321 / 77 direct → 319 = 241 + 76 + 2 definitions at `5c42b577`.
6. tests-harness §4: 184/181 → 186 sites / 170 files include generation, 183 assert 0 (method-bracket scan).
7. tests-harness §6 (at `5c42b577`) vs origin/dev: spec-cases 43 cases / 63 variants / 22 `Old` (not 41 / 58 / 18); pair-cases
   21 rejecting rows, 37 with `Run`, 1 with `Old` (not 20 / 38); `Ownership.Issues` 15 lines / 9 files (ratchet 16).
8. VER:121 / tests-harness §3.3: fixture `.ll` and sidecars contain no paths (0 of 5,932); normalization matters for CLI IR.

## 9. Owner decisions

1. EmitFixture built-ins: keep the `store i1` ban as a pipeline-neutral rule; drop the naming regex and the `select i1` check.
   Trade-off: the select check is the only always-on (non-native) proxy for branching evaluation; natively it is observed only
   for selected families per unit and on CI's full run.
2. Class rules and deletion lists (I and P-covered) as one decision; deletion in the family unit that makes the test pass under
   the selector. Trade-off: early deletion stops guarding the legacy generator's incidental shape (by definition unrequired);
   keeping them legacy-only until R6-gen adds per-path churn on ~70-120 assertions.
3. R5 metric (pending ledger) and stop rule (§3.4), recorded in ARCH and DEC.
4. Native deviation classes at R5's exit (OD-j): "match or (e)" (PLAN10:673) vs also (a)/(d) with item and clause. Trade-off:
   strict matching forces D5 fixes on the legacy generator or MIR reproducing legacy wrong output.
5. Accept a retaken A=B run in place of the lost pre-R5 evidence, and name a persistent baseline checkout.
6. Reconcile PRE5 §2 with the O2 corpus (§5.3) in a superseding DEC entry, including the `Strings`/`Arithmetic` kernels.
7. Stage names kept through R6-gen vs a harness stage map. Trade-off: kept names need a documented new meaning; a map adds
   harness code but keeps names honest.
8. (Planning session, DEC) R4-M13a's pairing/injection "R5" becomes R6-gen; the codegen deviation form and the pending ledger as
   temporary structures removed at R6-gen.
9. (Planning session) Selector form: evidence needs the R6-gen target combination (legacy acceptance + MIR codegen) and an A
   compiler that ignores it; a separate variable orthogonal to `KIMI_PIPELINE` fits both, a third `KIMI_PIPELINE` value fits
   neither without the harness change.

## 10. Risks

- Vacuous passes (fail-open audits, 33 negative names) hide MIR regressions; the pending metric cannot see them.
- Abort-location differences could turn into many `.stderr` deviations; they should be fixed against SPEC §22.5, not recorded.
- The ledger churns when R4 adds generating tests; additions must be explained per unit (base evidence comparison).
- Test deletion lowers the pending count; the script separates "passes now" from "no longer executed".
- Method-level counts can hide a swap of failing rows within one theory (rare).
- Nondeterministic MIR output would void part 2; checked once at the first codegen unit.
- Full sweeps cost 25-40 minutes; skipping them at group reviews lets stale expectations accumulate (memory `native-fixture-sweep`).
- The lost baseline checkout and the harness's single environment can silently time the wrong compiler (the harness checks the
  compiler hash, `:76-84`, which mitigates it).
- verify.ps1 grows a second acceptance rule; its regression scripts must cover both.

## Units

| ID | Title | Stage | Change (owner, files) | Prerequisites | Deliverable | Verification (focused; AGENTS row) | Net size | Diff. | Review weight | MIR hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| EV1 | IR-assertion classification and owner decision | early-R5-prereq | Planning record: candidate script + classified table (draft/Sketches on explicit instruction, or `artifacts/verify/r5-evidence/`) | none | Every IR-text, negative-name, Emission-structure, instrumentation and built-in assertion classed R/P/I/S with basis and covering fixture; decision request (class rules, I and P-covered lists, built-ins) | Documentation only: diff and reference review; second reader checks R/I rows and a P sample | 0 code; ≈450 rows | 3 | Volume and judgment; multi-line asserts missed by grep | One review + owner decision | No |
| EV2 | EmitFixture built-ins per decision | early-R5-prereq | `tests/xUnitTest/Tests/ScalarEmissionTest.cs` | EV1's built-in decision (can be asked first) | Naming and select checks removed; `store i1` kept as a neutral check | Compiler tests row: Unit `ScalarEmissionTest` + 2-3 EmitFixture classes; legacy fixtures `.ll` unchanged (EV4 or hash list); Session | -6/+3 | 1 | Assertion removal only | One review | No |
| EV3 | Fail-closed native instrumentation | early-R5-prereq | Tests: one import interposer used by `NativeAllocationAudit.cs` and the 12 rewrite files; transfer anchor required; text anchors asserted | none | Every instrumentation asserts it applied; a regression that a module with an unrewritten `HeapAlloc`/exit reference fails | Compiler tests row: Unit for the 88 audit classes + 12 files; native: one full `'*.ll'` sweep (every audit fixture's text changes); Session | +70/-35 | 2 | Mechanical, but 241 audits and native re-run | One review | No |
| EV4 | Fixture comparison script | early-R5-prereq | `scripts/compare-fixtures.ps1`, `tests/scripts/FixtureComparisonTest.ps1`, VER "Behavior-preserving commits" line | none | `.ll`/sidecar/results comparison with normalization, `.deviation` acceptance, identical-IR report | Tooling row: new regression script, `VerificationInputsTest.ps1`; smoke on two existing Sessions | +260 | 2 | Small script; normalization cases | One review | No |
| EV5 | O2 harness per-variant environment | early-R5-prereq | `src/backend/windows-x64/benchmark-o2-corpus.ps1`, `src/Benchmark/O2Corpus.md` | none | `-CandidateEnvironment`, per-variant record, A asserted selector-free | Tooling row: A=B run with an empty and a dummy candidate variable on a quiet machine | +30 | 2 | Quiet window needed | One review | No |
| EV6 | Retake A=B baseline and records | early-R5-prereq | Evidence `artifacts/benchmarks/o2-corpus/<stamp>-pre-r5-retake`, backup; PLAN §3 line (path, checkout location) | Owner accepts the retake (decision 5) | Restored P4 record, recorded baseline checkout | Measurement protocol (O2Corpus.md); PLAN record-only | 0 code, ±1 PLAN line | 1 | Procedure only | None (Q6(3) record) | No |
| EV7 | Corpus kernels per PRE5 reconciliation | early-R5-prereq | `src/Benchmark/Kimi/Corpus/{Strings,Arithmetic}.kimi`, expected checksums in the harness, O2Corpus.md | Owner decision 6 | Kernels timed A/B against `49cb2b5d` | Benchmark row: Session with whole-solution build; A=B run | +150 | 2 | Kernels must be supported by `49cb2b5d` and deterministic | One review | No |
| M1 | Codegen pending ledger in Verify | R5 | `scripts/verify.ps1` (selector switch, `-Pending`, `-UpdatePending`), a ledger script beside `verification-selection.ps1`, `tests/scripts/CodegenPendingTest.ps1`, `tests/xUnitTest/TestData/codegen-pending.tsv`, VER rows, summary fields | The codegen selector unit (first consumer; no stub) | Exact-equality test and native steps under the selector; `codegen-pending.json` metric | Tooling row: `VerificationSelectionTest.ps1`, new regression, `NativeFixtureRunnerTest.ps1`; Session (managed pipeline affected) | +260 + data file | 3 | Two acceptance rules in verify; count semantics; native step tolerance | One review (metric/stop rule: owner, decision 3) | No |
| M2 | Codegen deviation form | R5 | Test helpers (`Codegen`, `LegacyNative`), `ScalarEmissionTest.WriteFixture/EmitFixture`, `NativeAllocationAudit`; DEC entry; EV4 reads `.deviation` | Selector; EV4; first diverging test | Canonical per-path native/diagnostic expectation, removed at R6-gen | Compiler tests row: Unit for the first consumer + helper well-formedness; Session | +70 | 2 | Small; must not become a second mode | One review + planning DEC (temporary structure) | No |
| M3 | Generated-code requirement tests, neutral R matchers | R5 (per family) | Tests of A5P §9.1 list (closure env ≤8 bytes, 1,024-byte buffer, zero-sized/ABI slots/Array helpers, rc/arc order) and R rows from EV1 through one neutral IR-matcher helper | EV1; the family's codegen unit | Each requirement observed for both generators | Compiler tests row + the family's fixtures under both generators | ±200 per family | 3 | Re-spelling required properties without old names | Shared family group review (Q6(2)) | No |
| M4 | I and P-covered deletions per family | R5 (per family) | Tests listed by EV1 for that family | Owner decision 2; family unit | Tests leave the pending ledger without weakening required checks | Compiler tests row (same unit as the family) | Negative (≈-150 total, H) | 1 | Mechanical under an approved list | With the family group review | No |
| M5 | Library-reach Bodies reads to collector observations | R5 | 23 test reads (R4-M13a) | Collector with "generated" column | `Ownership.Bodies` ratchet 183 → 160 | Compiler tests row; Session | ±100 | 2 | Depends on collector API | One review | No (tests only) |
| M6 | OD4 test rewrite | R5 | ≈20 methods / 10 files (GenericGenerationDiagnosticTest and 9 others) with M2 | Collector unit and its OD4 design (partition, requirement) | Instance refusals asserted at acceptance on the MIR side, legacy recorded | Compiler tests row; diagnostic snapshot if partitions move | ±250 | 3 | Waits on cross-phase OD4 design | Inside the collector's full process | Depends on collector (MirAnalysis driver hold if it lands there) |
| M7 | O2 per-function metrics | R5 | Option of EV4 reading `<work>/native*/*.O2.ll` | EV4; first codegen families | `o2-metrics.json`, ranked fixtures to time | Tooling row: regression case in EV4's script | +90 | 2 | Text parsing of optimized IR | One review | No |
| M8 | Compiler pipeline benchmark under the selector | R5 (late) | `src/Benchmark/Benchmarks/CompilerPipelineBenchmark.cs`, `CompilerPipelineMeasurements.cs`, `CompilerPipeline.md` | MIR codegen covers the generic shape; decision 7 | Stages 5-6 time MIR codegen; equality check against public `WriteIr` under the same selector | Benchmark row: Session with whole-solution build; `--compiler-pipeline-check`; A/B run | ±50 | 3 | Codegen API and stage boundaries | One review | No |
| M9 | Cross-pipeline sweeps at group reviews and exit | R5 | Evidence only | M1, EV3, EV4 | Parts 1-4 green, pending empty at exit | `-Fixtures '*.ll'` and 40 milestones under both generators; compare script | 0 | 2 | 25-40 min per sweep | None (evidence) | No |
| G1 | Codegen switch and removal of the R5 scaffolding | R6-gen | Selector default, M1 tooling and ledger, M2 form and every `legacy:` value; DEC/ARCH rows | R5 exit (empty ledger, decided deviations, O2 gate) | MIR codegen default; scaffolding deleted | Session + `'*.ll'` + all milestones + diagnostic snapshot + O2 A/B | -400 tests/tooling (H) | 4 | Cross-phase switch | Full process + final review | Possibly (MIR mandatory in every build, critic 4.4) |
| G2 | Delete Emission-structure tests | R6-gen | 113 TryPrepare methods (keep 37 native parts), 8 Validate-flag methods, `EmissionOpcode` reads; ratchets to 0 | G1; EV1's R/P properties covered (M3) | No test reads `EmissionModule`/`BodyLowering` | Compiler tests row; Session | ≈-2,300 (H) | 2 | Volume, little judgment | R6-gen group review (Q6(2)) | No |
| G3 | Delete old-plan injection and pairing tests | R6-gen | 87 injection + 40 pairing reads (R4-M13a superseded by DEC note) | G1 | `Ownership.Bodies` ratchet → 24 (R6-check residue) | Compiler tests row; Session | ≈-1,800 (H) | 1 | Mechanical | R6-gen group review | No |
| G4 | Benchmark readers and zero-reference rows | R6-gen | 4 Benchmark `.Emission.` readers, split-IR check; ArchitectureRulesTest rows for `BodyLowering`, `EmissionModule`, `LlvmModuleWriter` over src, tests, src/Benchmark | G2, G3, Emission deletion | PLAN10:674 gate for Emission | Benchmark row: Session with whole-solution build; `ArchitectureRulesTest` | -40/+6 | 1 | Small | R6-gen group review | No |
| C1 | OldRule targets per family, removal at R6-check | R6-check (target sides early, per A5 family) | 19 OldRule methods, 22 spec-cases `Old`, 1 pair-cases `Old`, VerifiedSpecExampleTest old side | Each A5 family landed; R6-check switch for removal | Tests assert the revised rules only | Compiler tests row; diagnostic snapshot at the switch | ≈-250 (H) | 2 | Mechanical after families | One review per family (A5 group), switch group | No |
| C2 | Loan facts on Problem; Issues/OwnershipFailure migration | R6-check | `Problem` shape (R3-7, R4-M3a unit a); 21 pair-cases rows, 15 `Ownership.Issues`, 26 `OwnershipFailure.` lines | A5 families publishing Loan holders | Pair-cases judged on Problem facts | Compiler tests row; snapshot; Session | ±300 | 3 | Shared representation change | Full process | Yes if `MirReport`/`Problem` live in Mir |
| C3 | Control-flow entry calls to HIR lints | R6-check (lint pass early, unpublished) | 64 calls / 16 files; Binding lint owner (ARCH:15) | HIR lint design for 13 codes | No test calls `AnalyzeControlFlow`/`ControlFlowAnalysis.Analyze` | Compiler tests row; snapshot | ±600 (H) | 3 | Cross-phase move without a design yet | Full process | No |
| C4 | Old Analysis internals in tests | R6-check | Bodies residue 24, 3 `Body(c)` helpers (28 calls), old kind enums (114 methods / 52 files), LegacyRecords/ledger/`KIMI_PIPELINE`/PipelineSwitchTest | R6-check switch | Ownership ratchets to 0; zero-reference rows for `OwnershipAnalysis`/`OwnershipBody` | Compiler tests row; Session | ≈-3,000 (H) | 2 | Volume | R6-check group review | No |
